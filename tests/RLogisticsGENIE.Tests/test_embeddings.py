"""Switchable embedding provider tests."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src" / "RLogisticsGENIE"
sys.path.insert(0, str(ROOT))

from app.embeddings import (  # noqa: E402
    ENTERPRISE_EMBEDDING_GUIDE,
    OfflineTfidfProvider,
    create_embedding_provider,
)
from app.rag import RagStore, load_kb  # noqa: E402


def test_enterprise_guide_lists_primary_switches():
    switches = set(ENTERPRISE_EMBEDDING_GUIDE["switchable_providers"])
    assert {"fastembed", "ollama", "azure_openai", "gemini"} <= switches
    assert ENTERPRISE_EMBEDDING_GUIDE["azure_path"]["provider"] == "azure_openai"
    assert ENTERPRISE_EMBEDDING_GUIDE["google_path"]["provider"] == "gemini"
    assert ENTERPRISE_EMBEDDING_GUIDE["self_hosted"]["provider"] == "ollama"
    assert "Claude" in ENTERPRISE_EMBEDDING_GUIDE["claude_note"]


def test_gemini_provider_requires_api_key():
    import pytest
    from app.embeddings import GeminiEmbeddingProvider

    # Without key, constructor must fail (factory would fall back to offline)
    with pytest.raises(ValueError, match="GEMINI_API_KEY"):
        GeminiEmbeddingProvider()


def test_create_gemini_falls_back_without_key():
    p = create_embedding_provider("gemini")
    # No GEMINI_API_KEY in test env → factory falls back to offline
    assert p.name == "offline"


def test_create_ollama_falls_back_when_unreachable():
    p = create_embedding_provider("ollama")
    # No Ollama daemon in CI → factory falls back to offline
    assert p.name in ("offline", "ollama")
    if p.name == "ollama":
        vecs = p.embed_batch(["device guid clarification"])
        assert len(vecs) == 1
        assert len(vecs[0]) == p.dim


def test_offline_provider_default():
    p = create_embedding_provider("offline")
    assert p.name == "offline"
    assert isinstance(p, OfflineTfidfProvider)
    vecs = p.embed_batch(["Device GUID required", "vendor quotes transport"])
    assert len(vecs) == 2
    assert len(vecs[0]) == p.dim


def test_unknown_provider_falls_back_offline():
    p = create_embedding_provider("not-a-real-provider")
    assert p.name == "offline"


def test_rag_uses_injected_offline_provider():
    store = RagStore(provider=OfflineTfidfProvider())
    n = store.index_documents(load_kb())
    assert n > 0
    assert store.provider.name == "offline"
    assert "offline" in store.backend
    hits = store.search("clarification On Hold Device GUID", top_k=2)
    assert hits
