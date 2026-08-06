# RLogisticsGENIE embeddings — switchable: fastembed | ollama | azure_openai | gemini

## Switch

Set **`RAG_EMBEDDING_PROVIDER`**:

| Value | Role |
|-------|------|
| **`fastembed`** | Local neural (ONNX) — laptop / demo |
| **`ollama`** | Local / self-hosted via Ollama HTTP API |
| **`azure_openai`** | Azure OpenAI embedding deployment — Azure enterprise path |
| **`gemini`** | Google Gemini embedding API — Google path |
| `offline` | TF-IDF mock (no keys) |
| `openai` | Public OpenAI API (lab) |

Factory: [`app/embeddings.py`](../src/RLogisticsGENIE/app/embeddings.py). Example env: [`src/RLogisticsGENIE/.env.example`](../src/RLogisticsGENIE/.env.example).

Restart RLogisticsGENIE after changing provider (re-index / Qdrant dim may change).

## 1) Local — fastembed

```powershell
$env:RAG_EMBEDDING_PROVIDER="fastembed"
# optional: $env:FASTEMBED_MODEL="BAAI/bge-small-en-v1.5"
```

First run may download ONNX weights (HuggingFace). Fine for local mock; not a bank hosting story by itself.

## 2) Ollama (local / self-hosted)

| Model | Typical dims | Note |
|-------|--------------|------|
| **nomic-embed-text** | 768 | Recommended Ollama default |
| mxbai-embed-large | 1024 | Higher quality local |
| bge-m3 | 1024 | Multilingual |

```powershell
ollama pull nomic-embed-text
$env:RAG_EMBEDDING_PROVIDER="ollama"
$env:OLLAMA_BASE_URL="http://localhost:11434"
$env:OLLAMA_EMBEDDING_MODEL="nomic-embed-text"
```

Uses Ollama `/api/embed` (falls back to legacy `/api/embeddings`). Good for air-gapped / keep-data-local labs; you own patching, scaling, and SLA — not a managed cloud enterprise path like Azure/Gemini.

If Ollama is down, RLogisticsGENIE falls back to `offline` TF-IDF.

## 3) Azure OpenAI (enterprise Azure-shaped)

| Deployment | Dims | Note |
|------------|------|------|
| **text-embedding-3-small** | 1536 | Recommended default |
| **text-embedding-3-large** | 3072 | Higher quality |

```powershell
$env:RAG_EMBEDDING_PROVIDER="azure_openai"
$env:AZURE_OPENAI_ENDPOINT="https://YOUR-RESOURCE.openai.azure.com"
$env:AZURE_OPENAI_API_KEY="..."
$env:AZURE_OPENAI_EMBEDDING_DEPLOYMENT="text-embedding-3-small"
```

## 4) Gemini (Google-shaped)

| Model | Typical dims | Note |
|-------|--------------|------|
| **text-embedding-004** | 768 | Solid default via Gemini API |
| gemini-embedding-001 | up to 3072 | Newer; confirm in your Google project |

```powershell
$env:RAG_EMBEDDING_PROVIDER="gemini"
$env:GEMINI_API_KEY="..."
$env:GEMINI_EMBEDDING_MODEL="text-embedding-004"
```

Uses Google Generative Language API (`batchEmbedContents`). Vertex AI auth can be a later enterprise hardening step with the same model family.

## Claude?

Anthropic **Claude has no public embeddings API**. Use Claude later as the **LLM** for drafts/summaries; keep vectors on Azure OpenAI, Gemini, Ollama, or local fastembed.

## Health

`GET http://localhost:8090/health` → `rag.provider`, `embeddings_enterprise`.
