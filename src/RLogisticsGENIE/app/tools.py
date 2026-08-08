"""Shared GENIE tool implementations — used by HTTP /v1/tools and MCP stdio server."""

from __future__ import annotations

import json
import time
from typing import Any

from app.config import settings
from app.core_client import RLogisticsClient
from app.graphs import run_intake_assist, run_quote_cycle
from app.observability import estimate_tokens, record_usage
from app.rag import RagStore, get_shared_rag, load_kb
from app.skills import parse_quote_email

_KB_LOADED = False


def ensure_rag_indexed(rag: RagStore | None = None) -> RagStore:
    """Load KB into the shared (or provided) RagStore once."""
    global _KB_LOADED
    store = rag or get_shared_rag()
    if not _KB_LOADED or store.chunk_count == 0:
        docs = load_kb()
        if docs:
            store.index_documents(docs)
            _KB_LOADED = True
    return store


def list_tool_specs() -> list[dict[str, Any]]:
    return [
        {
            "name": "get_request",
            "description": "Load disposal request from RLogistics Core by id",
            "inputSchema": {
                "type": "object",
                "properties": {"request_id": {"type": "integer"}},
                "required": ["request_id"],
            },
        },
        {
            "name": "list_requests",
            "description": "List requests from RLogistics Core (optional status filter)",
            "inputSchema": {
                "type": "object",
                "properties": {"status": {"type": "string"}},
            },
        },
        {
            "name": "intake_assist",
            "description": "Completeness + RAG + summary + clarification draft (HITL)",
            "inputSchema": {
                "type": "object",
                "properties": {"request_id": {"type": "integer"}},
                "required": ["request_id"],
            },
        },
        {
            "name": "recommend_vendors",
            "description": "Vendor recommendation for a request (quote cycle)",
            "inputSchema": {
                "type": "object",
                "properties": {"request_id": {"type": "integer"}},
                "required": ["request_id"],
            },
        },
        {
            "name": "parse_quote",
            "description": "Parse vendor quote email body into structured fields",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "body": {"type": "string"},
                    "request_number": {"type": "string"},
                },
                "required": ["body"],
            },
        },
        {
            "name": "rag_search",
            "description": "Search SOP / policy knowledge base (chunked RAG)",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "query": {"type": "string"},
                    "top_k": {"type": "integer", "default": 4},
                },
                "required": ["query"],
            },
        },
        {
            "name": "run_multi_agent",
            "description": "Supervisor+Intake/Compliance/Vendor agents for a request (HITL on writes)",
            "inputSchema": {
                "type": "object",
                "properties": {"request_id": {"type": "integer"}},
                "required": ["request_id"],
            },
        },
        {
            "name": "send_vendor_quotes",
            "description": "Trigger Core vendor-quote emails for a request",
            "inputSchema": {
                "type": "object",
                "properties": {"request_id": {"type": "integer"}},
                "required": ["request_id"],
            },
        },
        {
            "name": "send_clarification",
            "description": "Post clarification question to Core (HITL confirmed)",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "request_id": {"type": "integer"},
                    "question": {"type": "string"},
                },
                "required": ["request_id", "question"],
            },
        },
    ]


def list_tools() -> list[dict[str, str]]:
    """HTTP-friendly short catalog."""
    return [{"name": t["name"], "description": t["description"]} for t in list_tool_specs()]


def _maybe_capture_eval(
    skill: str,
    input_text: str,
    output: Any,
    *,
    request_id: int | None = None,
    request_number: str | None = None,
) -> None:
    if not settings.eval_auto_capture:
        return
    try:
        from app.eval_store import get_eval_store

        out_text = output if isinstance(output, str) else json.dumps(output, default=str)
        get_eval_store().create_case(
            skill=skill,
            input_text=input_text,
            output_text=out_text,
            request_id=request_id,
            request_number=request_number,
            caller="tool",
            run_judge=True,
        )
    except Exception:
        pass


async def call_tool(name: str, args: dict[str, Any] | None = None) -> Any:
    """Dispatch a tool by name. Single source of truth for HTTP + MCP."""
    args = args or {}
    client = RLogisticsClient()
    rag = ensure_rag_indexed()
    t0 = time.perf_counter()
    ok = True
    err: str | None = None
    result: Any = None
    try:
        if name == "get_request":
            result = await client.get_request(int(args["request_id"]))
        elif name == "list_requests":
            result = await client.list_requests(args.get("status"))
        elif name == "intake_assist":
            req = await client.get_request(int(args["request_id"]))
            q = f"{req.get('site')} {req.get('dispositionType')} {req.get('requestType')} policy"
            hits = rag.search(q)
            result = run_intake_assist(req, hits)
            draft = ""
            if isinstance(result, dict):
                draft = str(result.get("clarification_draft") or result.get("summary") or result)
            _maybe_capture_eval(
                "intake_assist",
                q,
                draft or result,
                request_id=int(args["request_id"]),
                request_number=str(req.get("requestNumber") or ""),
            )
        elif name == "recommend_vendors":
            req = await client.get_request(int(args["request_id"]))
            vendors = await client.list_vendors()
            result = run_quote_cycle(req, vendors)
        elif name == "parse_quote":
            body = args.get("body", "")
            result = parse_quote_email(body, args.get("request_number"))
            _maybe_capture_eval(
                "parse_quote",
                body,
                result,
                request_number=args.get("request_number"),
            )
        elif name == "rag_search":
            query = args.get("query", "")
            result = rag.search(query, int(args.get("top_k", 4)))
            _maybe_capture_eval("rag_answer", query, result)
        elif name == "run_multi_agent":
            from app.agents import run_supervisor

            result = await run_supervisor(int(args["request_id"]))
        elif name == "send_vendor_quotes":
            result = await client.send_vendor_quotes(int(args["request_id"]))
        elif name == "send_clarification":
            result = await client.send_clarification(int(args["request_id"]), args["question"])
        else:
            result = {"error": f"unknown tool {name}"}
            ok = False
            err = str(result["error"])
        return result
    except Exception as ex:
        ok = False
        err = str(ex)
        raise
    finally:
        latency = (time.perf_counter() - t0) * 1000
        in_tok = estimate_tokens(json.dumps(args, default=str))
        out_tok = estimate_tokens(json.dumps(result, default=str) if result is not None else "")
        record_usage(
            operation=f"tool:{name}",
            provider=rag.backend if name == "rag_search" else "core-http",
            model="",
            input_tokens=in_tok,
            output_tokens=out_tok,
            latency_ms=latency,
            ok=ok,
            error=err,
        )
        await client.close()


def result_to_text(result: Any) -> str:
    if isinstance(result, str):
        return result
    return json.dumps(result, default=str, indent=2)


# Back-compat aliases used by older imports
async def dispatch_tool(name: str, args: dict[str, Any]) -> Any:
    return await call_tool(name, args)


def load_kb_once(rag: RagStore | None = None) -> bool:
    store = ensure_rag_indexed(rag)
    return store.chunk_count > 0
