# Observability, spend controls, and AI + SME evaluation

Phase 1 sandbox on **RLogisticsGENIE** (+ Admin UI in **RLogistics**).

## Architecture

| Piece | Location |
|-------|----------|
| Usage / cost | `app/observability.py` |
| Rate + spend | `app/limits.py` |
| AI judge | `app/eval_judge.py` (offline heuristic) |
| Eval cases | `app/eval_store.py` |
| Admin SME UI | `/Admin/AiEval` |

```text
HTTP/MCP → Correlation + RateSpendGuard → tools/RAG/LLM
                ↓                            ↓
           UsageStore (Redis/JSONL)     EvalStore + AI judge
                ↓                            ↓
        /v1/observability/*           /v1/eval/*  ← SME via Admin UI
```

## Usage + cost

Each GenAI-ish call records a **UsageEvent**: operation, provider/model, tokens (estimated), latency, `est_cost_usd`.

Price table (env):

| Setting | Default | Meaning |
|---------|---------|---------|
| `COST_EMBED_PER_1M` | 0.02 | USD / 1M embed tokens |
| `COST_LLM_IN_PER_1M` | 0.15 | USD / 1M LLM input |
| `COST_LLM_OUT_PER_1M` | 0.60 | USD / 1M LLM output |

APIs:

- `GET /v1/observability/summary?day=today`
- `GET /v1/observability/events?limit=100`
- `GET /v1/observability/limits`
- Health → `usage.today_calls`, `usage.today_est_usd`

Persistence: Redis when available; else memory + `data/usage.jsonl`.

## Rate limiting / spend

| Env | Default | Behavior |
|-----|---------|----------|
| `RATE_LIMIT_RPM` | 60 | Per caller / minute → **429** |
| `RATE_LIMIT_EMBEDS_DAY` | 5000 | Embed-ish ops / day |
| `SPEND_LIMIT_USD_DAY` | 5.0 | Estimated spend / day |
| `SPEND_ENABLED` | true | `false` = observe-only (log would-block, allow) |

Caller identity: hashed `X-Api-Key`, or `mcp`, or `anonymous`.

**Phase 2 (not built):** Azure APIM policies, Teams spend alerts, Key Vault.

## AI judge + human SME

1. Auto-capture (default `EVAL_AUTO_CAPTURE=true`) on intake / parse_quote / rag tools → EvalCase + offline AI judge.
2. SME reviews in UI **Admin → AI Eval** or `POST /v1/eval/cases/{id}/sme`.
3. Metrics: `GET /v1/eval/metrics`

| Metric | Meaning |
|--------|---------|
| AI pass rate | `% ai_judge.pass` |
| SME pass rate | `% sme.pass` (reviewed) |
| Agreement | both pass or both fail |
| Mean \|AI−SME\| | calibration gap |
| Cost per SME pass | today’s est. $ / SME passes |

Offline judge rubrics: completeness, safety (no write-bypass language), correctness, tone. Mean ≥ 3.5 and safe → pass.

## Quick demo

```powershell
# GENIE
cd src/RLogisticsGENIE
.\.venv\Scripts\Activate.ps1
uvicorn app.main:app --port 8090

# Create eval + score
curl -X POST http://localhost:8090/v1/eval/cases -H "Content-Type: application/json" -d "{\"skill\":\"clarification_draft\",\"input\":\"missing GUID\",\"output\":\"Please confirm Device GUID at site?\"}"
curl http://localhost:8090/v1/eval/metrics
curl http://localhost:8090/v1/observability/summary?day=today
```

UI: http://localhost:5088/Admin/AiEval (Act as Admin or Coordinator).
