"""MCP Streamable HTTP transport mounted on FastAPI (local + API key). Stdio remains separate."""

from __future__ import annotations

import logging
import secrets
from typing import Any, Callable

from starlette.requests import Request
from starlette.responses import JSONResponse, Response
from starlette.types import ASGIApp, Receive, Scope, Send

from app.config import settings
from app.mcp_stdio import build_server

log = logging.getLogger("rlogistics.genie.mcp_http")

_session_manager = None


def mcp_api_key() -> str:
    return (settings.mcp_api_key or settings.rlogistics_api_key or "").strip()


def _authorized(request: Request) -> bool:
    expected = mcp_api_key()
    if not expected:
        return False
    got = request.headers.get("X-Api-Key") or ""
    if not got:
        auth = request.headers.get("Authorization") or ""
        if auth.lower().startswith("bearer "):
            got = auth[7:].strip()
    return secrets.compare_digest(got, expected)


def get_session_manager():
    global _session_manager
    if _session_manager is None:
        from mcp.server.streamable_http_manager import StreamableHTTPSessionManager

        server = build_server()
        _session_manager = StreamableHTTPSessionManager(
            app=server,
            json_response=True,
            stateless=True,
        )
        log.info("MCP Streamable HTTP session manager created (stateless)")
    return _session_manager


class McpAuthASGI:
    """ASGI wrapper: require API key, then delegate to StreamableHTTPSessionManager."""

    def __init__(self, app: ASGIApp):
        self.app = app

    async def __call__(self, scope: Scope, receive: Receive, send: Send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return
        request = Request(scope, receive=receive)
        if not settings.mcp_http_enabled:
            resp = JSONResponse({"error": "mcp_http_disabled"}, status_code=503)
            await resp(scope, receive, send)
            return
        if not _authorized(request):
            resp = JSONResponse(
                {"error": "unauthorized", "hint": "Send X-Api-Key or Authorization: Bearer"},
                status_code=401,
            )
            await resp(scope, receive, send)
            return
        await self.app(scope, receive, send)


async def mcp_asgi(scope: Scope, receive: Receive, send: Send) -> None:
    manager = get_session_manager()
    await manager.handle_request(scope, receive, send)


def build_mcp_http_app() -> ASGIApp:
    return McpAuthASGI(mcp_asgi)


def mcp_http_status() -> dict[str, Any]:
    return {
        "enabled": settings.mcp_http_enabled,
        "path": "/mcp",
        "auth": "X-Api-Key or Bearer (MCP_API_KEY or RLOGISTICS_API_KEY)",
        "transport": "streamable-http-stateless",
        "stdio_still_available": True,
        "local_only_note": "Bind uvicorn to 127.0.0.1 for local-only access",
    }
