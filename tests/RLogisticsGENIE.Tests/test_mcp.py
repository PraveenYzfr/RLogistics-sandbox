"""MCP stdio server + in-repo client integration tests."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2] / "src" / "RLogisticsGENIE"
sys.path.insert(0, str(ROOT))


@pytest.mark.asyncio
async def test_mcp_build_server():
    from app.mcp_stdio import build_server

    server = build_server()
    assert server is not None


@pytest.mark.asyncio
async def test_mcp_client_list_and_rag_search():
    from app.mcp_client import call_mcp_tool, list_mcp_tools, parse_tool_json
    from app.tools import ensure_rag_indexed

    ensure_rag_indexed()

    tools = await list_mcp_tools()
    names = {t["name"] for t in tools}
    assert "rag_search" in names
    assert "intake_assist" in names

    payload = await call_mcp_tool("rag_search", {"query": "Device GUID", "top_k": 2})
    assert payload["ok"] is True
    parsed = parse_tool_json(payload)
    # structured list or JSON text list
    assert parsed is not None
