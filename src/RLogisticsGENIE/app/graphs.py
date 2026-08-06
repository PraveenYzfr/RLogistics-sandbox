"""LangGraph workflows for intake + quote cycle (real StateGraph when available)."""

from __future__ import annotations

import logging
from typing import Any, TypedDict

from app.skills import completeness_score, draft_clarification, recommend_vendors, summarize_request

log = logging.getLogger("rlogistics.genie.graphs")

_intake_graph = None
_quote_graph = None
_compile_status = "not-compiled"


class IntakeState(TypedDict, total=False):
    request: dict[str, Any]
    sop_hits: list[dict[str, Any]]
    completeness: dict[str, Any]
    summary: dict[str, Any]
    clarification_draft: str
    approval_pending: bool
    approved: bool
    posted: bool
    result: dict[str, Any]
    engine: str


class QuoteState(TypedDict, total=False):
    request: dict[str, Any]
    vendors: list[dict[str, Any]]
    recommendation: dict[str, Any]
    quote_parse: dict[str, Any] | None
    email_body: str | None
    next_step: str
    engine: str


def _intake_fallback(request: dict[str, Any], sop_hits: list[dict[str, Any]] | None = None) -> IntakeState:
    state: IntakeState = {"request": request, "sop_hits": sop_hits or [], "engine": "fallback"}
    state["completeness"] = completeness_score(request)
    state["summary"] = summarize_request(request, state["sop_hits"])
    state["clarification_draft"] = draft_clarification(request, state["completeness"].get("gaps") or [])
    state["approval_pending"] = True
    state["approved"] = False
    state["posted"] = False
    state["result"] = {
        "phase": "awaiting_coordinator_approval",
        "message": "Draft ready. Call POST /v1/intake/{id}/approve to post clarification to RLogistics Core.",
    }
    return state


def _quote_fallback(
    request: dict[str, Any],
    vendors: list[dict[str, Any]],
    email_body: str | None = None,
) -> QuoteState:
    from app.skills import parse_quote_email

    rec = recommend_vendors(request, vendors)
    state: QuoteState = {
        "request": request,
        "vendors": vendors,
        "recommendation": rec,
        "email_body": email_body,
        "engine": "fallback",
        "next_step": "Select vendors in RLogistics then POST core /vendor-quotes (or GENIE /v1/quotes/{id}/send)",
    }
    if email_body:
        state["quote_parse"] = parse_quote_email(email_body, request.get("requestNumber"))
        state["next_step"] = "Review parsed quote grid; award vendor via coordinator plan API"
    return state


def _build_intake_graph():
    from langgraph.graph import END, StateGraph

    def score(s: IntakeState) -> IntakeState:
        s["completeness"] = completeness_score(s["request"])
        return s

    def attach_rag(s: IntakeState) -> IntakeState:
        # sop_hits already provided by caller; keep node for graph clarity / future refresh
        s["sop_hits"] = s.get("sop_hits") or []
        return s

    def summarize(s: IntakeState) -> IntakeState:
        s["summary"] = summarize_request(s["request"], s.get("sop_hits"))
        return s

    def draft(s: IntakeState) -> IntakeState:
        gaps = (s.get("completeness") or {}).get("gaps") or []
        s["clarification_draft"] = draft_clarification(s["request"], gaps)
        s["approval_pending"] = True
        s["approved"] = False
        s["posted"] = False
        s["result"] = {
            "phase": "awaiting_coordinator_approval",
            "message": "Draft ready. Call POST /v1/intake/{id}/approve to post clarification to RLogistics Core.",
        }
        s["engine"] = "langgraph"
        return s

    g = StateGraph(dict)
    g.add_node("score", score)
    g.add_node("rag", attach_rag)
    g.add_node("summarize", summarize)
    g.add_node("draft", draft)
    g.set_entry_point("score")
    g.add_edge("score", "rag")
    g.add_edge("rag", "summarize")
    g.add_edge("summarize", "draft")
    g.add_edge("draft", END)
    return g.compile()


def _build_quote_graph():
    from langgraph.graph import END, StateGraph
    from app.skills import parse_quote_email

    def recommend(s: QuoteState) -> QuoteState:
        s["recommendation"] = recommend_vendors(s["request"], s.get("vendors") or [])
        s["next_step"] = "Select vendors in RLogistics then POST core /vendor-quotes (or GENIE /v1/quotes/{id}/send)"
        return s

    def maybe_parse(s: QuoteState) -> QuoteState:
        body = s.get("email_body")
        if body:
            s["quote_parse"] = parse_quote_email(body, (s.get("request") or {}).get("requestNumber"))
            s["next_step"] = "Review parsed quote grid; award vendor via coordinator plan API"
        s["engine"] = "langgraph"
        return s

    g = StateGraph(dict)
    g.add_node("recommend", recommend)
    g.add_node("parse", maybe_parse)
    g.set_entry_point("recommend")
    g.add_edge("recommend", "parse")
    g.add_edge("parse", END)
    return g.compile()


def ensure_graphs_compiled() -> str:
    global _intake_graph, _quote_graph, _compile_status
    if _intake_graph is not None and _quote_graph is not None:
        return _compile_status
    try:
        _intake_graph = _build_intake_graph()
        _quote_graph = _build_quote_graph()
        _compile_status = "langgraph-ok"
        log.info("LangGraph intake + quote graphs compiled")
    except Exception as ex:
        _intake_graph = None
        _quote_graph = None
        _compile_status = f"langgraph-fallback: {ex}"
        log.warning("LangGraph compile failed: %s", ex)
    return _compile_status


def run_intake_assist(request: dict[str, Any], sop_hits: list[dict[str, Any]] | None = None) -> IntakeState:
    """G1 IntakeAssist — LangGraph invoke when compiled, else sequential fallback."""
    ensure_graphs_compiled()
    if _intake_graph is not None:
        try:
            out = _intake_graph.invoke({"request": request, "sop_hits": sop_hits or []})
            return out  # type: ignore[return-value]
        except Exception as ex:
            log.warning("LangGraph intake invoke failed: %s", ex)
    return _intake_fallback(request, sop_hits)


def run_quote_cycle(
    request: dict[str, Any],
    vendors: list[dict[str, Any]],
    email_body: str | None = None,
) -> QuoteState:
    """G2 QuoteCycle — LangGraph invoke when compiled."""
    ensure_graphs_compiled()
    if _quote_graph is not None:
        try:
            out = _quote_graph.invoke(
                {"request": request, "vendors": vendors, "email_body": email_body}
            )
            return out  # type: ignore[return-value]
        except Exception as ex:
            log.warning("LangGraph quote invoke failed: %s", ex)
    return _quote_fallback(request, vendors, email_body)


def try_langgraph_compile() -> str:
    return ensure_graphs_compiled()
