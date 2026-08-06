"""Back-compat module — prefer app.tools + app.mcp_stdio."""

from __future__ import annotations

from app.tools import call_tool, dispatch_tool, list_tools, load_kb_once, ensure_rag_indexed

__all__ = [
    "call_tool",
    "dispatch_tool",
    "list_tools",
    "load_kb_once",
    "ensure_rag_indexed",
]


def main() -> None:
    """Deprecated entry — redirects to official MCP stdio server."""
    from app.mcp_stdio import main as mcp_main

    mcp_main()


if __name__ == "__main__":
    main()
