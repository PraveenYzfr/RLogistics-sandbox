"""Chunked RAG with switchable embedding providers + Qdrant (in-memory fallback)."""

from __future__ import annotations

import hashlib
import logging
import math
import re
from pathlib import Path
from typing import Any

from app.config import settings
from app.embeddings import ENTERPRISE_EMBEDDING_GUIDE, EmbeddingProvider, create_embedding_provider

log = logging.getLogger("rlogistics.genie.rag")

CHUNK_SIZE = 700
CHUNK_OVERLAP = 100

_shared: RagStore | None = None


def get_shared_rag() -> RagStore:
    global _shared
    if _shared is None:
        _shared = RagStore()
    return _shared


def chunk_text(text: str, chunk_size: int = CHUNK_SIZE, overlap: int = CHUNK_OVERLAP) -> list[str]:
    text = text.strip()
    if not text:
        return []
    if len(text) <= chunk_size:
        return [text]

    chunks: list[str] = []
    start = 0
    while start < len(text):
        end = min(start + chunk_size, len(text))
        if end < len(text):
            window = text[start:end]
            br = max(window.rfind("\n\n"), window.rfind("\n"), window.rfind(". "))
            if br > chunk_size // 3:
                end = start + br + 1
        piece = text[start:end].strip()
        if piece:
            chunks.append(piece)
        if end >= len(text):
            break
        start = max(end - overlap, start + 1)
    return chunks


def _cosine(a: list[float], b: list[float]) -> float:
    if not a or not b or len(a) != len(b):
        return 0.0
    return sum(x * y for x, y in zip(a, b))


def _stable_id(s: str) -> int:
    return int(hashlib.md5(s.encode("utf-8")).hexdigest()[:12], 16) % (10**9)


class RagStore:
    def __init__(self, provider: EmbeddingProvider | None = None) -> None:
        self._mem: list[dict[str, Any]] = []
        self._qdrant = None
        self._qm = None
        self._azure_search = False
        self._provider = provider or create_embedding_provider()
        self._dim = self._provider.dim
        self._vector_backend = (settings.vector_backend or "qdrant").strip().lower()
        if self._vector_backend == "azure_ai_search":
            self._init_azure_search()
        elif self._vector_backend == "memory":
            log.info("RAG vector store: memory only")
        else:
            self._init_qdrant()

    @property
    def chunk_count(self) -> int:
        if self._mem:
            return len(self._mem)
        if self._qdrant:
            try:
                info = self._qdrant.get_collection(settings.qdrant_collection)
                return int(info.points_count or 0)
            except Exception:
                return 0
        return 0

    @property
    def backend(self) -> str:
        return f"{self._provider.name}+{self.vector_store}"

    @property
    def vector_store(self) -> str:
        if self._azure_search:
            return "azure_ai_search"
        if self._qdrant:
            return "qdrant"
        return "memory"

    @property
    def dim(self) -> int:
        return self._dim

    @property
    def provider(self) -> EmbeddingProvider:
        return self._provider

    def _init_azure_search(self) -> None:
        if settings.azure_search_endpoint and settings.azure_search_api_key:
            self._azure_search = True
            log.info("RAG vector store: azure_ai_search %s", settings.azure_search_endpoint)
        else:
            self._azure_search = False
            log.warning("azure_ai_search selected but endpoint/key missing — memory fallback")
            self._vector_backend = "memory"

    def _init_qdrant(self) -> None:
        try:
            from qdrant_client import QdrantClient
            from qdrant_client.http import models as qm

            client = QdrantClient(url=settings.qdrant_url, timeout=5)
            client.get_collections()
            self._qdrant = client
            self._qm = qm
            log.info("RAG vector store: qdrant %s", settings.qdrant_url)
        except Exception as ex:
            self._qdrant = None
            self._qm = None
            log.warning("Qdrant unavailable (%s) — in-memory RAG", ex)

    def embed(self, text: str) -> list[float]:
        return self._provider.embed(text)

    def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        self._provider.fit_corpus(texts)
        vectors = self._provider.embed_batch(texts)
        if vectors:
            self._dim = len(vectors[0])
        return vectors

    def _ensure_collection(self) -> None:
        if not self._qdrant or not self._qm:
            return
        qm = self._qm
        names = [c.name for c in self._qdrant.get_collections().collections]
        if settings.qdrant_collection in names:
            try:
                info = self._qdrant.get_collection(settings.qdrant_collection)
                existing = info.config.params.vectors.size  # type: ignore[attr-defined]
                if existing != self._dim:
                    log.warning(
                        "Recreating Qdrant collection %s (dim %s -> %s)",
                        settings.qdrant_collection,
                        existing,
                        self._dim,
                    )
                    self._qdrant.delete_collection(settings.qdrant_collection)
                    names = [c.name for c in self._qdrant.get_collections().collections]
            except Exception:
                pass
        if settings.qdrant_collection not in names:
            self._qdrant.create_collection(
                collection_name=settings.qdrant_collection,
                vectors_config=qm.VectorParams(size=self._dim, distance=qm.Distance.COSINE),
            )

    def index_documents(self, docs: list[dict[str, str]]) -> int:
        chunks: list[dict[str, Any]] = []
        for d in docs:
            pieces = chunk_text(d["text"])
            for i, piece in enumerate(pieces):
                cid = f"{d['id']}::chunk-{i}"
                chunks.append(
                    {
                        "id": cid,
                        "doc_id": d["id"],
                        "title": d["title"],
                        "text": piece,
                        "chunk_index": i,
                    }
                )

        if not chunks:
            self._mem = []
            return 0

        vectors = self.embed_batch([c["text"] for c in chunks])
        self._dim = len(vectors[0]) if vectors else self._dim
        for c, v in zip(chunks, vectors):
            c["vector"] = v

        if self._qdrant and self._qm:
            try:
                self._ensure_collection()
                qm = self._qm
                points = [
                    qm.PointStruct(
                        id=_stable_id(c["id"]),
                        vector=c["vector"],
                        payload={
                            "title": c["title"],
                            "text": c["text"],
                            "doc_id": c["doc_id"],
                            "chunk_id": c["id"],
                            "chunk_index": c["chunk_index"],
                        },
                    )
                    for c in chunks
                ]
                self._qdrant.upsert(collection_name=settings.qdrant_collection, points=points)
            except Exception as ex:
                log.warning("Qdrant upsert failed: %s", ex)

        if self._azure_search:
            try:
                self._azure_upsert(chunks)
            except Exception as ex:
                log.warning("Azure AI Search upsert failed: %s", ex)

        self._mem = chunks
        return len(chunks)

    def _azure_headers(self) -> dict[str, str]:
        return {"api-key": settings.azure_search_api_key, "Content-Type": "application/json"}

    def _azure_upsert(self, chunks: list[dict[str, Any]]) -> None:
        import httpx

        # Assumes index already has vector field `contentVector` + text fields.
        endpoint = settings.azure_search_endpoint.rstrip("/")
        url = (
            f"{endpoint}/indexes/{settings.azure_search_index}/docs/index"
            f"?api-version={settings.azure_search_api_version}"
        )
        actions = []
        for c in chunks:
            actions.append(
                {
                    "@search.action": "mergeOrUpload",
                    "id": c["id"].replace(":", "_")[:128],
                    "title": c["title"],
                    "text": c["text"],
                    "doc_id": c["doc_id"],
                    "chunk_id": c["id"],
                    "contentVector": c["vector"],
                }
            )
        with httpx.Client(timeout=60.0) as client:
            # batch in 320
            for i in range(0, len(actions), 320):
                r = client.post(url, headers=self._azure_headers(), json={"value": actions[i : i + 320]})
                r.raise_for_status()

    def search(self, query: str, top_k: int = 4) -> list[dict[str, Any]]:
        qv = self.embed(query)
        if self._azure_search:
            try:
                return self._azure_search_query(qv, top_k)
            except Exception as ex:
                log.warning("Azure AI Search query failed: %s", ex)

        if self._qdrant:
            try:
                if hasattr(self._qdrant, "search"):
                    hits = self._qdrant.search(
                        collection_name=settings.qdrant_collection,
                        query_vector=qv,
                        limit=top_k,
                    )
                    return [
                        {
                            "title": h.payload.get("title"),
                            "text": h.payload.get("text"),
                            "score": float(h.score),
                            "doc_id": h.payload.get("doc_id"),
                            "chunk_id": h.payload.get("chunk_id"),
                        }
                        for h in hits
                    ]
                # Newer qdrant-client API
                res = self._qdrant.query_points(
                    collection_name=settings.qdrant_collection,
                    query=qv,
                    limit=top_k,
                )
                points = getattr(res, "points", res) or []
                return [
                    {
                        "title": (p.payload or {}).get("title"),
                        "text": (p.payload or {}).get("text"),
                        "score": float(getattr(p, "score", 0) or 0),
                        "doc_id": (p.payload or {}).get("doc_id"),
                        "chunk_id": (p.payload or {}).get("chunk_id"),
                    }
                    for p in points
                ]
            except Exception as ex:
                log.warning("Qdrant search failed: %s", ex)

        scored = []
        for d in self._mem:
            scored.append(
                {
                    "title": d["title"],
                    "text": d["text"],
                    "score": _cosine(qv, d["vector"]),
                    "doc_id": d.get("doc_id", d.get("id")),
                    "chunk_id": d.get("id"),
                }
            )
        scored.sort(key=lambda x: x["score"], reverse=True)
        return scored[:top_k]

    def _azure_search_query(self, vector: list[float], top_k: int) -> list[dict[str, Any]]:
        import httpx

        endpoint = settings.azure_search_endpoint.rstrip("/")
        url = (
            f"{endpoint}/indexes/{settings.azure_search_index}/docs/search"
            f"?api-version={settings.azure_search_api_version}"
        )
        body = {
            "count": True,
            "select": "title,text,doc_id,chunk_id",
            "vectorQueries": [
                {
                    "kind": "vector",
                    "vector": vector,
                    "fields": "contentVector",
                    "k": top_k,
                }
            ],
        }
        with httpx.Client(timeout=30.0) as client:
            r = client.post(url, headers=self._azure_headers(), json=body)
            r.raise_for_status()
            values = r.json().get("value") or []
            return [
                {
                    "title": v.get("title"),
                    "text": v.get("text"),
                    "score": float(v.get("@search.score") or 0),
                    "doc_id": v.get("doc_id"),
                    "chunk_id": v.get("chunk_id"),
                }
                for v in values[:top_k]
            ]


def load_kb(kb_dir: Path | None = None) -> list[dict[str, str]]:
    root = kb_dir or Path(__file__).resolve().parents[1] / "kb"
    docs: list[dict[str, str]] = []
    if not root.exists():
        return docs
    for p in sorted(root.glob("**/*")):
        if p.suffix.lower() not in {".md", ".txt"}:
            continue
        docs.append(
            {
                "id": p.stem,
                "title": p.stem.replace("-", " ").title(),
                "text": p.read_text(encoding="utf-8"),
            }
        )
    return docs


def embed(text: str) -> list[float]:
    return get_shared_rag().embed(text)


def embedding_status() -> dict[str, Any]:
    store = get_shared_rag()
    return {
        "provider": store.provider.name,
        "vector_store": store.vector_store,
        "backend": store.backend,
        "configured": settings.rag_embedding_provider,
        "vector_backend_setting": settings.vector_backend,
        "dim": store.dim,
        "chunks": store.chunk_count,
        "enterprise_guide": ENTERPRISE_EMBEDDING_GUIDE,
    }
