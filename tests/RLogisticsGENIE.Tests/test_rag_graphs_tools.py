"""RAG chunking + retrieval tests (no Core / Qdrant required)."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src" / "RLogisticsGENIE"
sys.path.insert(0, str(ROOT))

from app.rag import RagStore, chunk_text, load_kb  # noqa: E402
from app.graphs import run_intake_assist, run_quote_cycle, try_langgraph_compile  # noqa: E402
from app.tools import list_tool_specs, call_tool  # noqa: E402
import pytest  # noqa: E402


def test_chunk_text_splits_long_doc():
    text = ("Paragraph one about Device GUID requirements.\n\n" * 40)
    chunks = chunk_text(text, chunk_size=200, overlap=40)
    assert len(chunks) > 1
    assert all(len(c) <= 250 for c in chunks)


def test_rag_indexes_kb_and_searches():
    docs = load_kb()
    assert docs, "kb/*.md should exist"
    store = RagStore()
    n = store.index_documents(docs)
    assert n >= len(docs)  # at least one chunk per doc; usually more
    hits = store.search("Device GUID required on assets", top_k=3)
    assert hits
    assert hits[0]["score"] >= 0
    assert hits[0].get("text") or hits[0].get("title")
    assert store.provider.name in ("offline", "fastembed", "azure_openai", "openai", "tfidf", "hash", "gemini", "ollama")
    assert store.vector_store in ("memory", "qdrant", "azure_ai_search")


def test_langgraph_intake_invokes():
    status = try_langgraph_compile()
    assert "langgraph" in status
    state = run_intake_assist(
        {
            "requestNumber": "RLogistics-T",
            "contactName": "Alex",
            "contactEmail": "a@b.com",
            "site": "HQ",
            "status": "Created",
            "dispositionType": "Sanitize",
            "requestType": "UsSurplus",
            "pickupCity": "CLT",
            "assets": [
                {
                    "assetType": "Laptop",
                    "manufacturer": "Dell",
                    "model": "1",
                    "deviceGuid": "g",
                    "quantity": 1,
                }
            ],
        },
        sop_hits=[{"title": "SOP", "text": "GUID required", "score": 0.9}],
    )
    assert state.get("completeness")
    assert state.get("clarification_draft")
    assert state.get("engine") in ("langgraph", "fallback")


def test_langgraph_quote_cycle():
    state = run_quote_cycle(
        {"requestNumber": "RLogistics-1", "dispositionType": "Destroy"},
        [{"id": 1, "name": "IronVault Destruction", "type": "Processing"}],
        email_body="Total $500 in 3 business days",
    )
    assert state.get("recommendation")
    assert state.get("quote_parse")


def test_tool_specs_cover_plan_tools():
    names = {t["name"] for t in list_tool_specs()}
    assert {
        "get_request",
        "intake_assist",
        "rag_search",
        "parse_quote",
        "send_clarification",
        "send_vendor_quotes",
    } <= names


@pytest.mark.asyncio
async def test_call_tool_rag_search():
    # Ensure indexed via shared path
    from app.tools import ensure_rag_indexed

    ensure_rag_indexed()
    result = await call_tool("rag_search", {"query": "vendor quotes transport", "top_k": 2})
    assert isinstance(result, list)
    assert len(result) <= 2


@pytest.mark.asyncio
async def test_call_tool_parse_quote():
    result = await call_tool(
        "parse_quote",
        {"body": "Quote total $1,000.00 for 5 business days", "request_number": "RLogistics-9"},
    )
    assert result["totalAmount"] == 1000.0
    assert result["etaDays"] == 5
