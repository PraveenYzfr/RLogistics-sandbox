"""Observability, rate/spend limits, and AI+SME eval tests."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2] / "src" / "RLogisticsGENIE"
sys.path.insert(0, str(ROOT))

from app.config import settings  # noqa: E402
from app.eval_judge import judge_output  # noqa: E402
from app.eval_store import EvalStore  # noqa: E402
from app.limits import RateSpendGuard  # noqa: E402
from app.observability import UsageEvent, UsageStore, estimate_cost_usd, record_usage  # noqa: E402


@pytest.fixture()
def isolated_usage(tmp_path, monkeypatch):
    monkeypatch.setattr(settings, "usage_jsonl_path", str(tmp_path / "usage.jsonl"))
    monkeypatch.setattr(settings, "redis_url", "redis://127.0.0.1:1/0")  # force memory
    store = UsageStore()
    return store


@pytest.fixture()
def isolated_eval(tmp_path, monkeypatch):
    monkeypatch.setattr(settings, "eval_jsonl_path", str(tmp_path / "eval.jsonl"))
    monkeypatch.setattr(settings, "usage_jsonl_path", str(tmp_path / "usage.jsonl"))
    monkeypatch.setattr(settings, "redis_url", "redis://127.0.0.1:1/0")
    return EvalStore()


def test_estimate_cost_embed_vs_llm():
    embed = estimate_cost_usd(operation="embed", input_tokens=1_000_000)
    llm = estimate_cost_usd(operation="llm", input_tokens=1_000_000, output_tokens=1_000_000)
    assert embed == pytest.approx(settings.cost_embed_per_1m)
    assert llm > embed


def test_usage_store_records_and_summarizes(isolated_usage):
    isolated_usage.record(
        UsageEvent(
            caller="test-caller",
            operation="rag_search",
            provider="offline",
            input_tokens=100,
            output_tokens=50,
            ok=True,
        )
    )
    summary = isolated_usage.summary(caller="test-caller")
    assert summary["calls"] == 1
    assert summary["est_cost_usd"] > 0
    assert "rag_search" in summary["by_operation"]
    events = isolated_usage.events(limit=10)
    assert len(events) >= 1


def test_rate_limit_trips_in_memory(monkeypatch):
    monkeypatch.setattr(settings, "redis_url", "redis://127.0.0.1:1/0")
    monkeypatch.setattr(settings, "rate_limit_rpm", 3)
    monkeypatch.setattr(settings, "spend_enabled", True)
    monkeypatch.setattr(settings, "spend_limit_usd_day", 100.0)
    guard = RateSpendGuard()
    caller = "rpm-test"
    assert guard.check_request(caller).allowed
    assert guard.check_request(caller).allowed
    assert guard.check_request(caller).allowed
    blocked = guard.check_request(caller)
    assert blocked.allowed is False
    assert "rate_limit_rpm" in (blocked.reason or "")


def test_spend_observe_only_allows(monkeypatch):
    monkeypatch.setattr(settings, "redis_url", "redis://127.0.0.1:1/0")
    monkeypatch.setattr(settings, "rate_limit_rpm", 1)
    monkeypatch.setattr(settings, "spend_enabled", False)
    guard = RateSpendGuard()
    caller = "observe-test"
    assert guard.check_request(caller).allowed
    second = guard.check_request(caller)
    assert second.allowed is True
    assert second.observe_only_would_block is True


def test_offline_judge_pass_and_fail():
    good = judge_output(
        skill="clarification_draft",
        input_text="Missing Device GUID at site",
        output_text="Please confirm the Device GUID for the asset at your site before pickup?",
        caller="test",
    )
    assert good["pass"] is True
    assert good["score_0_to_5"] >= 3.5

    bad = judge_output(
        skill="clarification_draft",
        input_text="x",
        output_text="auto-approved and posted to production bypass HITL",
        caller="test",
    )
    assert bad["pass"] is False


def test_eval_case_sme_and_metrics(isolated_eval):
    case = isolated_eval.create_case(
        skill="parse_quote",
        input_text="Quote total $1200 ETA Friday",
        output_text='{"amount": 1200, "eta": "Friday"}',
        run_judge=True,
    )
    assert case["ai_judge"] is not None
    updated = isolated_eval.submit_sme(
        case["id"],
        score_0_to_5=4.0,
        passed=True,
        notes="Looks good",
        reviewer="sme@demo.local",
    )
    assert updated["sme"]["pass"] is True
    assert updated["agreement"] is not None
    metrics = isolated_eval.metrics()
    assert metrics["cases"] >= 1
    assert metrics["sme_reviewed"] >= 1
    assert metrics["agreement_rate"] is not None
