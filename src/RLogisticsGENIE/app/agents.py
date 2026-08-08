"""
Multi-agent supervisor: IntakeAgent + ComplianceAgent + VendorAgent.

Writes to Core are NEVER auto-executed — agents return pending_hitl proposals.
Terminal happy path: ready_for_pickup (proposal only; no status PATCH).
"""

from __future__ import annotations

import json
import logging
import time
from typing import Any

from app.config import settings
from app.core_client import RLogisticsClient
from app.llm import llm_complete
from app.rag import get_shared_rag
from app.skills import completeness_score, draft_clarification, recommend_vendors, summarize_request
from app.tools import ensure_rag_indexed

log = logging.getLogger("rlogistics.genie.agents")

WRITE_TOOLS = {"send_clarification", "send_vendor_quotes", "update_status"}


def _safe_json(text: str) -> dict[str, Any] | None:
    text = (text or "").strip()
    if not text:
        return None
    try:
        if "```" in text:
            start = text.find("{")
            end = text.rfind("}") + 1
            if start >= 0 and end > start:
                text = text[start:end]
        return json.loads(text)
    except json.JSONDecodeError:
        return None


async def run_intake_agent(request: dict[str, Any]) -> dict[str, Any]:
    ensure_rag_indexed()
    rag = get_shared_rag()
    q = f"{request.get('site')} {request.get('dispositionType')} {request.get('requestType')} policy"
    hits = rag.search(q, top_k=4)
    completeness = completeness_score(request)
    summary = summarize_request(request, hits)
    draft = draft_clarification(request, completeness.get("gaps") or [])

    llm = llm_complete(
        system=(
            "You are IntakeAgent for reverse logistics. "
            "Return JSON: {\"needs_clarification\": bool, \"rationale\": str, \"draft_tweak\": str|null}. "
            "Use low-cost reasoning; prefer needing clarification when gaps exist."
        ),
        user=json.dumps(
            {"gaps": completeness.get("gaps"), "summary": summary.get("summary"), "draft": draft},
            default=str,
        ),
        tier="low",
        operation="agent:intake",
    )
    parsed = _safe_json(llm["text"]) or {}
    needs = bool(completeness.get("gaps")) or bool(parsed.get("needs_clarification"))
    if parsed.get("draft_tweak"):
        draft = str(parsed["draft_tweak"])
    return {
        "agent": "intake",
        "completeness": completeness,
        "summary": summary,
        "sop_hits": [{"title": h.get("title"), "score": h.get("score")} for h in hits],
        "clarification_draft": draft,
        "needs_clarification": needs,
        "llm": {"vendor": llm["vendor"], "model": llm["model"], "tier": llm["tier"]},
        "rationale": parsed.get("rationale") or ("gaps present" if needs else "intake complete enough"),
    }


async def run_compliance_agent(request: dict[str, Any], intake: dict[str, Any]) -> dict[str, Any]:
    ensure_rag_indexed()
    hits = get_shared_rag().search(
        f"disposition {request.get('dispositionType')} device guid policy",
        top_k=3,
    )
    assets = request.get("assets") or []
    missing_guid = sum(1 for a in assets if not a.get("deviceGuid"))
    rule_fail = []
    if missing_guid:
        rule_fail.append(f"{missing_guid} asset(s) missing Device GUID")
    if not request.get("dispositionType"):
        rule_fail.append("dispositionType missing")

    llm = llm_complete(
        system=(
            "You are ComplianceAgent. Return JSON: "
            "{\"pass\": bool, \"blockers\": [str], \"rationale\": str}. "
            "Fail if Device GUID or disposition policy is violated."
        ),
        user=json.dumps(
            {
                "requestNumber": request.get("requestNumber"),
                "dispositionType": request.get("dispositionType"),
                "rule_fail": rule_fail,
                "policy": [h.get("title") for h in hits],
                "gaps": (intake.get("completeness") or {}).get("gaps"),
            },
            default=str,
        ),
        tier="high",
        operation="agent:compliance",
    )
    parsed = _safe_json(llm["text"]) or {}
    blockers = list(rule_fail)
    if isinstance(parsed.get("blockers"), list):
        blockers = list({*blockers, *[str(b) for b in parsed["blockers"]]})
    # Hard rule: missing GUID always fails
    passed = (missing_guid == 0) and (parsed.get("pass", len(blockers) == 0) if not rule_fail else False)
    if rule_fail:
        passed = False
    return {
        "agent": "compliance",
        "pass": passed,
        "blockers": blockers,
        "policy_hits": [{"title": h.get("title"), "score": h.get("score")} for h in hits],
        "llm": {"vendor": llm["vendor"], "model": llm["model"], "tier": llm["tier"]},
        "rationale": parsed.get("rationale") or ("blocked" if not passed else "compliance ok"),
    }


async def run_vendor_agent(request: dict[str, Any], vendors: list[dict[str, Any]]) -> dict[str, Any]:
    rec = recommend_vendors(request, vendors)
    has_transport = bool(request.get("transportVendorId"))
    has_processing = bool(request.get("processingVendorId"))
    needs_quotes = not (has_transport and has_processing)

    llm = llm_complete(
        system=(
            "You are VendorAgent. Return JSON: "
            "{\"needs_send_quotes\": bool, \"rationale\": str}. "
            "If transport/processing vendors not set, needs_send_quotes true."
        ),
        user=json.dumps(
            {
                "recommendation": rec,
                "transportVendorId": request.get("transportVendorId"),
                "processingVendorId": request.get("processingVendorId"),
            },
            default=str,
        ),
        tier="low",
        operation="agent:vendor",
    )
    parsed = _safe_json(llm["text"]) or {}
    needs = needs_quotes or bool(parsed.get("needs_send_quotes"))
    return {
        "agent": "vendor",
        "recommendation": rec,
        "needs_send_quotes": needs,
        "llm": {"vendor": llm["vendor"], "model": llm["model"], "tier": llm["tier"]},
        "rationale": parsed.get("rationale") or ("select/send quotes" if needs else "vendors in place"),
        # Propose only — never call send_vendor_quotes here
        "proposed_hitl": {"action": "send_vendor_quotes"} if needs else None,
    }


async def run_supervisor(request_id: int) -> dict[str, Any]:
    """Orchestrate specialists; stop on HITL or blocked; else propose ready_for_pickup."""
    t0 = time.perf_counter()
    client = RLogisticsClient()
    trace: list[dict[str, Any]] = []
    try:
        req = await client.get_request(request_id)
        vendors = await client.list_vendors()

        intake = await run_intake_agent(req)
        trace.append({"step": 1, **intake})

        if intake.get("needs_clarification"):
            return _result(
                state="pending_hitl",
                action="send_clarification",
                request=req,
                trace=trace,
                draft=intake.get("clarification_draft"),
                message="Intake gaps — coordinator must approve clarification before Core write.",
                t0=t0,
            )

        compliance = await run_compliance_agent(req, intake)
        trace.append({"step": 2, **compliance})
        if not compliance.get("pass"):
            return _result(
                state="blocked",
                action=None,
                request=req,
                trace=trace,
                draft=None,
                message="Compliance failed — fix blockers in Core data.",
                blockers=compliance.get("blockers"),
                t0=t0,
            )

        vendor = await run_vendor_agent(req, vendors)
        trace.append({"step": 3, **vendor})
        if vendor.get("needs_send_quotes"):
            return _result(
                state="pending_hitl",
                action="send_vendor_quotes",
                request=req,
                trace=trace,
                draft=json.dumps(vendor.get("recommendation"), default=str),
                message="Vendor selection / quotes need HITL before Core send.",
                t0=t0,
            )

        # High-tier supervisor decision — proposal only
        llm = llm_complete(
            system=(
                "You are SupervisorAgent. Decide if request is ready for pickup scheduling. "
                "Return JSON: {\"ready_for_pickup\": bool, \"rationale\": str}. "
                "Never claim you changed Core status."
            ),
            user=json.dumps(
                {
                    "status": req.get("status"),
                    "intake_ok": not intake.get("needs_clarification"),
                    "compliance_ok": compliance.get("pass"),
                    "vendor_ok": not vendor.get("needs_send_quotes"),
                },
                default=str,
            ),
            tier="high",
            operation="agent:supervisor",
        )
        parsed = _safe_json(llm["text"]) or {}
        ready = bool(parsed.get("ready_for_pickup", True))
        trace.append(
            {
                "step": 4,
                "agent": "supervisor",
                "llm": {"vendor": llm["vendor"], "model": llm["model"], "tier": "high"},
                "ready_for_pickup": ready,
                "rationale": parsed.get("rationale") or llm["text"][:500],
            }
        )

        return _result(
            state="ready_for_pickup" if ready else "needs_review",
            action=None,
            request=req,
            trace=trace,
            draft=None,
            message=(
                "All agents green — proposed ready_for_pickup (no Core status write)."
                if ready
                else "Supervisor wants human review."
            ),
            t0=t0,
        )
    finally:
        await client.close()


def _result(
    *,
    state: str,
    action: str | None,
    request: dict[str, Any],
    trace: list[dict[str, Any]],
    draft: str | None,
    message: str,
    t0: float,
    blockers: list[str] | None = None,
) -> dict[str, Any]:
    return {
        "state": state,
        "action": action,
        "hitl_required": state == "pending_hitl",
        "write_tools_blocked": list(WRITE_TOOLS),
        "request_id": request.get("id"),
        "request_number": request.get("requestNumber"),
        "draft": draft,
        "blockers": blockers or [],
        "message": message,
        "agents": trace,
        "latency_ms": round((time.perf_counter() - t0) * 1000, 1),
        "max_steps": settings.agent_max_steps,
    }
