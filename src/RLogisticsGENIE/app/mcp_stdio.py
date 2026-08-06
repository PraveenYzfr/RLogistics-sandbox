"""Official MCP stdio server for RLogistics GENIE tools (Cursor-attachable)."""

from __future__ import annotations

import json
import logging
from typing import Any

import anyio
from mcp.server.lowlevel.server import Server
from mcp.server.stdio import stdio_server
from mcp import types

from app.tools import call_tool, list_tool_specs, ensure_rag_indexed, result_to_text

log = logging.getLogger("rlogistics.genie.mcp")


async def _on_list_tools(
    ctx: Any,
    params: types.PaginatedRequestParams | None,
) -> types.ListToolsResult:
    tools = [
        types.Tool(
            name=spec["name"],
            description=spec["description"],
            input_schema=spec.get("inputSchema") or {"type": "object", "properties": {}},
        )
        for spec in list_tool_specs()
    ]
    return types.ListToolsResult(tools=tools)


async def _on_call_tool(
    ctx: Any,
    params: types.CallToolRequestParams,
) -> types.CallToolResult:
    try:
        result = await call_tool(params.name, dict(params.arguments or {}))
        if isinstance(result, dict) and result.get("error"):
            return types.CallToolResult(
                content=[types.TextContent(type="text", text=str(result["error"]))],
                is_error=True,
            )
        text = result_to_text(result)
        structured = result if isinstance(result, dict) else {"result": result}
        return types.CallToolResult(
            content=[types.TextContent(type="text", text=text)],
            structured_content=structured,
        )
    except Exception as ex:
        log.exception("tool %s failed", params.name)
        return types.CallToolResult(
            content=[types.TextContent(type="text", text=str(ex))],
            is_error=True,
        )


def build_server() -> Server[Any]:
    ensure_rag_indexed()
    return Server(
        "rlogistics-genie",
        version="1.0.0",
        title="RLogisticsGENIE",
        description="RLogistics reverse-logistics GenAI tools (Core HTTP + RAG)",
        instructions=(
            "Tools call RLogistics Core over HTTP and local RAG. "
            "Use intake_assist then send_clarification only after human approval."
        ),
        on_list_tools=_on_list_tools,
        on_call_tool=_on_call_tool,
    )


async def run_stdio() -> None:
    server = build_server()
    async with stdio_server() as (read_stream, write_stream):
        await server.run(read_stream, write_stream, server.create_initialization_options())


def main() -> None:
    logging.basicConfig(level=logging.INFO)
    anyio.run(run_stdio)


if __name__ == "__main__":
    main()
