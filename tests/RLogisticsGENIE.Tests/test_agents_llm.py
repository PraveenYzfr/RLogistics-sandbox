"""LLM dual-tier + multi-agent + mcp http status tests."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2] / "src" / "RLogisticsGENIE"
sys.path.insert(0, str(ROOT))

from app.config import settings  # noqa: E402
from app.llm import llm_complete, llm_status, resolve_model, resolve_vendor  # noqa: E402
from app.mcp_http import mcp_http_status  # noqa: E402


def test_vendor_switch_moves_both_tiers(monkeypatch):
    monkeypatch.setattr(settings, "llm_vendor", "gemini")
    monkeypatch.setattr(settings, "gemini_llm_low_model", "gemini-2.0-flash-lite")
    monkeypatch.setattr(settings, "gemini_llm_high_model", "gemini-2.0-flash")
    assert resolve_vendor() == "gemini"
    v_low, m_low = resolve_model("low")
    v_high, m_high = resolve_model("high")
    assert v_low == v_high == "gemini"
    assert "flash" in m_low or "lite" in m_low
    assert m_high != ""  # high model set for same vendor
    st = llm_status()
    assert st["vendor"] == "gemini"
    assert st["low"]["vendor"] == "gemini"
    assert st["high"]["vendor"] == "gemini"


def test_offline_llm_complete():
    monkeypatch_vendor = None
    from app.config import settings as s

    # force offline
    prev = s.llm_vendor
    s.llm_vendor = "offline"
    try:
        out = llm_complete("sys", "user task about Device GUID", tier="low")
        assert out["vendor"] == "offline"
        assert out["text"]
        assert out["ok"] is True
        out_h = llm_complete("sys", "decide ready for pickup?", tier="high")
        assert out_h["tier"] == "high"
    finally:
        s.llm_vendor = prev


def test_mcp_http_status_shape():
    st = mcp_http_status()
    assert st["path"] == "/mcp"
    assert st["stdio_still_available"] is True
    assert "auth" in st


@pytest.mark.asyncio
async def test_multi_agent_run_offline_against_core_or_fail_soft(monkeypatch):
    """If Core is down, agent run raises; with offline LLM path unit still constructs."""
    monkeypatch.setattr(settings, "llm_vendor", "offline")
    from app.agents import run_intake_agent

    req = {
        "id": 1,
        "requestNumber": "REQ-TEST",
        "requestType": "DeviceReturn",
        "dispositionType": "Recycle",
        "site": "Lab",
        "contactName": "A",
        "contactEmail": "a@demo.local",
        "pickupAddressLine1": "1 St",
        "pickupCity": "X",
        "assets": [{"assetType": "Laptop", "quantity": 1}],
        "status": "Assigned",
    }
    intake = await run_intake_agent(req)
    assert intake["agent"] == "intake"
    assert "clarification_draft" in intake
    assert intake["llm"]["vendor"] in ("offline", "gemini", "azure", "openai", "claude", "ollama")
