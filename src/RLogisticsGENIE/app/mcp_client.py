"""In-repo MCP client — connects to GENIE stdio MCP server for agents/tests."""

from __future__ import annotations

import json
import sys
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any, AsyncIterator

from mcp import ClientSession, StdioServerParameters, types
from mcp.client.stdio import stdio_client


def _server_params() -> StdioServerParameters:
    """Spawn `python -m app.mcp_stdio` with GENIE package on PYTHONPATH."""
    genie_root = Path(__file__).resolve().parents[1]
    return StdioServerParameters(
        command=sys.executable,
        args=["-m", "app.mcp_stdio"],
        cwd=str(genie_root),
        env=None,
    )


@asynccontextmanager
async def open_session() -> AsyncIterator[ClientSession]:
    params = _server_params()
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            yield session


async def list_mcp_tools() -> list[dict[str, Any]]:
    async with open_session() as session:
        result = await session.list_tools()
        return [
            {
                "name": t.name,
                "description": t.description,
                "inputSchema": getattr(t, "inputSchema", None) or getattr(t, "input_schema", {}) or {},
            }
            for t in result.tools
        ]


async def call_mcp_tool(name: str, arguments: dict[str, Any] | None = None) -> dict[str, Any]:
    async with open_session() as session:
        result = await session.call_tool(name, arguments or {})
        texts = []
        for block in result.content or []:
            if isinstance(block, types.TextContent) or getattr(block, "type", None) == "text":
                texts.append(getattr(block, "text", str(block)))
        structured = getattr(result, "structuredContent", None) or getattr(result, "structured_content", None)
        return {
            "ok": not bool(getattr(result, "isError", getattr(result, "is_error", False))),
            "text": "\n".join(texts),
            "structured": structured,
            "raw_text": texts,
        }


def parse_tool_json(payload: dict[str, Any]) -> Any:
    """Best-effort parse of tool text as JSON."""
    if payload.get("structured") is not None:
        return payload["structured"]
    text = payload.get("text") or ""
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return text
