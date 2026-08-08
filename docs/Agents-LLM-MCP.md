# Multi-agent, dual-tier LLM, MCP HTTP, vector backends

## Part 1 — Multi-agent (B)

**Supervisor** orchestrates:

1. **IntakeAgent** (LLM **low**) — gaps, draft clarification, RAG  
2. **ComplianceAgent** (LLM **high**) — Device GUID / disposition veto  
3. **VendorAgent** (LLM **low**) — recommend / propose send quotes  
4. **Supervisor** (LLM **high**) — `ready_for_pickup` **proposal only**

API: `POST /v1/agents/run/{request_id}`  
UI: Coordinator Process → **Run multi-agent**

**HITL:** `send_clarification` / `send_vendor_quotes` / status changes are **never** auto-called. States: `pending_hitl`, `blocked`, `ready_for_pickup`, `needs_review`.

## Part 2 — Switches

### LLM vendor + low/high

```powershell
$env:LLM_VENDOR="gemini"   # azure | openai | gemini | ollama | claude | offline
$env:LLM_DEFAULT_TIER="low"
# Both tiers stay on Gemini models:
# GEMINI_LLM_LOW_MODEL / GEMINI_LLM_HIGH_MODEL
```

`GET /v1/llm/status`

### MCP remote (local)

- Stdio unchanged: `python -m app.mcp_stdio`  
- HTTP: `http://127.0.0.1:8090/mcp` (Streamable HTTP, API key)  
- Auth: `X-Api-Key` or `Bearer` = `MCP_API_KEY` or `RLOGISTICS_API_KEY`  
- Bind uvicorn to `127.0.0.1` for local-only

### Vector backend

```powershell
$env:VECTOR_BACKEND="qdrant"          # default local
# $env:VECTOR_BACKEND="memory"
# $env:VECTOR_BACKEND="azure_ai_search"  # needs AZURE_SEARCH_* (cloud from laptop)
```

Qdrant remains the local default; Azure AI Search is optional.
