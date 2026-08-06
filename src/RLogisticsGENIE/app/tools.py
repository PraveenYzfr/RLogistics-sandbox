"""Shared GENIE tool implementations — used by HTTP /v1/tools and MCP stdio server."""

from __future__ import annotations

import json
from typing import Any

from app.core_client import RLogisticsClient
from app.graphs import run_intake_assist, run_quote_cycle
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


async def call_tool(name: str, args: dict[str, Any] | None = None) -> Any:
    """Dispatch a tool by name. Single source of truth for HTTP + MCP."""
    args = args or {}
    client = RLogisticsClient()
    rag = ensure_rag_indexed()
    try:
        if name == "get_request":
            return await client.get_request(int(args["request_id"]))
        if name == "list_requests":
            return await client.list_requests(args.get("status"))
        if name == "intake_assist":
            req = await client.get_request(int(args["request_id"]))
            q = f"{req.get('site')} {req.get('dispositionType')} {req.get('requestType')} policy"
            hits = rag.search(q)
            return run_intake_assist(req, hits)
        if name == "recommend_vendors":
            req = await client.get_request(int(args["request_id"]))
            vendors = await client.list_vendors()
            return run_quote_cycle(req, vendors)
        if name == "parse_quote":
            return parse_quote_email(args.get("body", ""), args.get("request_number"))
        if name == "rag_search":
            return rag.search(args.get("query", ""), int(args.get("top_k", 4)))
        if name == "send_vendor_quotes":
            return await client.send_vendor_quotes(int(args["request_id"]))
        if name == "send_clarification":
            return await client.send_clarification(int(args["request_id"]), args["question"])
        return {"error": f"unknown tool {name}"}
    finally:
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
