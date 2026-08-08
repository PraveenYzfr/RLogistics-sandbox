"""
Dual-tier LLM providers.

Switch vendor once — both low (cheap/fast) and high (deeper) stay on that vendor:
  LLM_VENDOR=gemini|azure|openai|ollama|claude|offline
  LLM_DEFAULT_TIER=low|high
"""

from __future__ import annotations

import logging
import time
from abc import ABC, abstractmethod
from typing import Any

import httpx

from app.config import settings
from app.observability import estimate_tokens, record_usage

log = logging.getLogger("rlogistics.genie.llm")

VENDOR_MODELS: dict[str, dict[str, str]] = {
    "offline": {"low": "offline-template", "high": "offline-template"},
    "azure": {
        "low": "azure:" + (settings.azure_openai_llm_low_deployment or "gpt-4o-mini"),
        "high": "azure:" + (settings.azure_openai_llm_high_deployment or "gpt-4o"),
    },
    "openai": {
        "low": settings.openai_llm_low_model or "gpt-4o-mini",
        "high": settings.openai_llm_high_model or "gpt-4o",
    },
    "gemini": {
        "low": settings.gemini_llm_low_model or "gemini-2.0-flash-lite",
        "high": settings.gemini_llm_high_model or "gemini-2.0-flash",
    },
    "claude": {
        "low": settings.claude_llm_low_model or "claude-3-5-haiku-latest",
        "high": settings.claude_llm_high_model or "claude-3-5-sonnet-latest",
    },
    "ollama": {
        "low": settings.ollama_llm_low_model or "llama3.2:3b",
        "high": settings.ollama_llm_high_model or "llama3.1:8b",
    },
}


def resolve_vendor() -> str:
    v = (settings.llm_vendor or settings.genie_llm_mode or "offline").strip().lower()
    if v in ("mock", "tfidf"):
        return "offline"
    if v == "google":
        return "gemini"
    if v == "anthropic":
        return "claude"
    if v not in VENDOR_MODELS:
        return "offline"
    return v


def resolve_model(tier: str | None = None) -> tuple[str, str]:
    """Return (vendor, model_id) for tier low|high."""
    vendor = resolve_vendor()
    t = (tier or settings.llm_default_tier or "low").strip().lower()
    if t not in ("low", "high"):
        t = "low"
    # Refresh azure labels from live settings
    catalog = {
        "offline": {"low": "offline-template", "high": "offline-template"},
        "azure": {
            "low": settings.azure_openai_llm_low_deployment or "gpt-4o-mini",
            "high": settings.azure_openai_llm_high_deployment or "gpt-4o",
        },
        "openai": {
            "low": settings.openai_llm_low_model or "gpt-4o-mini",
            "high": settings.openai_llm_high_model or "gpt-4o",
        },
        "gemini": {
            "low": settings.gemini_llm_low_model or "gemini-2.0-flash-lite",
            "high": settings.gemini_llm_high_model or "gemini-2.0-flash",
        },
        "claude": {
            "low": settings.claude_llm_low_model or "claude-3-5-haiku-latest",
            "high": settings.claude_llm_high_model or "claude-3-5-sonnet-latest",
        },
        "ollama": {
            "low": settings.ollama_llm_low_model or "llama3.2:3b",
            "high": settings.ollama_llm_high_model or "llama3.1:8b",
        },
    }
    return vendor, catalog[vendor][t]


class LlmProvider(ABC):
    name: str = "base"

    @abstractmethod
    def complete(self, system: str, user: str, *, tier: str = "low") -> str: ...


class OfflineLlmProvider(LlmProvider):
    name = "offline"

    def complete(self, system: str, user: str, *, tier: str = "low") -> str:
        # Deterministic stub for lab — echoes task intent
        snippet = (user or "")[:400]
        return (
            f"[offline:{tier}] Based on the task, proceed with structured analysis.\n"
            f"Context excerpt: {snippet}\n"
            f"Recommendation: follow HITL for any Core writes."
        )


class AzureOpenAILlmProvider(LlmProvider):
    name = "azure"

    def complete(self, system: str, user: str, *, tier: str = "low") -> str:
        if not settings.azure_openai_endpoint or not settings.azure_openai_api_key:
            raise ValueError("azure LLM requires AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_API_KEY")
        _, model = resolve_model(tier)
        url = (
            f"{settings.azure_openai_endpoint.rstrip('/')}/openai/deployments/{model}/chat/completions"
            f"?api-version={settings.azure_openai_api_version}"
        )
        body = {
            "messages": [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            "temperature": 0.2 if tier == "high" else 0.4,
        }
        with httpx.Client(timeout=90.0) as client:
            r = client.post(
                url,
                headers={"api-key": settings.azure_openai_api_key, "Content-Type": "application/json"},
                json=body,
            )
            r.raise_for_status()
            return r.json()["choices"][0]["message"]["content"]


class OpenAILlmProvider(LlmProvider):
    name = "openai"

    def complete(self, system: str, user: str, *, tier: str = "low") -> str:
        if not settings.openai_api_key:
            raise ValueError("openai LLM requires OPENAI_API_KEY")
        _, model = resolve_model(tier)
        with httpx.Client(timeout=90.0) as client:
            r = client.post(
                "https://api.openai.com/v1/chat/completions",
                headers={
                    "Authorization": f"Bearer {settings.openai_api_key}",
                    "Content-Type": "application/json",
                },
                json={
                    "model": model,
                    "messages": [
                        {"role": "system", "content": system},
                        {"role": "user", "content": user},
                    ],
                    "temperature": 0.2 if tier == "high" else 0.4,
                },
            )
            r.raise_for_status()
            return r.json()["choices"][0]["message"]["content"]


class GeminiLlmProvider(LlmProvider):
    name = "gemini"

    def complete(self, system: str, user: str, *, tier: str = "low") -> str:
        if not settings.gemini_api_key:
            raise ValueError("gemini LLM requires GEMINI_API_KEY")
        _, model = resolve_model(tier)
        url = (
            f"https://generativelanguage.googleapis.com/v1beta/models/"
            f"{model}:generateContent?key={settings.gemini_api_key}"
        )
        body = {
            "systemInstruction": {"parts": [{"text": system}]},
            "contents": [{"role": "user", "parts": [{"text": user}]}],
        }
        with httpx.Client(timeout=90.0) as client:
            r = client.post(url, json=body)
            r.raise_for_status()
            parts = r.json()["candidates"][0]["content"]["parts"]
            return "".join(p.get("text", "") for p in parts)


class ClaudeLlmProvider(LlmProvider):
    name = "claude"

    def complete(self, system: str, user: str, *, tier: str = "low") -> str:
        if not settings.anthropic_api_key:
            raise ValueError("claude LLM requires ANTHROPIC_API_KEY")
        _, model = resolve_model(tier)
        with httpx.Client(timeout=90.0) as client:
            r = client.post(
                "https://api.anthropic.com/v1/messages",
                headers={
                    "x-api-key": settings.anthropic_api_key,
                    "anthropic-version": "2023-06-01",
                    "Content-Type": "application/json",
                },
                json={
                    "model": model,
                    "max_tokens": 1024 if tier == "low" else 2048,
                    "system": system,
                    "messages": [{"role": "user", "content": user}],
                },
            )
            r.raise_for_status()
            blocks = r.json().get("content") or []
            return "".join(b.get("text", "") for b in blocks if b.get("type") == "text")


class OllamaLlmProvider(LlmProvider):
    name = "ollama"

    def complete(self, system: str, user: str, *, tier: str = "low") -> str:
        _, model = resolve_model(tier)
        base = settings.ollama_base_url.rstrip("/")
        with httpx.Client(timeout=120.0) as client:
            r = client.post(
                f"{base}/api/chat",
                json={
                    "model": model,
                    "stream": False,
                    "messages": [
                        {"role": "system", "content": system},
                        {"role": "user", "content": user},
                    ],
                },
            )
            r.raise_for_status()
            return r.json()["message"]["content"]


def create_llm_provider(vendor: str | None = None) -> LlmProvider:
    v = (vendor or resolve_vendor()).lower()
    try:
        if v == "offline":
            return OfflineLlmProvider()
        if v == "azure":
            return AzureOpenAILlmProvider()
        if v == "openai":
            return OpenAILlmProvider()
        if v == "gemini":
            return GeminiLlmProvider()
        if v == "claude":
            return ClaudeLlmProvider()
        if v == "ollama":
            return OllamaLlmProvider()
        raise ValueError(f"unknown llm vendor {v}")
    except Exception as ex:
        log.error("LLM provider '%s' failed (%s) — offline fallback", v, ex)
        return OfflineLlmProvider()


def llm_complete(system: str, user: str, *, tier: str = "low", operation: str = "llm") -> dict[str, Any]:
    vendor, model = resolve_model(tier)
    provider = create_llm_provider(vendor)
    t0 = time.perf_counter()
    ok = True
    err = None
    text = ""
    try:
        text = provider.complete(system, user, tier=tier)
    except Exception as ex:
        ok = False
        err = str(ex)
        text = OfflineLlmProvider().complete(system, user, tier=tier)
        vendor, model = "offline", "offline-template"
    latency = (time.perf_counter() - t0) * 1000
    record_usage(
        operation=operation,
        provider=vendor,
        model=model,
        input_tokens=estimate_tokens(system + user),
        output_tokens=estimate_tokens(text),
        latency_ms=latency,
        ok=ok,
        error=err,
    )
    return {
        "text": text,
        "vendor": vendor,
        "tier": tier,
        "model": model,
        "ok": ok,
        "error": err,
    }


def llm_status() -> dict[str, Any]:
    vendor = resolve_vendor()
    low = resolve_model("low")
    high = resolve_model("high")
    return {
        "vendor": vendor,
        "default_tier": settings.llm_default_tier,
        "low": {"vendor": low[0], "model": low[1]},
        "high": {"vendor": high[0], "model": high[1]},
        "note": "Switch LLM_VENDOR once; both low and high use that vendor's models.",
    }
