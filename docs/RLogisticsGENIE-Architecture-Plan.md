# RLogistics RLogisticsGENIE — Architecture & Implementation Plan

**Version:** v1  
**Date:** 2026-08-04  
**Status:** Implementation plan (post Phase 0 RLogistics Core sandbox)  
**Companion:** [RLogistics-GenAI-Opportunity-Map-v1.txt](./RLogistics-GenAI-Opportunity-Map-v1.txt)

---

## 1. North star

RLogisticsGENIE is a **sidecar GenAI engine** that *reads/writes RLogistics through APIs*, never owns disposal business records. Coordinators stay human-in-the-loop for approve / award / cancel.

| Layer | Responsibility |
|--------|----------------|
| **RLogistics Core** (`RLogistics`) | System of record: requests, assets, vendors, templates, outbox, audit |
| **RLogisticsGENIE** (`RLogisticsGENIE`) | LLM agents, RAG, quote parse, recommendations, Graph mail/Teams bridges |
| **Microsoft Graph** | Real mail + Teams (replace mock outbox in bank path) |
| **MCP** | Controlled tool surface for agents (Core + Graph + RAG + policies) |
| **Vector DB** | Embeddings for SOPs, history, similar requests, quote snippets |

```
┌─────────────┐   REST/MCP    ┌──────────────────┐
│ RLogistics UI /    │──────────────►│   RLogistics Core API   │◄── SQL RLogistics
│ ServiceNow  │               │  (system of rec) │
└─────────────┘               └────────┬─────────┘
                                       │ tools only
┌─────────────┐   LangGraph   ┌────────▼─────────┐     Graph
│ Coord chat  │◄─────────────►│   RLogisticsGENIE orchestr. │◄───► Mail / Teams
│ / Process   │               │  LangChain+RAG   │
└─────────────┘               └────────┬─────────┘
                                       │
                              ┌────────▼─────────┐
                              │ Vector DB + blob  │
                              │ SOPs, quotes, CoC │
                              └──────────────────┘
```

---

## 2. What Phase 0 already provides (API contract RLogisticsGENIE will use)

All routes require persona header `X-RLogistics-User-Id` (simulates Entra user). Swagger: `/swagger`. Samples: `src/RLogistics/RLogistics.http`.

| Domain | API | UI parity | Email side-effect |
|--------|-----|-----------|-------------------|
| Personas | `GET /api/users` | Act as… | — |
| List/Get/Create | `GET/POST /api/requests` | Create wizard | `Status_Created` |
| Assign | `POST .../assign` | Claim | `Status_Assigned` |
| Status | `PATCH .../status` | Process | `Status_{name}` |
| Fields | `PATCH .../fields` | Notes | — |
| Plan | `POST .../plan` | Vendors/slots/return-by | status if scheduled |
| Vendor RFQ | `POST .../vendor-quotes` | Send quotes | Transport + Processing templates |
| Return reminder | `POST .../return-reminder` | Process button | `DeviceReturnReminder` |
| Clarification | `POST .../clarifications` | Query user | Clarification + OnHold |
| Reply | `POST .../clarifications/{id}/reply` | User detail | status when unhold |
| Vendors | `GET /api/vendors?type=` | Dropdowns | — |
| Outbox | `GET /api/email-outbox` | Outbox | — |
| Bulk reminders | `POST /api/email-outbox/run-return-reminders` | Outbox | reminders |
| Templates | `GET/PUT /api/admin/email-templates` | Admin | content source |
| Config | `GET/PUT /api/admin/config` | Admin | SLA knobs |

**RLogisticsGENIE rule:** never write SQL to `Requests` directly — only Core APIs, for audit + email parity.

---

## 3. Target repo layout

```text
src/
  RLogistics/                 # DONE — system of record
  RLogisticsGENIE.Api/            # RLogisticsGENIE HTTP API (chat, suggest, parse)
  RLogisticsGENIE.Workers/        # Inbox poll, reminder cron, feed parsers
  RLogisticsGENIE.Agents/         # LangGraph graphs + LangChain chains
  RLogisticsGENIE.Mcp.Server/     # MCP server exposing tools to agents/IDE
  RLogisticsGENIE.Mcp.Client/     # Optional: RLogisticsGENIE as MCP client to Core+Graph
  packages/
    RLogistics.Contracts/          # Shared OpenAPI-generated clients (optional)
docs/
  RLogisticsGENIE-Architecture-Plan.md   # this file
  RLogistics-GenAI-Opportunity-Map-v1.txt
infra/
  docker-compose.yml        # genie-api, chroma/qdrant, redis
```

**Language split (recommended for bank-friendly .NET shop):**

| Component | Stack | Why |
|-----------|--------|-----|
| Core | .NET (done) | SSoT, SQL, Razor |
| RLogisticsGENIE API + Workers | **Python 3.12** FastAPI *or* .NET Semantic Kernel | Python wins for LangGraph/LangChain maturity first |
| MCP server | Python (`mcp`) + thin HTTP tools over Core | Matches Cursor/Claude tooling model |
| Graph | Microsoft Graph SDK (Python or .NET) | Mail + Teams |
| Vector | **Qdrant** (local) → Azure AI Search (bank) | Same hybrid code later |

Pilot: **Python RLogisticsGENIE + .NET Core**. Bank production path: wrap models behind Azure OpenAI / Foundry.

---

## 4. Pillar map (what to build)

### 4.1 LangChain — atomic skills (chains / LCEL)

Small, testable units; **no** multi-step workflow ownership.

| Skill ID | Input | Output | Grounding |
|----------|-------|--------|-----------|
| `completeness_score` | RequestDetail JSON | field gaps, risk score | schema + policy chunks |
| `draft_clarification` | Request + gaps | question text | SOPs |
| `summarize_request` | Request + audit | one-screen brief | live Core only |
| `compose_rfq` | Request + vendor type | subject/body (optional override template) | Core + templates |
| `parse_quote_email` | raw MIME/text | structured QuoteDto | few-shot + schema |
| `recommend_vendors` | Request + history | ranked transport/processing | vector similar + SQL KPIs |
| `status_narrative` | status transition | plain-language update | request state |
| `excel_column_map` | CSV headers sample | map to canonical status model | prior maps |
| `nl_ops_query` | question | plan + SQL/API intents | schema RAG |

LangChain tools = thin clients to **MCP tools** or Direct Core REST.

### 4.2 LangGraph — multi-step agent workflows

Graphs with explicit nodes, checkpoints, and **human interrupt**.

| Graph | Nodes (simplified) | HITL interrupt |
|-------|--------------------|----------------|
| **G1 IntakeAssist** | load request → score completeness → draft fix / clarification → *interrupt* → post clarification | Coordinator approve draft |
| **G2 QuoteCycle** | load request → compose RFQ → Core vendor-quotes → poll Graph inbox → parse quotes → normalize grid → recommend → *interrupt* → plan/select | Award vendor |
| **G3 Orchestrate** | next-best-action → schedule draft → status narrative → reminder check | Confirm pickup |
| **G4 StatusIngest** | receive Excel/mail → map schema → validate transitions → *interrupt on anomaly* → Core status patch | Confirm load |
| **G5 RequestorQa** | retrieve request → RAG policy → answer with citations | Read-only |

**State** (example `GenieState`): `request_id`, `persona_id`, `messages`, `artifacts[]` (parsed quotes), `recommendation`, `approval_pending`, `sources[]`, `audit_trace_id`.

**Checkpointer:** SQLite/Postgres (dev) → Redis/Azure durable (prod). Resume after coordinator UI “Approve”.

### 4.3 RAG + Vector DB

| Corpus | Source | Chunk | Use |
|--------|--------|-------|-----|
| RLogistics SOPs / disposition rules | Markdown/PDF in `docs/kb/` | 500–800 tokens | policy Q&A |
| Email templates + token docs | Core `EmailTemplates` | whole template | composition |
| Historical requests (redacted) | Core API export nightly | summary + site + asset mix | similar cases |
| Vendor quote archive | Parsed quotes | line items + free text | pricing norms |
| CoC / cert samples | Blob store | per-doc sections | compliance later |

**Pipeline:** ingest → chunk → embed (Azure OpenAI `text-embedding-3-small` or local `nomic`) → Qdrant collections `kb_sops`, `req_history`, `quotes`.

**Retrieval:** hybrid (BM25 + vector) when Azure Search available; pure vector in lab.

**Critical:** For live “where is my request?” — **do not** rely on vector alone; always tool-call Core `GET /requests/{id}`. RAG is for policy/SOP; **tools** for transactional truth.

### 4.4 Microsoft Graph (Mail + Teams)

| Capability | Graph API | RLogisticsGENIE use |
|------------|-----------|-----------|
| Send RFQ / status / reminder | `POST /users/{id}/sendMail` | Worker replaces EmailOutbox writer when config `EmailProvider=Graph` |
| Read vendor replies | delta query inbox folder “RLogistics-Quotes” | G2 parse_quote |
| Subscribe webhooks | change notifications | push over poll |
| Teams notify coord | chat message / channel | “Quote ready for RLogistics-1004” |
| Adaptive Cards | Teams | Approve / Reject recommendation (HITL) |

**Config:** app registration, `Mail.Send`, `Mail.Read`, `Chat.ReadWrite`, mailbox `rlogistics-genie@tenant` or application access policy on shared mailbox.

**Local phase:** keep mock outbox; adapter interface:

```text
IEmailTransport
  MockOutboxEmailTransport   # today writes EmailOutbox via Core or direct
  GraphEmailTransport        # Graph send + optional Core audit log
```

Outbound still should **audit** into Core (template code, request id) so coordinators see history regardless of transport.

### 4.5 MCP — server and client

**Why MCP:** one stable tool schema for Cursor agents, RLogisticsGENIE LangGraph tools, and later bank copilots.

#### MCP Server (`rlogistics-genie-mcp`) — *what agents can call*

| Tool | Side effect | Maps to |
|------|-------------|---------|
| `list_requests` | R | GET /api/requests |
| `get_request` | R | GET /api/requests/{id} |
| `create_request` | W + email | POST |
| `update_status` | W + email | PATCH status |
| `assign_request` | W | assign |
| `plan_request` | W | plan |
| `send_vendor_quotes` | W + email | vendor-quotes |
| `send_return_reminder` | W | return-reminder |
| `add_clarification` | W | clarifications |
| `list_outbox` | R | email-outbox |
| `list_vendors` | R | vendors |
| `get_templates` | R | admin templates |
| `rag_search` | R | Qdrant |
| `draft_with_llm` | none (draft only) | LangChain skill |
| `parse_quote` | none | LangChain skill |
| `recommend_vendors` | none | skill + history |
| `graph_send_mail` | W (prod) | Graph (gated) |
| `graph_list_quote_replies` | R | Graph |

**Auth:** MCP server receives service token; injects `X-RLogistics-User-Id` **from session** (never let model invent privileged user without policy).

#### MCP Client (RLogisticsGENIE as client)

RLogisticsGENIE runtime **consumes** MCP server tools rather than re-implementing HTTP — single tool definition for LangGraph `ToolNode`.

Optional second server later: `rlogistics-graph-mcp` isolating Graph scopes.

### 4.6 GenAI product features → graphs (from opportunity map)

| P | Feature | Graph / component |
|---|---------|-------------------|
| P0 | Completeness + clarification draft | G1 |
| P0 | Quote RFQ + email parse + compare | G2 + Graph |
| P0 | Request summary on Process | LangChain skill in UI |
| P1 | Vendor recommend + schedule suggest | G2 end + G3 |
| P1 | Excel status map + exceptions | G4 Worker |
| P2 | CoC OCR | document pipeline + vision model |
| P2 | NL ops reporting | skill + read-only SQL views |
| P2 | Requestor Q&A | G5 |

---

## 5. Implementation phases (build order)

### Phase A — Wire RLogisticsGENIE shell (1–2 weeks lab)

1. Scaffold `RLogisticsGENIE.Api` (FastAPI): health, `/v1/summarize/{requestId}`, `/v1/completeness/{requestId}`
2. OpenAPI client for Core; persona header passthrough
3. Docker Compose: RLogisticsGENIE + Qdrant + Redis
4. Seed SOP chunks into Qdrant from `docs/kb/`
5. Azure OpenAI or local LLM via env (`LLM_API_BASE`)

**Exit:** Process UI/API can call summary + completeness; no writes from RLogisticsGENIE except via Core.

### Phase B — MCP server (parallel or next)

1. Implement MCP tools over Core REST
2. Connect Cursor / Claude to MCP for developer productivity
3. Point LangGraph tools at same MCP

**Exit:** `get_request` + `update_status` callable from MCP client and agent.

### Phase C — LangGraph G1 + G2 drafts (HITL UI)

1. G1: completeness → draft clarification → `POST .../clarifications` only after approve
2. G2 start: `compose_rfq` optional prefill; then Core `vendor-quotes`
3. Mock inbound: paste email body → `parse_quote` → store in Genie working memory table (not Core until accepted)

**Exit:** Coordinator demo path without Graph.

### Phase D — Microsoft Graph mail

1. App reg + Graph `Mail.Send` for RFQ/status (feature flag)
2. Shared mailbox delta for quote replies
3. Persist parsed Quote into Genie DB; surface compare grid in UI
4. Audit: log Graph message id on Core outbox or Genie `MessageLog`

**Exit:** End-to-end quote email without Outlook manual parse (lab tenant).

### Phase E — Teams + remind orchestration

1. Teams Adaptive Card for “Approve vendor recommendation”
2. Worker: call Core `run-return-reminders` on schedule; optional Graph send
3. Status narratives on status change (subscribe Core or event hook)

### Phase F — Excel feed modernization

1. Upload vendor status file → map columns → validate → PATCH statuses
2. Anomaly digest to Teams/outbox

### Phase G — Bank hardening (continuous)

1. Prompt/response logging, redaction, evaluation harness (golden JSON for parse_quote)
2. Move Qdrant → Azure AI Search; LLM → approved Foundry endpoint
3. SOC-style access to serials (mask in prompts; unmask only in Core)

---

## 6. Key data contracts (RLogisticsGENIE-side)

```json
// ParsedQuote (after email parse)
{
  "requestNumber": "RLogistics-1002",
  "vendorName": "SwiftHaul Logistics",
  "vendorType": "Transport",
  "currency": "USD",
  "totalAmount": 1250.00,
  "lineItems": [{ "description": "pallet pickup", "amount": 400 }],
  "etaDays": 3,
  "exceptions": [],
  "confidence": 0.86,
  "sourceMessageId": "graph-msg-..."
}
```

```json
// VendorRecommendation
{
  "transport": [{ "vendorId": 1, "score": 0.91, "reasons": ["..."] }],
  "processing": [{ "vendorId": 3, "score": 0.88, "reasons": ["..."] }],
  "sources": ["similar:RLogistics-0944", "sop:sanitize-laptop"]
}
```

Core stays authoritative; recommendations stay in RLogisticsGENIE until coordinator runs `plan` / status APIs.

---

## 7. Security & guardrails (non-negotiable)

- **HITL** for award, cancel, force status jumps outside happy path  
- **Grounding logs:** every suggestion stores retrieved chunk ids + request snapshot hash  
- **No price hallucination:** quote amounts only from parsed source text  
- **PII:** minimize serials in LLM prompts; prefer counts + types  
- **Tool policy:** MCP denies `graph_send_mail` and `update_status` for non-coord roles  
- **Deterministic rules engine** for hard compliance blocks (asset class → disposition)

---

## 8. Tech stack cheat-sheet

| Concern | Lab default | Bank target |
|---------|-------------|-------------|
| LLM | Azure OpenAI GPT-4.1 / o-series | Approved WFF model endpoint |
| Orchestration | LangGraph | LangGraph or SK agents (same graphs) |
| Chains | LangChain LCEL | same |
| Embeddings | text-embedding-3-small | approved emb model |
| Vector | Qdrant Docker | Azure AI Search |
| Mail/Teams | Graph app-only + mock fallback | Graph enterprise app |
| MCP | Python MCP SDK | same + network isolation |
| Secrets | .env / User secrets | Key Vault |
| Observability | LangSmith optional + Core AuditLog | App Insights + prompt vault |

---

## 9. Immediate next engineering tasks (when you say “build RLogisticsGENIE”)

1. Create solution projects: `RLogisticsGENIE.Api`, `RLogisticsGENIE.Agents`, `RLogisticsGENIE.Mcp.Server`  
2. `docker-compose` Qdrant + Genie  
3. Implement 2 endpoints: summarize + completeness (Core GET only)  
4. MCP: `get_request`, `list_requests`, `send_vendor_quotes`  
5. LangGraph G1 stub with human interrupt  
6. Wire Process page “RLogisticsGENIE assist” panel later  

**Do not** merge RLogisticsGENIE SQL into Core. Keep Core dumb and reliable; RLogisticsGENIE smart and replaceable.

---

## 10. Success metrics (pilot)

| Metric | Baseline (mock) | Target |
|--------|-----------------|--------|
| Time to first clarifying question | manual | &lt; 2 min assisted |
| Quote compare prep | open Outlook manually | structured grid for 80% of replies |
| Incomplete requests reaching coord queue | high | down 30%+ after completeness UX |
| Coordinator override rate | n/a | logged; not zero expected |

---

## Document control

- Aligns to opportunity map priorities P0–P2  
- Assumes Phase 0 Core API (status email, RFQ, return reminders, templates) as tool surface  
- Next review: implement Phase A after user greenlight  

**END**
