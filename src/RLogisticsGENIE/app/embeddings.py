"""
Switchable embedding providers for GENIE RAG.

Primary switches (settings.rag_embedding_provider):
  fastembed    — local ONNX neural (lab / laptop)
  ollama       — local / self-hosted via Ollama HTTP API
  azure_openai — Azure OpenAI embedding deployment (enterprise Azure path)
  gemini       — Google Gemini embedding API (Google path)

Also available:
  offline      — TF-IDF local mock (no keys)
  openai       — public OpenAI embeddings API
"""

from __future__ import annotations

import logging
import math
import re
from abc import ABC, abstractmethod
from collections import Counter
from typing import Any

import httpx

from app.config import settings

log = logging.getLogger("rlogistics.genie.embeddings")

HASH_DIM = 256


def _tokenize(text: str) -> list[str]:
    return re.findall(r"[a-z0-9]+", text.lower())


def hash_embed(text: str, dim: int = HASH_DIM) -> list[float]:
    vec = [0.0] * dim
    tokens = _tokenize(text)
    if not tokens:
        return vec
    for t in tokens:
        vec[hash(t) % dim] += 1.0
    for a, b in zip(tokens, tokens[1:]):
        vec[hash(a + "_" + b) % dim] += 0.5
    norm = math.sqrt(sum(v * v for v in vec)) or 1.0
    return [v / norm for v in vec]


class EmbeddingProvider(ABC):
    name: str = "base"

    @property
    @abstractmethod
    def dim(self) -> int: ...

    @abstractmethod
    def embed_batch(self, texts: list[str]) -> list[list[float]]: ...

    def embed(self, text: str) -> list[float]:
        return self.embed_batch([text])[0]

    def fit_corpus(self, texts: list[str]) -> None:
        return None


class OfflineTfidfProvider(EmbeddingProvider):
    name = "offline"

    def __init__(self, max_features: int = 2048) -> None:
        self.max_features = max_features
        self.vocab: dict[str, int] = {}
        self.idf: list[float] = []
        self._dim = HASH_DIM

    @property
    def dim(self) -> int:
        return self._dim if self.vocab else HASH_DIM

    def fit_corpus(self, texts: list[str]) -> None:
        df: Counter[str] = Counter()
        tokenized = [_tokenize(d) for d in texts]
        for toks in tokenized:
            for t in set(toks):
                df[t] += 1
        terms = [t for t, _ in df.most_common(self.max_features)]
        self.vocab = {t: i for i, t in enumerate(terms)}
        self._dim = len(terms) or 1
        n = max(len(texts), 1)
        self.idf = [0.0] * self._dim
        for t, i in self.vocab.items():
            self.idf[i] = math.log((1 + n) / (1 + df[t])) + 1.0

    def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not self.vocab:
            self.fit_corpus(texts)
        return [self._embed_one(t) for t in texts]

    def _embed_one(self, text: str) -> list[float]:
        if not self.vocab:
            return hash_embed(text, HASH_DIM)
        tf: Counter[str] = Counter(_tokenize(text))
        total = sum(tf.values()) or 1
        vec = [0.0] * self._dim
        for t, c in tf.items():
            i = self.vocab.get(t)
            if i is None:
                continue
            vec[i] = (c / total) * self.idf[i]
        norm = math.sqrt(sum(v * v for v in vec)) or 1.0
        return [v / norm for v in vec]


class FastEmbedProvider(EmbeddingProvider):
    name = "fastembed"

    def __init__(self, model_name: str | None = None) -> None:
        from fastembed import TextEmbedding

        self.model_name = model_name or settings.fastembed_model
        self._model = TextEmbedding(model_name=self.model_name)
        sample = list(self._model.embed(["dim probe"]))[0]
        self._dim = len(list(sample))

    @property
    def dim(self) -> int:
        return self._dim

    def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        return [[float(x) for x in vec] for vec in self._model.embed(texts)]


class OllamaEmbeddingProvider(EmbeddingProvider):
    """Local / self-hosted embeddings via Ollama (`/api/embed`)."""

    name = "ollama"

    _KNOWN_DIMS = {
        "nomic-embed-text": 768,
        "mxbai-embed-large": 1024,
        "all-minilm": 384,
        "bge-m3": 1024,
        "snowflake-arctic-embed": 1024,
    }

    def __init__(self) -> None:
        self.base_url = (settings.ollama_base_url or "http://localhost:11434").rstrip("/")
        self.model = settings.ollama_embedding_model or "nomic-embed-text"
        self._dim = (
            settings.rag_embedding_dimensions
            or self._KNOWN_DIMS.get(self.model.lower(), 768)
        )
        with httpx.Client(timeout=5.0) as client:
            r = client.get(f"{self.base_url}/api/tags")
            r.raise_for_status()

    @property
    def dim(self) -> int:
        return self._dim

    def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        with httpx.Client(timeout=120.0) as client:
            r = client.post(
                f"{self.base_url}/api/embed",
                json={"model": self.model, "input": texts},
            )
            if r.status_code == 404:
                return [self._embed_one_legacy(client, t) for t in texts]
            r.raise_for_status()
            vectors = r.json().get("embeddings") or []
            if len(vectors) != len(texts):
                return [self._embed_one_legacy(client, t) for t in texts]
            if vectors:
                self._dim = len(vectors[0])
            return [[float(x) for x in row] for row in vectors]

    def _embed_one_legacy(self, client: httpx.Client, text: str) -> list[float]:
        r = client.post(
            f"{self.base_url}/api/embeddings",
            json={"model": self.model, "prompt": text},
        )
        r.raise_for_status()
        values = r.json()["embedding"]
        self._dim = len(values)
        return [float(x) for x in values]


class AzureOpenAIEmbeddingProvider(EmbeddingProvider):
    name = "azure_openai"

    _KNOWN_DIMS = {
        "text-embedding-3-large": 3072,
        "text-embedding-3-small": 1536,
        "text-embedding-ada-002": 1536,
    }

    def __init__(self) -> None:
        if not settings.azure_openai_endpoint or not settings.azure_openai_api_key:
            raise ValueError(
                "azure_openai requires AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_API_KEY"
            )
        if not settings.azure_openai_embedding_deployment:
            raise ValueError("Set AZURE_OPENAI_EMBEDDING_DEPLOYMENT (e.g. text-embedding-3-small)")
        self.endpoint = settings.azure_openai_endpoint.rstrip("/")
        self.api_key = settings.azure_openai_api_key
        self.api_version = settings.azure_openai_api_version
        self.deployment = settings.azure_openai_embedding_deployment
        self._dim = (
            settings.rag_embedding_dimensions
            or self._KNOWN_DIMS.get(self.deployment.lower(), 1536)
        )

    @property
    def dim(self) -> int:
        return self._dim

    def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        url = (
            f"{self.endpoint}/openai/deployments/{self.deployment}/embeddings"
            f"?api-version={self.api_version}"
        )
        headers = {"api-key": self.api_key, "Content-Type": "application/json"}
        body: dict[str, Any] = {"input": texts}
        if settings.rag_embedding_dimensions and "embedding-3" in self.deployment:
            body["dimensions"] = settings.rag_embedding_dimensions
        with httpx.Client(timeout=60.0) as client:
            r = client.post(url, headers=headers, json=body)
            r.raise_for_status()
            data = sorted(r.json()["data"], key=lambda x: x["index"])
            vectors = [row["embedding"] for row in data]
            if vectors:
                self._dim = len(vectors[0])
            return vectors


class GeminiEmbeddingProvider(EmbeddingProvider):
    """Google Gemini embeddings via Generative Language API (API key)."""

    name = "gemini"

    _KNOWN_DIMS = {
        "text-embedding-004": 768,
        "embedding-001": 768,
        "gemini-embedding-001": 3072,
    }

    def __init__(self) -> None:
        if not settings.gemini_api_key:
            raise ValueError("gemini embedding provider requires GEMINI_API_KEY")
        self.api_key = settings.gemini_api_key
        model = settings.gemini_embedding_model.strip()
        if model.startswith("models/"):
            model = model[len("models/") :]
        self.model = model
        self._dim = (
            settings.rag_embedding_dimensions
            or self._KNOWN_DIMS.get(self.model.lower(), 768)
        )

    @property
    def dim(self) -> int:
        return self._dim

    def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        url = (
            f"https://generativelanguage.googleapis.com/v1beta/models/"
            f"{self.model}:batchEmbedContents?key={self.api_key}"
        )
        requests_body = []
        for t in texts:
            item: dict[str, Any] = {
                "model": f"models/{self.model}",
                "content": {"parts": [{"text": t}]},
            }
            if settings.rag_embedding_dimensions:
                item["outputDimensionality"] = settings.rag_embedding_dimensions
            requests_body.append(item)
        with httpx.Client(timeout=90.0) as client:
            r = client.post(url, json={"requests": requests_body})
            r.raise_for_status()
            embeddings = r.json().get("embeddings") or []
            vectors = [row["values"] for row in embeddings]
            if len(vectors) != len(texts):
                vectors = [self._embed_one(t) for t in texts]
            if vectors:
                self._dim = len(vectors[0])
            return vectors

    def _embed_one(self, text: str) -> list[float]:
        url = (
            f"https://generativelanguage.googleapis.com/v1beta/models/"
            f"{self.model}:embedContent?key={self.api_key}"
        )
        body: dict[str, Any] = {
            "model": f"models/{self.model}",
            "content": {"parts": [{"text": text}]},
        }
        if settings.rag_embedding_dimensions:
            body["outputDimensionality"] = settings.rag_embedding_dimensions
        with httpx.Client(timeout=60.0) as client:
            r = client.post(url, json=body)
            r.raise_for_status()
            values = r.json()["embedding"]["values"]
            self._dim = len(values)
            return values


class OpenAIEmbeddingProvider(EmbeddingProvider):
    name = "openai"

    _KNOWN_DIMS = {
        "text-embedding-3-large": 3072,
        "text-embedding-3-small": 1536,
        "text-embedding-ada-002": 1536,
    }

    def __init__(self) -> None:
        if not settings.openai_api_key:
            raise ValueError("openai embedding provider requires OPENAI_API_KEY")
        self.api_key = settings.openai_api_key
        self.model = settings.openai_embedding_model
        self._dim = (
            settings.rag_embedding_dimensions
            or self._KNOWN_DIMS.get(self.model.lower(), 1536)
        )

    @property
    def dim(self) -> int:
        return self._dim

    def embed_batch(self, texts: list[str]) -> list[list[float]]:
        if not texts:
            return []
        body: dict[str, Any] = {"model": self.model, "input": texts}
        if settings.rag_embedding_dimensions and "embedding-3" in self.model:
            body["dimensions"] = settings.rag_embedding_dimensions
        with httpx.Client(timeout=60.0) as client:
            r = client.post(
                "https://api.openai.com/v1/embeddings",
                headers={
                    "Authorization": f"Bearer {self.api_key}",
                    "Content-Type": "application/json",
                },
                json=body,
            )
            r.raise_for_status()
            data = sorted(r.json()["data"], key=lambda x: x["index"])
            vectors = [row["embedding"] for row in data]
            if vectors:
                self._dim = len(vectors[0])
            return vectors


def create_embedding_provider(provider: str | None = None) -> EmbeddingProvider:
    mode = (provider or settings.rag_embedding_provider or "offline").strip().lower()
    try:
        if mode in ("offline", "tfidf", "mock"):
            log.info("Embedding provider: offline (TF-IDF local mock)")
            return OfflineTfidfProvider()
        if mode == "fastembed":
            log.info("Embedding provider: fastembed (%s)", settings.fastembed_model)
            return FastEmbedProvider()
        if mode == "ollama":
            log.info(
                "Embedding provider: ollama model=%s base=%s",
                settings.ollama_embedding_model,
                settings.ollama_base_url,
            )
            return OllamaEmbeddingProvider()
        if mode in ("azure_openai", "azure"):
            log.info(
                "Embedding provider: azure_openai deployment=%s",
                settings.azure_openai_embedding_deployment,
            )
            return AzureOpenAIEmbeddingProvider()
        if mode in ("gemini", "google", "google_gemini"):
            log.info("Embedding provider: gemini model=%s", settings.gemini_embedding_model)
            return GeminiEmbeddingProvider()
        if mode == "openai":
            log.info("Embedding provider: openai model=%s", settings.openai_embedding_model)
            return OpenAIEmbeddingProvider()
        raise ValueError(f"Unknown rag_embedding_provider: {mode}")
    except Exception as ex:
        log.error(
            "Failed to init embedding provider '%s' (%s) — falling back to offline TF-IDF",
            mode,
            ex,
        )
        return OfflineTfidfProvider()


ENTERPRISE_EMBEDDING_GUIDE = {
    "switchable_providers": [
        "fastembed",
        "ollama",
        "azure_openai",
        "gemini",
        "offline",
        "openai",
    ],
    "local": {
        "provider": "fastembed",
        "notes": "Laptop/demo neural embeddings (ONNX).",
    },
    "self_hosted": {
        "provider": "ollama",
        "recommended_models": [
            {"id": "nomic-embed-text", "dims": 768, "notes": "Common Ollama embedding default"},
            {"id": "mxbai-embed-large", "dims": 1024, "notes": "Higher quality local"},
            {"id": "bge-m3", "dims": 1024, "notes": "Multilingual; pull via ollama pull bge-m3"},
        ],
        "notes": "Self-hosted / air-gapped friendly; you own SLA, patching, and hardening.",
    },
    "azure_path": {
        "provider": "azure_openai",
        "recommended_models": [
            {"id": "text-embedding-3-small", "dims": 1536, "notes": "Common Azure enterprise default"},
            {"id": "text-embedding-3-large", "dims": 3072, "notes": "Higher quality"},
        ],
    },
    "google_path": {
        "provider": "gemini",
        "recommended_models": [
            {"id": "text-embedding-004", "dims": 768, "notes": "Default Gemini embedding model"},
            {"id": "gemini-embedding-001", "dims": 3072, "notes": "Newer Gemini embedding; confirm in project"},
        ],
    },
    "claude_note": "Anthropic Claude has no public embeddings API — use Claude for LLM generation, not vectors.",
    "next_platform": "Azure AI Search or Vertex AI Search for managed enterprise RAG",
}
