from __future__ import annotations

import hashlib
import time
import uuid
from contextlib import asynccontextmanager
from typing import Any

from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

from app.config import settings
from app.core_client import RLogisticsClient, RedisCache, cache_key
from app.embeddings import ENTERPRISE_EMBEDDING_GUIDE
from app.eval_store import get_eval_store
from app.graphs import run_intake_assist, run_quote_cycle, try_langgraph_compile
from app.limits import get_rate_guard
from app.llm import llm_status
from app.mcp_http import build_mcp_http_app, get_session_manager, mcp_http_status
from app.observability import (
    caller_var,
    correlation_id_var,
    estimate_tokens,
    get_usage_store,
    record_usage,
    utc_day,
)
from app.rag import get_shared_rag, load_kb, embedding_status
from app.skills import parse_quote_email, status_narrative
from app.tools import call_tool, ensure_rag_indexed, list_tools
from app.agents import run_supervisor

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


class EvalCreateBody(BaseModel):
    skill: str
    input: str
    output: str
    request_id: int | None = None
    request_number: str | None = None
    run_judge: bool = True


class SmeScoreBody(BaseModel):
    score_0_to_5: float = Field(ge=0, le=5)
    passed: bool
    notes: str = ""
    reviewer: str = "sme"


rag = get_shared_rag()
redis_cache = RedisCache()
_pending_intake: dict[int, dict[str, Any]] = {}


@asynccontextmanager
async def lifespan(app: FastAPI):
    ensure_rag_indexed(rag)
    try_langgraph_compile()
    if settings.mcp_http_enabled:
        manager = get_session_manager()
        async with manager.run():
            yield
    else:
        yield


app = FastAPI(
    title="RLogisticsGENIE",
    version="1.3.0",
    description="GenAI sidecar — multi-agent, dual-tier LLM, MCP HTTP+stdio, RAG",
    lifespan=lifespan,
)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)
if settings.mcp_http_enabled:
    app.mount("/mcp", build_mcp_http_app())


def _caller_from_request(request: Request) -> str:
    key = request.headers.get("X-Api-Key") or ""
    if key:
        return "key:" + hashlib.sha256(key.encode()).hexdigest()[:12]
    path = request.url.path or ""
    if path.startswith("/v1/mcp"):
        return "mcp"
    return "anonymous"


@app.middleware("http")
async def correlation_and_limits(request: Request, call_next):
    cid = request.headers.get("X-Correlation-Id") or uuid.uuid4().hex
    caller = _caller_from_request(request)
    correlation_id_var.set(cid)
    caller_var.set(caller)

    path = request.url.path or ""
    if path.startswith("/mcp"):
        caller = "mcp-http"
        caller_var.set(caller)
    if path not in ("/health", "/docs", "/openapi.json", "/redoc") and not path.startswith("/mcp"):
        decision = get_rate_guard().check_request(caller, record=True)
        if not decision.allowed:
            return JSONResponse(
                status_code=429,
                content={"error": "rate_or_spend_limit", **decision.to_dict()},
                headers={"X-Correlation-Id": cid},
            )

    t0 = time.perf_counter()
    try:
        response: Response = await call_next(request)
    except Exception:
        raise
    latency = (time.perf_counter() - t0) * 1000
    response.headers["X-Correlation-Id"] = cid
    if path.startswith("/v1/") and path not in (
        "/v1/observability/summary",
        "/v1/observability/events",
        "/v1/observability/limits",
    ):
        record_usage(
            operation=f"http:{request.method}:{path}",
            provider="http",
            model="",
            input_tokens=0,
            output_tokens=0,
            latency_ms=latency,
            ok=response.status_code < 500,
            error=None if response.status_code < 400 else f"status={response.status_code}",
        )
    return response


def _mcp_status() -> str:
    try:
        from app.mcp_stdio import build_server

        build_server()
        return "sdk-stdio"
    except Exception as ex:
        return f"unavailable: {ex}"


@app.get("/health")
async def health():
    usage = get_usage_store().summary(day=utc_day())
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
        "llm": llm_status(),
        "llm_mode": settings.genie_llm_mode,
        "langgraph": try_langgraph_compile(),
        "mcp": _mcp_status(),
        "mcp_http": mcp_http_status(),
        "agents": {"mode": "multi", "specialists": ["intake", "compliance", "vendor"], "supervisor": True},
        "usage": {
            "today_calls": usage.get("calls"),
            "today_est_usd": usage.get("est_cost_usd"),
            "backend": usage.get("backend"),
        },
        "limits": get_rate_guard().status(caller_var.get() or "anonymous"),
    }


@app.get("/v1/observability/summary")
async def obs_summary(day: str | None = None, caller: str | None = None):
    d = None if not day or day == "today" else day
    summary = get_usage_store().summary(day=d, caller=caller)
    who = caller or caller_var.get() or "anonymous"
    limits = get_rate_guard().status(who)
    return {**summary, "limits": limits}


@app.get("/v1/observability/events")
async def obs_events(limit: int = 100, day: str | None = None):
    d = None if not day or day == "today" else day
    return {"day": d or utc_day(), "events": get_usage_store().events(limit=limit, day=d)}


@app.get("/v1/observability/limits")
async def obs_limits(caller: str | None = None):
    who = caller or caller_var.get() or "anonymous"
    return get_rate_guard().status(who)


@app.post("/v1/eval/cases")
async def eval_create(body: EvalCreateBody):
    case = get_eval_store().create_case(
        skill=body.skill,
        input_text=body.input,
        output_text=body.output,
        request_id=body.request_id,
        request_number=body.request_number,
        caller=caller_var.get() or "http",
        run_judge=body.run_judge,
    )
    return case


@app.get("/v1/eval/cases")
async def eval_list(pending_sme: bool | None = None, limit: int = 100):
    return {"cases": get_eval_store().list_cases(pending_sme=pending_sme, limit=limit)}


@app.get("/v1/eval/cases/{case_id}")
async def eval_get(case_id: str):
    case = get_eval_store().get(case_id)
    if not case:
        raise HTTPException(404, "case not found")
    return case


@app.post("/v1/eval/cases/{case_id}/sme")
async def eval_sme(case_id: str, body: SmeScoreBody):
    try:
        return get_eval_store().submit_sme(
            case_id,
            score_0_to_5=body.score_0_to_5,
            passed=body.passed,
            notes=body.notes,
            reviewer=body.reviewer,
        )
    except KeyError:
        raise HTTPException(404, "case not found") from None


@app.get("/v1/eval/metrics")
async def eval_metrics():
    return get_eval_store().metrics()


@app.get("/v1/llm/status")
async def llm_status_endpoint():
    return llm_status()


@app.post("/v1/agents/run/{request_id}")
async def agents_run(request_id: int):
    """Multi-agent supervisor run — HITL gates on all Core writes."""
    try:
        return await run_supervisor(request_id)
    except Exception as ex:
        raise HTTPException(502, f"agent run failed: {ex}") from ex


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
        if settings.eval_auto_capture:
            get_eval_store().create_case(
                skill="intake_assist",
                input_text=str(req.get("requestNumber") or request_id),
                output_text=str(state.get("clarification_draft") or state.get("summary") or state),
                request_id=request_id,
                request_number=str(req.get("requestNumber") or ""),
                caller=caller_var.get() or "http",
            )
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
            state["result"] = {
                "phase": "posted",
                "rlogistics": posted.get("requestNumber") or posted.get("id"),
            }
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
    parsed = parse_quote_email(body.body, body.request_number)
    if settings.eval_auto_capture:
        get_eval_store().create_case(
            skill="parse_quote",
            input_text=body.body,
            output_text=str(parsed),
            request_number=body.request_number,
            caller=caller_var.get() or "http",
        )
    record_usage(
        operation="parse_quote",
        provider="offline",
        model="skill",
        input_tokens=estimate_tokens(body.body),
        output_tokens=estimate_tokens(str(parsed)),
        ok=True,
    )
    return parsed


@app.get("/v1/rag/search")
async def rag_search(q: str, top_k: int = 4):
    t0 = time.perf_counter()
    hits = rag.search(q, top_k)
    record_usage(
        operation="rag_search",
        provider=rag.backend,
        model="",
        input_tokens=estimate_tokens(q),
        output_tokens=estimate_tokens(str(hits)),
        latency_ms=(time.perf_counter() - t0) * 1000,
        ok=True,
    )
    return {"query": q, "backend": rag.backend, "hits": hits}


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
