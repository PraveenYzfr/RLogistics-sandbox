# RLogistics + RLogisticsGENIE

**RLogisticsGENIE** — GenAI sidecar for the **RLogistics** reverse-logistics sandbox (personal lab; not a bank production system).

This repo ships a **Phase 0 local RLogistics app** (system-of-record simulation) plus RLogisticsGENIE (skills, RAG, MCP) and architecture docs.

## Quick start

```powershell
cd d:\Praveen\Projects\MDT-GENIE
dotnet run --project src/RLogistics
```

- UI: http://localhost:5088/Persona  
- Swagger: http://localhost:5088/swagger  
- SQL: `RLogistics` on `LAPTOP-R6U8H616`

Guide: [docs/local-simulation.md](docs/local-simulation.md)

### Tests

```powershell
dotnet test tests/RLogistics.Tests/RLogistics.Tests.csproj
# RLogisticsGENIE skills (activate venv first)
python -m pytest tests/RLogisticsGENIE.Tests -q
```

Details: [docs/Testing.md](docs/Testing.md)

### Demo logins (Act as…)

| Persona | Use for |
|---|---|
| user@demo.local | Create disposal requests |
| coord1@demo.local | Dashboard + process workflows |
| admin@demo.local | Templates + configuration |

## Phase 0 included

- Roles: User / Coordinator / Admin  
- Create wizard + partner API  
- Request types + Device GUID + manufacturer/model on equipment  
- Coordinator dashboard + process screen  
- Vendors, pickup schedule, workflow statuses  
- Admin email templates + config  
- Mock email outbox — status change, transport+processing RFQ quotes, device-return reminders  
- Full REST surface (Swagger + `src/RLogistics/RLogistics.http`)

## Architecture docs

| Doc | Description |
|---|---|
| [docs/Embeddings.md](docs/Embeddings.md) | Switchable RAG embeddings (offline vs Azure OpenAI enterprise) |
| [docs/Observability-Eval.md](docs/Observability-Eval.md) | Usage/cost, rate+spend limits, AI judge + SME eval |
| [docs/Agents-LLM-MCP.md](docs/Agents-LLM-MCP.md) | Multi-agent, dual-tier LLM, MCP HTTP, vector backends |
| [docs/Testing.md](docs/Testing.md) | Automated tests (xUnit + pytest) + how to run |
| [docs/CLAUDE-CODE-CONTEXT.md](docs/CLAUDE-CODE-CONTEXT.md) | **AI / Claude Code handoff** — full project context + pasteable system prompt |
| [docs/Notifications-Mail-Teams.md](docs/Notifications-Mail-Teams.md) | Mock / personal Graph / enterprise Graph / Teams webhooks |
| [docs/RLogisticsGENIE-Runbook.md](docs/RLogisticsGENIE-Runbook.md) | RLogisticsGENIE FastAPI sidecar runbook |
| [docs/SOLID-Principles.md](docs/SOLID-Principles.md) | SOLID + where implemented + hints |
| [docs/Design-Patterns.md](docs/Design-Patterns.md) | 8 patterns (DI, Adapter, Repository, Builder, Decorator, Facade, Strategy, Middleware) |
| [docs/RLogisticsGENIE-Architecture-Plan.md](docs/RLogisticsGENIE-Architecture-Plan.md) | GenAI build plan |
| [docs/RLogistics-GenAI-Opportunity-Map-v1.txt](docs/RLogistics-GenAI-Opportunity-Map-v1.txt) | Opportunity map |

## API security

Switch in `appsettings.json` → `Authentication:Mode`:

- `Jwt` — Bearer only  
- `ApiKey` — `X-Api-Key` only  
- `JwtAndApiKey` — either (default)

Lab password: `Demo@RLogistics2026!` · sample keys under `Authentication:ApiKeys`.  
Permissions: fine-grained policies (`rlogistics.requests.*`, `rlogistics.admin.*`).  
Login: `POST /api/auth/token` · schemes: `GET /api/auth/schemes`.


## Layout

```text
src/RLogistics/     # .NET 10: Web API + Razor UI + EF + SQL Server
docs/             # product + RLogisticsGENIE architecture
```

## Next: RLogisticsGENIE + Redis + Docker (D:)

Docker Desktop is installed on **`D:\Docker`**. See [docs/Docker-Desktop-D-Drive.md](docs/Docker-Desktop-D-Drive.md).

```powershell
# Infra (Redis 6379 + Qdrant 6333)
.\scripts\start-infra.ps1

# Core (Redis:Enabled=true)
dotnet run --project src/RLogistics --urls http://localhost:5088

# RLogisticsGENIE
cd src/RLogisticsGENIE
.\.venv\Scripts\Activate.ps1
$env:RLOGISTICS_URL="http://localhost:5088"
$env:RLOGISTICS_API_KEY="rlogistics-demo-coord-key-change-me"
$env:REDIS_URL="redis://localhost:6379/0"
$env:QDRANT_URL="http://localhost:6333"
uvicorn app.main:app --port 8090
```

Full guide: [docs/RLogisticsGENIE-Runbook.md](docs/RLogisticsGENIE-Runbook.md)  
Architecture: [docs/RLogisticsGENIE-Architecture-Plan.md](docs/RLogisticsGENIE-Architecture-Plan.md)


