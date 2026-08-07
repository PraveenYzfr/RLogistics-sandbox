"""Usage events + estimated GenAI cost (Redis + JSONL fallback)."""

from __future__ import annotations

import json
import logging
import threading
import time
import uuid
from contextvars import ContextVar
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from redis import Redis

from app.config import settings

log = logging.getLogger("rlogistics.genie.observability")

correlation_id_var: ContextVar[str] = ContextVar("correlation_id", default="")
caller_var: ContextVar[str] = ContextVar("caller", default="anonymous")


def utc_day(ts: float | None = None) -> str:
    dt = datetime.fromtimestamp(ts or time.time(), tz=timezone.utc)
    return dt.strftime("%Y-%m-%d")


def estimate_tokens(text: str) -> int:
    """Rough heuristic: ~4 chars per token."""
    if not text:
        return 0
    return max(1, len(text) // 4)


def estimate_cost_usd(
    *,
    operation: str,
    input_tokens: int = 0,
    output_tokens: int = 0,
) -> float:
    embed_ops = {"embed", "rag_search", "tool:rag_search"}
    if operation in embed_ops or operation.startswith("embed"):
        return (input_tokens / 1_000_000.0) * settings.cost_embed_per_1m
    # LLM / judge / tools treated as LLM-ish
    return (
        (input_tokens / 1_000_000.0) * settings.cost_llm_in_per_1m
        + (output_tokens / 1_000_000.0) * settings.cost_llm_out_per_1m
    )


@dataclass
class UsageEvent:
    id: str = field(default_factory=lambda: uuid.uuid4().hex[:12])
    ts: float = field(default_factory=time.time)
    day: str = ""
    correlation_id: str = ""
    caller: str = "anonymous"
    operation: str = ""
    provider: str = ""
    model: str = ""
    input_tokens: int = 0
    output_tokens: int = 0
    latency_ms: float = 0.0
    est_cost_usd: float = 0.0
    ok: bool = True
    error: str | None = None

    def __post_init__(self) -> None:
        if not self.day:
            self.day = utc_day(self.ts)
        if not self.correlation_id:
            self.correlation_id = correlation_id_var.get() or ""
        if not self.caller or self.caller == "anonymous":
            self.caller = caller_var.get() or "anonymous"
        if self.est_cost_usd <= 0 and (self.input_tokens or self.output_tokens):
            self.est_cost_usd = estimate_cost_usd(
                operation=self.operation,
                input_tokens=self.input_tokens,
                output_tokens=self.output_tokens,
            )

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


class UsageStore:
    """Redis-backed usage with in-memory + JSONL fallback."""

    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._memory: list[dict[str, Any]] = []
        self._redis: Redis | None = None
        try:
            client = Redis.from_url(settings.redis_url, decode_responses=True)
            client.ping()
            self._redis = client
        except Exception:
            self._redis = None
        Path(settings.usage_jsonl_path).parent.mkdir(parents=True, exist_ok=True)

    @property
    def backend(self) -> str:
        return "redis" if self._redis else "memory+jsonl"

    def record(self, event: UsageEvent) -> UsageEvent:
        payload = event.to_dict()
        with self._lock:
            self._memory.append(payload)
            if len(self._memory) > 5000:
                self._memory = self._memory[-2500:]
        try:
            path = Path(settings.usage_jsonl_path)
            with path.open("a", encoding="utf-8") as f:
                f.write(json.dumps(payload, default=str) + "\n")
        except Exception as ex:
            log.debug("jsonl write failed: %s", ex)
        if self._redis:
            try:
                key = f"usage:events:{event.day}"
                self._redis.lpush(key, json.dumps(payload, default=str))
                self._redis.ltrim(key, 0, 4999)
                self._redis.expire(key, 60 * 60 * 24 * 14)
                # aggregates
                self._redis.hincrby(f"usage:agg:{event.day}", "calls", 1)
                self._redis.hincrbyfloat(f"usage:agg:{event.day}", "est_cost_usd", event.est_cost_usd)
                self._redis.hincrby(
                    f"usage:agg:{event.day}:by_op", event.operation or "unknown", 1
                )
                self._redis.hincrbyfloat(
                    f"usage:spend:{event.caller}:{event.day}", "usd", event.est_cost_usd
                )
                if event.operation in ("embed", "rag_search", "tool:rag_search") or (
                    event.operation or ""
                ).startswith("embed"):
                    self._redis.hincrby(
                        f"usage:embeds:{event.caller}:{event.day}", "count", 1
                    )
            except Exception as ex:
                log.debug("redis usage write failed: %s", ex)
        return event

    def events(self, limit: int = 100, day: str | None = None) -> list[dict[str, Any]]:
        day = day or utc_day()
        if self._redis:
            try:
                raw = self._redis.lrange(f"usage:events:{day}", 0, max(limit - 1, 0))
                return [json.loads(x) for x in raw]
            except Exception:
                pass
        with self._lock:
            items = [e for e in self._memory if e.get("day") == day]
            return list(reversed(items[-limit:]))

    def summary(self, day: str | None = None, caller: str | None = None) -> dict[str, Any]:
        day = day or utc_day()
        events = self.events(limit=5000, day=day)
        if caller:
            events = [e for e in events if e.get("caller") == caller]
        by_op: dict[str, int] = {}
        by_provider: dict[str, int] = {}
        total_cost = 0.0
        total_in = 0
        total_out = 0
        ok_n = 0
        for e in events:
            op = e.get("operation") or "unknown"
            by_op[op] = by_op.get(op, 0) + 1
            prov = e.get("provider") or "n/a"
            by_provider[prov] = by_provider.get(prov, 0) + 1
            total_cost += float(e.get("est_cost_usd") or 0)
            total_in += int(e.get("input_tokens") or 0)
            total_out += int(e.get("output_tokens") or 0)
            if e.get("ok", True):
                ok_n += 1
        return {
            "day": day,
            "backend": self.backend,
            "calls": len(events),
            "ok_calls": ok_n,
            "est_cost_usd": round(total_cost, 6),
            "input_tokens": total_in,
            "output_tokens": total_out,
            "by_operation": by_op,
            "by_provider": by_provider,
        }

    def today_spend_usd(self, caller: str) -> float:
        day = utc_day()
        if self._redis:
            try:
                v = self._redis.hget(f"usage:spend:{caller}:{day}", "usd")
                if v is not None:
                    return float(v)
            except Exception:
                pass
        s = self.summary(day=day, caller=caller)
        return float(s.get("est_cost_usd") or 0)

    def today_embeds(self, caller: str) -> int:
        day = utc_day()
        if self._redis:
            try:
                v = self._redis.hget(f"usage:embeds:{caller}:{day}", "count")
                if v is not None:
                    return int(v)
            except Exception:
                pass
        events = self.events(limit=5000, day=day)
        return sum(
            1
            for e in events
            if e.get("caller") == caller
            and (
                e.get("operation") in ("embed", "rag_search", "tool:rag_search")
                or str(e.get("operation") or "").startswith("embed")
            )
        )


_store: UsageStore | None = None


def get_usage_store() -> UsageStore:
    global _store
    if _store is None:
        _store = UsageStore()
    return _store


def record_usage(**kwargs: Any) -> UsageEvent:
    return get_usage_store().record(UsageEvent(**kwargs))
