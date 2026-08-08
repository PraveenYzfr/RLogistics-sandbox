# RLogisticsGENIE — runbook (Redis + Qdrant + GenAI sidecar)

## Quick start (local)

### 1) Infra
```powershell
cd d:\Praveen\Projects\RLogistics
docker compose -f infra/docker-compose.yml up -d redis qdrant
```
Enable Redis in Core when Redis is up:
```json
"Redis": { "Enabled": true, "Configuration": "localhost:6379" }
```

### 2) RLogistics Core (:5088)
```powershell
dotnet run --project src/RLogistics --urls http://localhost:5088
```

### 3) RLogisticsGENIE (:8090)
```powershell
cd src/RLogisticsGENIE
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:RLOGISTICS_URL="http://localhost:5088"
$env:RLOGISTICS_API_KEY="rlogistics-demo-coord-key-change-me"
$env:REDIS_URL="redis://localhost:6379/0"
$env:QDRANT_URL="http://localhost:6333"
$env:GENIE_LLM_MODE="offline"
uvicorn app.main:app --host 0.0.0.0 --port 8090 --reload
```

### 4) Verify
- RLogisticsGENIE health: http://localhost:8090/health  
  - Expect `mcp: "sdk-stdio"`, `langgraph: "langgraph-ok"`, `rag.backend` = `fastembed` | `tfidf` | `hash`
- Proxy: http://localhost:5088/api/genie/health  
- Process screen **RLogisticsGENIE assist** panel  

## Architecture

| Piece | Implementation |
|-------|----------------|
| **HTTP API** | FastAPI — skills + graphs + RAG |
| **Tools** | Shared [`app/tools.py`](../src/RLogisticsGENIE/app/tools.py) |
| **MCP server** | Official SDK stdio — `python -m app.mcp_stdio` |
| **MCP client** | [`app/mcp_client.py`](../src/RLogisticsGENIE/app/mcp_client.py) for agents/tests; Cursor uses example config |
| **RAG** | Chunked KB + **fastembed** (preferred) / **TF-IDF** fallback / hash last resort → Qdrant or memory |
| **LangGraph** | Real `StateGraph.invoke` for intake + quote (fallback sequential if compile fails) |
| **Core client** | HTTP only — RLogisticsGENIE never owns SQL |

## RLogisticsGENIE endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/health` | redis/qdrant/rag/mcp/langgraph |
| GET | `/v1/completeness/{id}` | gaps + score |
| GET | `/v1/summarize/{id}` | summary + RAG hints |
| GET | `/v1/intake/{id}` | G1 LangGraph draft (HITL) |
| POST | `/v1/intake/{id}/approve` | post clarification to Core |
| GET | `/v1/vendors/recommend/{id}` | G2 vendor recommend |
| POST | `/v1/quotes/{id}/send` | Core vendor-quotes |
| POST | `/v1/quotes/parse` | paste email → structured quote |
| GET | `/v1/rag/search?q=` | chunked SOP RAG |
| GET | `/v1/tools` | tool catalog (shared layer) |
| POST | `/v1/tools/call` | invoke tool (HTTP) |
| GET | `/v1/mcp/tools` | list via MCP client (stdio) |
| POST | `/v1/mcp/call` | call via MCP client (lab smoke) |

## MCP (Cursor)

1. Copy [`.cursor/mcp.json.example`](../.cursor/mcp.json.example) to your Cursor MCP settings (adjust python path / cwd).
2. Ensure Core is running if you call Core-backed tools.
3. Entrypoint:

```powershell
cd src/RLogisticsGENIE
.\.venv\Scripts\python.exe -m app.mcp_stdio
```

Tools: `get_request`, `list_requests`, `intake_assist`, `recommend_vendors`, `parse_quote`, `rag_search`, `send_vendor_quotes`, `send_clarification`.

In-repo client smoke (with RLogisticsGENIE deps installed):

```powershell
python -c "import asyncio; from app.mcp_client import list_mcp_tools; print(asyncio.run(list_mcp_tools()))"
```

## RAG (switchable embeddings)

Default lab mode is **`offline`** (TF-IDF) — fine for local mock, **not** production semantic search.

| `RAG_EMBEDDING_PROVIDER` | Use when | Grade |
|--------------------------|----------|--------|
| `offline` | Local TF-IDF mock, no keys | Sandbox only |
| **`fastembed`** | Local neural laptop demo | Local / demo |
| **`ollama`** | Local / self-hosted Ollama | Self-hosted (you own SLA) |
| **`azure_openai`** | Azure OpenAI embeddings | Azure enterprise path |
| **`gemini`** | Google Gemini embeddings | Google path |
| `openai` | Public OpenAI API lab | Usually not bank path |

### Models

**Azure:** `text-embedding-3-small` (default) or `text-embedding-3-large`  
**Gemini:** `text-embedding-004` (default) or `gemini-embedding-001`  
**Ollama:** `nomic-embed-text` (default) or `mxbai-embed-large` / `bge-m3`  
**Local ONNX:** fastembed `BAAI/bge-small-en-v1.5`

```powershell
# Local ONNX
$env:RAG_EMBEDDING_PROVIDER="fastembed"

# Ollama (pull model first: ollama pull nomic-embed-text)
$env:RAG_EMBEDDING_PROVIDER="ollama"
$env:OLLAMA_BASE_URL="http://localhost:11434"
$env:OLLAMA_EMBEDDING_MODEL="nomic-embed-text"

# Azure
$env:RAG_EMBEDDING_PROVIDER="azure_openai"
$env:AZURE_OPENAI_ENDPOINT="https://YOUR-RESOURCE.openai.azure.com"
$env:AZURE_OPENAI_API_KEY="..."
$env:AZURE_OPENAI_EMBEDDING_DEPLOYMENT="text-embedding-3-small"

# Gemini
$env:RAG_EMBEDDING_PROVIDER="gemini"
$env:GEMINI_API_KEY="..."
$env:GEMINI_EMBEDDING_MODEL="text-embedding-004"
```

See [`.env.example`](../src/RLogisticsGENIE/.env.example) and [`docs/Embeddings.md`](Embeddings.md).

- KB: `src/RLogisticsGENIE/kb/*.md` (chunked ~700 chars)
- Vectors: Qdrant `rlogistics_sops` when up; else in-memory
- **Azure AI Search** integrated vectorization = next platform step (still deferred)

Health: `GET /health` → `rag.provider`, `embeddings_enterprise`, `usage.today_*`, `limits`

## Observability / spend / eval

See [`docs/Observability-Eval.md`](Observability-Eval.md).

| API | Purpose |
|-----|---------|
| `GET /v1/observability/summary` | calls + est. $ by operation |
| `GET /v1/observability/events` | recent usage events |
| `GET /v1/observability/limits` | remaining RPM / budget |
| `GET/POST /v1/eval/cases` | create/list eval cases |
| `POST /v1/eval/cases/{id}/sme` | SME score |
| `GET /v1/eval/metrics` | AI vs SME performance |

Admin UI: RLogistics `/Admin/AiEval`.

## LangGraph

- Intake: score → rag attach → summarize → draft (HITL)
- Quote: recommend → optional parse
- Health field `langgraph` should be `langgraph-ok` when package compiles

## Tests

```powershell
cd d:\Praveen\Projects\RLogistics
.\src\RLogisticsGENIE\.venv\Scripts\python.exe -m pip install -r src/RLogisticsGENIE/requirements.txt
.\src\RLogisticsGENIE\.venv\Scripts\python.exe -m pytest tests/RLogisticsGENIE.Tests -q
```

## LLM modes
- `offline` (default): rule-based skills + local RAG  
- `openai` / `azure`: env keys for future model calls
