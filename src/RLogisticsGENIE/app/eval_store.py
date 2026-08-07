"""Eval cases: AI judge + SME human scores."""

from __future__ import annotations

import json
import logging
import threading
import time
import uuid
from pathlib import Path
from typing import Any

from redis import Redis

from app.config import settings
from app.eval_judge import judge_output
from app.observability import get_usage_store, utc_day

log = logging.getLogger("rlogistics.genie.eval")


def _agreement(ai: dict[str, Any] | None, sme: dict[str, Any] | None) -> dict[str, Any] | None:
    if not ai or not sme:
        return None
    ai_score = float(ai.get("score_0_to_5") or 0)
    sme_score = float(sme.get("score_0_to_5") or 0)
    ai_pass = bool(ai.get("pass"))
    sme_pass = bool(sme.get("pass"))
    return {
        "abs_diff": round(abs(ai_score - sme_score), 2),
        "both_pass": ai_pass and sme_pass,
        "both_fail": (not ai_pass) and (not sme_pass),
        "agree_pass_fail": ai_pass == sme_pass,
    }


class EvalStore:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._memory: dict[str, dict[str, Any]] = {}
        self._redis: Redis | None = None
        try:
            client = Redis.from_url(settings.redis_url, decode_responses=True)
            client.ping()
            self._redis = client
        except Exception:
            self._redis = None
        Path(settings.eval_jsonl_path).parent.mkdir(parents=True, exist_ok=True)
        self._load_jsonl()

    def _load_jsonl(self) -> None:
        path = Path(settings.eval_jsonl_path)
        if not path.exists():
            return
        try:
            with path.open(encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if not line:
                        continue
                    row = json.loads(line)
                    if row.get("id"):
                        self._memory[row["id"]] = row
        except Exception as ex:
            log.debug("eval jsonl load failed: %s", ex)

    @property
    def backend(self) -> str:
        return "redis" if self._redis else "memory+jsonl"

    def _persist(self, case: dict[str, Any]) -> None:
        with self._lock:
            self._memory[case["id"]] = case
        try:
            with Path(settings.eval_jsonl_path).open("a", encoding="utf-8") as f:
                f.write(json.dumps(case, default=str) + "\n")
        except Exception as ex:
            log.debug("eval jsonl write failed: %s", ex)
        if self._redis:
            try:
                self._redis.hset("eval:cases", case["id"], json.dumps(case, default=str))
            except Exception as ex:
                log.debug("eval redis write failed: %s", ex)

    def _all(self) -> list[dict[str, Any]]:
        if self._redis:
            try:
                raw = self._redis.hgetall("eval:cases")
                if raw:
                    return [json.loads(v) for v in raw.values()]
            except Exception:
                pass
        with self._lock:
            return list(self._memory.values())

    def create_case(
        self,
        *,
        skill: str,
        input_text: str,
        output_text: str,
        request_id: int | None = None,
        request_number: str | None = None,
        caller: str = "http",
        run_judge: bool = True,
    ) -> dict[str, Any]:
        case_id = uuid.uuid4().hex[:12]
        ai_judge = None
        if run_judge:
            ai_judge = judge_output(
                skill=skill,
                input_text=input_text,
                output_text=output_text,
                caller=caller,
            )
        case = {
            "id": case_id,
            "created_at": time.time(),
            "day": utc_day(),
            "request_id": request_id,
            "request_number": request_number,
            "skill": skill,
            "input": input_text[:4000],
            "output": output_text[:8000],
            "ai_judge": ai_judge,
            "sme": None,
            "agreement": None,
        }
        self._persist(case)
        return case

    def get(self, case_id: str) -> dict[str, Any] | None:
        if self._redis:
            try:
                raw = self._redis.hget("eval:cases", case_id)
                if raw:
                    return json.loads(raw)
            except Exception:
                pass
        with self._lock:
            return self._memory.get(case_id)

    def list_cases(self, *, pending_sme: bool | None = None, limit: int = 100) -> list[dict[str, Any]]:
        items = sorted(self._all(), key=lambda c: c.get("created_at") or 0, reverse=True)
        if pending_sme is True:
            items = [c for c in items if not c.get("sme")]
        elif pending_sme is False:
            items = [c for c in items if c.get("sme")]
        return items[:limit]

    def submit_sme(
        self,
        case_id: str,
        *,
        score_0_to_5: float,
        passed: bool,
        notes: str = "",
        reviewer: str = "sme",
    ) -> dict[str, Any]:
        case = self.get(case_id)
        if not case:
            raise KeyError(case_id)
        score = max(0.0, min(5.0, float(score_0_to_5)))
        case["sme"] = {
            "score_0_to_5": score,
            "pass": bool(passed),
            "notes": notes or "",
            "reviewer": reviewer,
            "reviewed_at": time.time(),
        }
        case["agreement"] = _agreement(case.get("ai_judge"), case["sme"])
        self._persist(case)
        return case

    def metrics(self) -> dict[str, Any]:
        cases = self._all()
        if not cases:
            return {
                "cases": 0,
                "ai_pass_rate": None,
                "sme_pass_rate": None,
                "agreement_rate": None,
                "mean_abs_ai_sme": None,
                "cost_per_sme_pass": None,
                "pending_sme": 0,
            }
        ai_scores = []
        ai_pass = 0
        ai_n = 0
        sme_pass = 0
        sme_n = 0
        agree_n = 0
        agree_total = 0
        abs_diffs: list[float] = []
        pending = 0
        for c in cases:
            aj = c.get("ai_judge")
            if aj:
                ai_n += 1
                ai_scores.append(float(aj.get("score_0_to_5") or 0))
                if aj.get("pass"):
                    ai_pass += 1
            sme = c.get("sme")
            if not sme:
                pending += 1
            else:
                sme_n += 1
                if sme.get("pass"):
                    sme_pass += 1
                agr = c.get("agreement") or _agreement(aj, sme)
                if agr:
                    agree_total += 1
                    if agr.get("agree_pass_fail"):
                        agree_n += 1
                    abs_diffs.append(float(agr.get("abs_diff") or 0))

        usage = get_usage_store().summary(day=utc_day())
        cost = float(usage.get("est_cost_usd") or 0)
        cost_per = (cost / sme_pass) if sme_pass else None

        return {
            "cases": len(cases),
            "pending_sme": pending,
            "ai_pass_rate": round(ai_pass / ai_n, 4) if ai_n else None,
            "ai_mean_score": round(sum(ai_scores) / len(ai_scores), 3) if ai_scores else None,
            "sme_pass_rate": round(sme_pass / sme_n, 4) if sme_n else None,
            "sme_reviewed": sme_n,
            "agreement_rate": round(agree_n / agree_total, 4) if agree_total else None,
            "mean_abs_ai_sme": round(sum(abs_diffs) / len(abs_diffs), 3) if abs_diffs else None,
            "cost_per_sme_pass": round(cost_per, 6) if cost_per is not None else None,
            "today_est_cost_usd": usage.get("est_cost_usd"),
            "backend": self.backend,
        }


_eval: EvalStore | None = None


def get_eval_store() -> EvalStore:
    global _eval
    if _eval is None:
        _eval = EvalStore()
    return _eval
