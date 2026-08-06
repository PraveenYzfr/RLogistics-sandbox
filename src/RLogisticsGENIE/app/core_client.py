from __future__ import annotations

import hashlib
import json
from typing import Any

import httpx
from redis import Redis

from app.config import settings


class RLogisticsClient:
    """HTTP client for RLogistics Core REST (JWT or X-Api-Key). Never touches SQL."""

    def __init__(self, base_url: str | None = None, api_key: str | None = None) -> None:
        self.base_url = (base_url or settings.rlogistics_url).rstrip("/")
        self.api_key = api_key or settings.rlogistics_api_key
        self._http = httpx.AsyncClient(timeout=30.0)

    def _headers(self) -> dict[str, str]:
        return {"X-Api-Key": self.api_key, "Accept": "application/json"}

    async def close(self) -> None:
        await self._http.aclose()

    async def get_request(self, request_id: int) -> dict[str, Any]:
        r = await self._http.get(
            f"{self.base_url}/api/requests/{request_id}",
            headers=self._headers(),
        )
        r.raise_for_status()
        return r.json()

    async def list_requests(self, status: str | None = None) -> list[dict[str, Any]]:
        params = {}
        if status:
            params["status"] = status
        r = await self._http.get(
            f"{self.base_url}/api/requests",
            headers=self._headers(),
            params=params,
        )
        r.raise_for_status()
        return r.json()

    async def list_vendors(self, vendor_type: str | None = None) -> list[dict[str, Any]]:
        params = {}
        if vendor_type:
            params["type"] = vendor_type
        r = await self._http.get(
            f"{self.base_url}/api/vendors",
            headers=self._headers(),
            params=params,
        )
        r.raise_for_status()
        return r.json()

    async def send_clarification(self, request_id: int, question: str) -> dict[str, Any]:
        r = await self._http.post(
            f"{self.base_url}/api/requests/{request_id}/clarifications",
            headers=self._headers(),
            json={"question": question},
        )
        r.raise_for_status()
        return r.json()

    async def send_vendor_quotes(self, request_id: int) -> dict[str, Any]:
        r = await self._http.post(
            f"{self.base_url}/api/requests/{request_id}/vendor-quotes",
            headers=self._headers(),
        )
        r.raise_for_status()
        return r.json()

    async def send_return_reminder(self, request_id: int) -> dict[str, Any]:
        r = await self._http.post(
            f"{self.base_url}/api/requests/{request_id}/return-reminder",
            headers=self._headers(),
        )
        r.raise_for_status()
        return r.json()

    async def update_status(self, request_id: int, status: str, notes: str | None = None) -> dict[str, Any]:
        r = await self._http.patch(
            f"{self.base_url}/api/requests/{request_id}/status",
            headers=self._headers(),
            json={"status": status, "notes": notes},
        )
        r.raise_for_status()
        return r.json()


class RedisCache:
    def __init__(self) -> None:
        self._client: Redis | None = None
        try:
            self._client = Redis.from_url(settings.redis_url, decode_responses=True)
            self._client.ping()
        except Exception:
            self._client = None

    @property
    def available(self) -> bool:
        return self._client is not None

    def get_json(self, key: str) -> Any | None:
        if not self._client:
            return None
        try:
            raw = self._client.get(key)
            return json.loads(raw) if raw else None
        except Exception:
            return None

    def set_json(self, key: str, value: Any, ttl: int | None = None) -> None:
        if not self._client:
            return
        try:
            self._client.setex(key, ttl or settings.cache_ttl_seconds, json.dumps(value))
        except Exception:
            pass

    def delete(self, key: str) -> None:
        if not self._client:
            return
        try:
            self._client.delete(key)
        except Exception:
            pass


def cache_key(*parts: str) -> str:
    return "genie:" + ":".join(parts)


def content_hash(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]
