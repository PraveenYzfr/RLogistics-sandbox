"""Rate limiting + daily spend controls (Redis with in-memory fallback)."""

from __future__ import annotations

import logging
import threading
import time
from collections import defaultdict, deque
from dataclasses import dataclass
from typing import Any

from redis import Redis

from app.config import settings
from app.observability import get_usage_store, utc_day

log = logging.getLogger("rlogistics.genie.limits")


@dataclass
class LimitDecision:
    allowed: bool
    reason: str | None = None
    remaining_rpm: int | None = None
    remaining_budget_usd: float | None = None
    remaining_embeds: int | None = None
    observe_only_would_block: bool = False

    def to_dict(self) -> dict[str, Any]:
        return {
            "allowed": self.allowed,
            "reason": self.reason,
            "remaining_rpm": self.remaining_rpm,
            "remaining_budget_usd": self.remaining_budget_usd,
            "remaining_embeds": self.remaining_embeds,
            "observe_only_would_block": self.observe_only_would_block,
            "spend_enabled": settings.spend_enabled,
        }


class RateSpendGuard:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        # caller -> deque of request timestamps (seconds)
        self._rpm: dict[str, deque[float]] = defaultdict(deque)
        self._redis: Redis | None = None
        try:
            client = Redis.from_url(settings.redis_url, decode_responses=True)
            client.ping()
            self._redis = client
        except Exception:
            self._redis = None

    @property
    def backend(self) -> str:
        return "redis" if self._redis else "memory"

    def _minute_bucket(self) -> int:
        return int(time.time() // 60)

    def _count_rpm(self, caller: str) -> int:
        if self._redis:
            try:
                key = f"limits:rpm:{caller}:{self._minute_bucket()}"
                n = int(self._redis.get(key) or 0)
                return n
            except Exception:
                pass
        now = time.time()
        with self._lock:
            q = self._rpm[caller]
            while q and now - q[0] > 60:
                q.popleft()
            return len(q)

    def _incr_rpm(self, caller: str) -> int:
        if self._redis:
            try:
                key = f"limits:rpm:{caller}:{self._minute_bucket()}"
                n = int(self._redis.incr(key))
                if n == 1:
                    self._redis.expire(key, 120)
                return n
            except Exception:
                pass
        now = time.time()
        with self._lock:
            q = self._rpm[caller]
            while q and now - q[0] > 60:
                q.popleft()
            q.append(now)
            return len(q)

    def status(self, caller: str) -> dict[str, Any]:
        rpm = self._count_rpm(caller)
        spend = get_usage_store().today_spend_usd(caller)
        embeds = get_usage_store().today_embeds(caller)
        return {
            "caller": caller,
            "backend": self.backend,
            "rpm_used": rpm,
            "remaining_rpm": max(settings.rate_limit_rpm - rpm, 0),
            "spend_usd_used": round(spend, 6),
            "remaining_budget_usd": round(max(settings.spend_limit_usd_day - spend, 0.0), 6),
            "embeds_used": embeds,
            "remaining_embeds": max(settings.rate_limit_embeds_day - embeds, 0),
            "limits": {
                "rate_limit_rpm": settings.rate_limit_rpm,
                "rate_limit_embeds_day": settings.rate_limit_embeds_day,
                "spend_limit_usd_day": settings.spend_limit_usd_day,
                "spend_enabled": settings.spend_enabled,
            },
        }

    def check_request(self, caller: str, *, record: bool = True) -> LimitDecision:
        """RPM + daily spend gate for an incoming HTTP/MCP request."""
        status = self.status(caller)
        rpm_after = self._incr_rpm(caller) if record else self._count_rpm(caller) + 1
        remaining_rpm = max(settings.rate_limit_rpm - rpm_after, 0)
        remaining_budget = float(status["remaining_budget_usd"])
        remaining_embeds = int(status["remaining_embeds"])

        def _block(reason: str) -> LimitDecision:
            d = LimitDecision(
                allowed=False,
                reason=reason,
                remaining_rpm=remaining_rpm,
                remaining_budget_usd=remaining_budget,
                remaining_embeds=remaining_embeds,
            )
            if not settings.spend_enabled:
                log.warning("spend_enabled=false would-block: %s caller=%s", reason, caller)
                d.allowed = True
                d.observe_only_would_block = True
            return d

        if rpm_after > settings.rate_limit_rpm:
            return _block(f"rate_limit_rpm exceeded ({settings.rate_limit_rpm}/min)")
        if status["spend_usd_used"] >= settings.spend_limit_usd_day:
            return _block(
                f"spend_limit_usd_day exceeded (${settings.spend_limit_usd_day}/day)"
            )
        return LimitDecision(
            allowed=True,
            remaining_rpm=remaining_rpm,
            remaining_budget_usd=remaining_budget,
            remaining_embeds=remaining_embeds,
        )

    def check_embed(self, caller: str, *, extra_embeds: int = 1) -> LimitDecision:
        status = self.status(caller)
        used = int(status["embeds_used"]) + extra_embeds
        remaining = max(settings.rate_limit_embeds_day - used, 0)
        if used > settings.rate_limit_embeds_day:
            d = LimitDecision(
                allowed=False,
                reason=f"rate_limit_embeds_day exceeded ({settings.rate_limit_embeds_day}/day)",
                remaining_rpm=status["remaining_rpm"],
                remaining_budget_usd=status["remaining_budget_usd"],
                remaining_embeds=remaining,
            )
            if not settings.spend_enabled:
                log.warning("spend_enabled=false would-block embeds caller=%s", caller)
                d.allowed = True
                d.observe_only_would_block = True
            return d
        return LimitDecision(
            allowed=True,
            remaining_rpm=status["remaining_rpm"],
            remaining_budget_usd=status["remaining_budget_usd"],
            remaining_embeds=remaining,
        )

    def check_spend_for_cost(self, caller: str, est_cost: float) -> LimitDecision:
        status = self.status(caller)
        projected = float(status["spend_usd_used"]) + est_cost
        remaining = max(settings.spend_limit_usd_day - projected, 0.0)
        if projected > settings.spend_limit_usd_day and est_cost > 0:
            d = LimitDecision(
                allowed=False,
                reason=f"spend_limit_usd_day would exceed ${settings.spend_limit_usd_day}",
                remaining_rpm=status["remaining_rpm"],
                remaining_budget_usd=remaining,
                remaining_embeds=status["remaining_embeds"],
            )
            if not settings.spend_enabled:
                d.allowed = True
                d.observe_only_would_block = True
            return d
        return LimitDecision(
            allowed=True,
            remaining_rpm=status["remaining_rpm"],
            remaining_budget_usd=remaining,
            remaining_embeds=status["remaining_embeds"],
        )


_guard: RateSpendGuard | None = None


def get_rate_guard() -> RateSpendGuard:
    global _guard
    if _guard is None:
        _guard = RateSpendGuard()
    return _guard
