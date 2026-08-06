from __future__ import annotations

from contextlib import asynccontextmanager
from typing import Any

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field

from app.config import settings
from app.core_client import RLogisticsClient, RedisCache, cache_key
from app.graphs import run_intake_assist, run_quote_cycle, try_langgraph_compile
from app.rag import get_shared_rag, load_kb, embedding_status
from app.skills import parse_quote_email, status_narrative
from app.tools import call_tool, ensure_rag_indexed, list_tools
from app.embeddings import ENTERPRISE_EMBEDDING_GUIDE


class ApproveBody(BaseModel):
    post_to_mdt: bool = True
    question: str | None = None


class ParseQuoteBody(BaseModel):
    body: str
    request_number: str | None = None


class ToolCallBody(BaseModel):
    name: str
    args: dict[str, Any] = Field(default_factory=dict)


class McpCallBody(BaseModel):
    name: str
    args: dict[str, Any] = Field(default_factory=dict)


rag = get_shared_rag()
redis_cache = RedisCache()
_pending_intake: dict[int, dict[str, Any]] = {}


@asynccontextmanager
async def lifespan(app: FastAPI):
    ensure_rag_indexed(rag)
    try_langgraph_compile()
    yield


app = FastAPI(
    title="RLogisticsGENIE",
    version="1.1.0",
    description="GenAI sidecar for RLogistics Core — LangGraph, chunked RAG, MCP stdio + HTTP tools",
    lifespan=lifespan,
)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


def _mcp_status() -> str:
    try:
        from app.mcp_stdio import build_server

        build_server()
        return "sdk-stdio"
    except Exception as ex:
        return f"unavailable: {ex}"


@app.get("/health")
async def health():
    return {
        "ok": True,
        "rlogistics": settings.rlogistics_url,
        "redis": redis_cache.available,
        "qdrant": rag._qdrant is not None,
        "rag": {
            **embedding_status(),
            "kb_docs": len(load_kb()),
            "qdrant": rag._qdrant is not None,
        },
        "embeddings_enterprise": ENTERPRISE_EMBEDDING_GUIDE,
        "llm_mode": settings.genie_llm_mode,
        "langgraph": try_langgraph_compile(),
        "mcp": _mcp_status(),
    }


@app.get("/v1/tools")
async def tools():
    return {"tools": list_tools()}


@app.post("/v1/tools/call")
async def tools_call(body: ToolCallBody):
    return await call_tool(body.name, body.args)


@app.get("/v1/mcp/tools")
async def mcp_tools_via_client():
    """Smoke: list tools through in-repo MCP client (spawns stdio server)."""
    try:
        from app.mcp_client import list_mcp_tools

        return {"tools": await list_mcp_tools()}
    except Exception as ex:
        raise HTTPException(502, f"MCP client error: {ex}") from ex


@app.post("/v1/mcp/call")
async def mcp_call_via_client(body: McpCallBody):
    """Lab smoke: call a tool through MCP client → stdio server."""
    try:
        from app.mcp_client import call_mcp_tool, parse_tool_json

        payload = await call_mcp_tool(body.name, body.args)
        return {"mcp": payload, "parsed": parse_tool_json(payload)}
    except Exception as ex:
        raise HTTPException(502, f"MCP client error: {ex}") from ex


@app.get("/v1/completeness/{request_id}")
async def completeness(request_id: int):
    ck = cache_key("completeness", str(request_id))
    cached = redis_cache.get_json(ck)
    if cached:
        cached["cache"] = "hit"
        return cached
    client = RLogisticsClient()
    try:
        req = await client.get_request(request_id)
        from app.skills import completeness_score

        result = completeness_score(req)
        result["requestNumber"] = req.get("requestNumber")
        result["cache"] = "miss"
        redis_cache.set_json(ck, result)
        return result
    except Exception as ex:
        raise HTTPException(status_code=502, detail=f"RLogistics Core error: {ex}") from ex
    finally:
        await client.close()


@app.get("/v1/summarize/{request_id}")
async def summarize(request_id: int):
    client = RLogisticsClient()
    try:
        req = await client.get_request(request_id)
        hits = rag.search(f"{req.get('requestType')} {req.get('dispositionType')} policy")
        return summarize_request_safe(req, hits)
    except Exception as ex:
        raise HTTPException(status_code=502, detail=f"RLogistics Core error: {ex}") from ex
    finally:
        await client.close()


def summarize_request_safe(req, hits):
    from app.skills import summarize_request

    return summarize_request(req, hits)


@app.get("/v1/intake/{request_id}")
async def intake(request_id: int):
    client = RLogisticsClient()
    try:
        req = await client.get_request(request_id)
        hits = rag.search(f"{req.get('site')} {req.get('dispositionType')} {req.get('requestType')}")
        state = run_intake_assist(req, hits)
        _pending_intake[request_id] = state
        return state
    except Exception as ex:
        raise HTTPException(status_code=502, detail=str(ex)) from ex
    finally:
        await client.close()


@app.post("/v1/intake/{request_id}/approve")
async def intake_approve(request_id: int, body: ApproveBody):
    """HITL: post clarification draft to RLogistics Core after coordinator approval."""
    state = _pending_intake.get(request_id)
    client = RLogisticsClient()
    try:
        if state is None:
            req = await client.get_request(request_id)
            hits = rag.search(str(req.get("site", "")))
            state = run_intake_assist(req, hits)
        q = body.question or state.get("clarification_draft")
        if not q:
            raise HTTPException(400, "No clarification question")
        if body.post_to_mdt:
            posted = await client.send_clarification(request_id, q)
            state["approved"] = True
            state["posted"] = True
            state["result"] = {"phase": "posted", "rlogistics": posted.get("requestNumber") or posted.get("id")}
            redis_cache.delete(cache_key("completeness", str(request_id)))
        else:
            state["approved"] = True
            state["posted"] = False
            state["result"] = {"phase": "approved_not_posted", "question": q}
        _pending_intake[request_id] = state
        return state
    except HTTPException:
        raise
    except Exception as ex:
        raise HTTPException(502, str(ex)) from ex
    finally:
        await client.close()


@app.get("/v1/vendors/recommend/{request_id}")
async def vendors_recommend(request_id: int):
    client = RLogisticsClient()
    try:
        req = await client.get_request(request_id)
        vendors = await client.list_vendors()
        return run_quote_cycle(req, vendors)
    except Exception as ex:
        raise HTTPException(502, str(ex)) from ex
    finally:
        await client.close()


@app.post("/v1/quotes/{request_id}/send")
async def quotes_send(request_id: int):
    client = RLogisticsClient()
    try:
        return await client.send_vendor_quotes(request_id)
    except Exception as ex:
        raise HTTPException(502, str(ex)) from ex
    finally:
        await client.close()


@app.post("/v1/quotes/parse")
async def quotes_parse(body: ParseQuoteBody):
    return parse_quote_email(body.body, body.request_number)


@app.get("/v1/rag/search")
async def rag_search(q: str, top_k: int = 4):
    return {"query": q, "backend": rag.backend, "hits": rag.search(q, top_k)}


@app.post("/v1/reminders/{request_id}")
async def reminders(request_id: int):
    client = RLogisticsClient()
    try:
        return await client.send_return_reminder(request_id)
    except Exception as ex:
        raise HTTPException(502, str(ex)) from ex
    finally:
        await client.close()


@app.get("/v1/narrative/{request_id}")
async def narrative(request_id: int, from_status: str = "Assigned", to_status: str = "PickupScheduled"):
    client = RLogisticsClient()
    try:
        req = await client.get_request(request_id)
        return {"narrative": status_narrative(req, from_status, to_status)}
    except Exception as ex:
        raise HTTPException(502, str(ex)) from ex
    finally:
        await client.close()
