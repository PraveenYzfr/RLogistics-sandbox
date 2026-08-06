# RLogistics — Claude Code / AI Agent Handoff Context

> **How to use this file**  
> Paste the block under **[SYSTEM PROMPT FOR CLAUDE CODE]** at the start of a Claude Code session, or say:  
> `Read docs/CLAUDE-CODE-CONTEXT.md and follow it for all work in this repo.`  
> Keep this file updated when architecture/config conventions change.

---

## [SYSTEM PROMPT FOR CLAUDE CODE]

```
You are working in the RLogistics local sandbox repo (NOT Acme Bank production RLogistics).

Product:
- RLogistics (.NET ASP.NET Core, Razor Pages + REST) — reverse logistics "reverse logistics sandbox" simulation
- RLogisticsGENIE (Python FastAPI sidecar) — GenAI assist; NEVER owns SQL; only calls Core APIs

Hard rules:
1. Do not treat this as bank production code. Demo personas and lab secrets only.
2. Prefer minimal, focused diffs. Match existing patterns (DI in DependencyInjection.cs, adapters, facades).
3. RLogisticsGENIE must not write SQL or own RLogistics domain DB — call Core HTTP APIs with API keys.
4. All outbound email/Teams must go through IEmailTransport / ITeamsNotifier / EmailNotificationService so outboxes stay auditable.
5. Schema changes for local upgrades go through Data/SchemaPatcher.cs (EnsureCreated does not alter columns).
6. Do not commit secrets (ClientSecret, real webhook URLs, Graph token cache, .env). Prefer appsettings.Development.json or user-secrets.
7. Do not run destructive git (force push, hard reset) unless the user explicitly asks.
8. Only commit when the user explicitly asks.
9. Default Notifications:Mode is Mock — mock must keep working with empty Graph ClientId.
10. Personal Graph device login should request mail scopes only for Outlook; Teams Graph scopes only when Teams:Provider=Graph.

Key URLs (local):
- Core: http://localhost:5088
- Swagger: http://localhost:5088/swagger
- RLogisticsGENIE: http://localhost:8090
- Redis: localhost:6379 (optional Docker)
- Qdrant: localhost:6333 (optional Docker)

Read before large changes:
- docs/CLAUDE-CODE-CONTEXT.md (this file’s body)
- docs/Notifications-Mail-Teams.md
- docs/RLogisticsGENIE-Runbook.md
- docs/Design-Patterns.md
- docs/local-simulation.md

Run Core:  dotnet run --project src/RLogistics
Run RLogisticsGENIE: cd src/RLogisticsGENIE && (venv) uvicorn app.main:app --port 8090 --reload
Infra:     scripts/start-infra.ps1 or docker compose -f infra/docker-compose.yml up -d

When modifying notifications, preserve Composite* transports and AlwaysAuditToOutbox behavior.
When modifying requests/API, keep auth policies (RLogisticsPermissions) and FluentValidation.

Tests:
- After code changes run: `dotnet test tests/RLogistics.Tests/RLogistics.Tests.csproj`
- RLogisticsGENIE skills: `python -m pytest tests/RLogisticsGENIE.Tests -q`
- See docs/Testing.md. Prefer adding tests when fixing bugs (red-green).
```

---

## 1. What this project is

| | |
|--|--|
| **Name** | RLogistics |
| **Path (typical)** | `d:\Praveen\Projects\RLogistics` (Windows) |
| **Purpose** | Personal lab sandbox that mimics Acme Bank–style **RLogistics** reverse logistics workflows + **RLogisticsGENIE** GenAI sidecar |
| **Not** | Production bank systems, real WF network, production Exchange |

### Personas (seeded)

| Email | Role | Use |
|-------|------|-----|
| `user@demo.local` | User / Requestor | Create wizard, my requests, reply clarifications |
| `coord1@demo.local`, `coord2@demo.local` | Coordinator | Dashboard, process, assign, plan, quotes |
| `admin@demo.local` | Admin | Templates, config, notifications setup |

UI persona: cookie / page `/Persona`.  
API persona: `X-RLogistics-User-Id` and/or JWT / API key (enterprise dual auth).

Demo JWT password: `Demo@RLogistics2026!`  
API keys (lab): `rlogistics-demo-user-key-change-me`, `rlogistics-demo-coord-key-change-me`, `rlogistics-demo-admin-key-change-me`

---

## 2. Stack & run

### RLogistics

- **TFM:** net10.0 (see `src/RLogistics/RLogistics.csproj`)
- **Web:** Razor Pages + Controllers + Swagger
- **ORM:** EF Core SQL Server (`RLogisticsDbContext`)
- **DI root:** `DependencyInjection.cs` → `AddRLogistics`
- **DB:** `Server=LAPTOP-R6U8H616;Database=RLogisticsCore;Integrated Security=True;...` (connection string in appsettings — may need change on other machines)
- **Port:** 5088 (see `Properties/launchSettings.json`)

```powershell
cd d:\Praveen\Projects\RLogistics
dotnet run --project src/RLogistics
```

Samples: `src/RLogistics/RLogistics.http`

### RLogisticsGENIE

- Python FastAPI, port **8090**
- Calls Core with API key; no direct SQL
- Offline LLM mode by default; optional real LLM later
- **Proper RAG:** chunked `kb/*.md` + fastembed (preferred) / TF-IDF fallback → Qdrant or memory (`app/rag.py`)
- **LangGraph:** real `StateGraph.invoke` for intake + quote (`app/graphs.py`)
- **MCP server:** official SDK stdio — `python -m app.mcp_stdio` (Cursor: `.cursor/mcp.json.example`)
- **MCP client:** `app/mcp_client.py` for agents/tests; HTTP `/v1/tools` shares `app/tools.py`
- Core proxy: `/api/genie/*`

See `docs/RLogisticsGENIE-Runbook.md`.

### Infra (optional Docker Desktop on D:)

- Compose: `infra/docker-compose.yml` → Redis `6379`, Qdrant `6333`
- Notes: `docs/Docker-Desktop-D-Drive.md`
- Scripts: `scripts/start-infra.ps1`, `scripts/install-docker-desktop-d.ps1`

If Redis is down and `Redis:Enabled` is true, Core may fail distributed cache — set `"Redis:Enabled": false` for pure offline runs.

---

## 3. Architecture (mental model)

```
Browser / Postman / RLogisticsGENIE
         │
         ▼
   ┌─────────────┐     JWT / API Key / X-RLogistics-User-Id
   │  RLogistics   │
   │  REST + UI  │
   └──────┬──────┘
          │
          ├── RequestService (+ cache + logging decorators)
          ├── EmailNotificationService
          │       ├── IEmailTransport → CompositeEmailTransport
          │       │       ├── MockOutboxEmailTransport (SQL EmailOutbox)
          │       │       └── GraphMailTransport (Personal / Enterprise)
          │       └── ITeamsNotifier → CompositeTeamsNotifier
          │               ├── Mock SQL TeamsOutbox (always audit)
          │               ├── IncomingWebhook (real-time channel)
          │               └── Graph chat / team+channel
          └── GenieClient → http://localhost:8090

   RLogisticsGENIE ──HTTP──► Core APIs only (no SQL)
```

### Design patterns (already used)

| Pattern | Where |
|---------|--------|
| Adapter | `MockOutboxEmailTransport`, Graph mail, Teams composite |
| Repository | `RequestRepository` |
| Builder | `DisposalRequestBuilder` |
| Decorator | Logging + Caching on `IRequestService` |
| Facade | `RequestWorkflowFacade` |
| Strategy | Auth presentation, disposition messages |
| Middleware | Correlation id, security headers, API exceptions |

Docs: `docs/Design-Patterns.md`, `docs/SOLID-Principles.md`

---

## 4. Domain (RLogistics Core)

### Request types
US Surplus, Point to Point, International, Request a Box

### Workflow statuses (approx)
Created → Assigned → PickupScheduled → PickedUp → Delivered  
Also: OnHold, PO Approval (rare), Cancelled

### Assets (required fields for create)
Type, **Manufacturer**, **Model**, Device GUID, quantity, optional serial/tag/condition

### Important entities
`DisposalRequest`, `AssetLine`, `Vendor` (Transport/Processing), `Clarification`, `EmailTemplate`, `EmailOutbox`, `TeamsOutbox`, `AppConfig`, `AuditLog`, `AppUser`

### Schema evolution
`Data/SchemaPatcher.cs` runs SQL `IF COL_LENGTH` / `IF OBJECT_ID` patches after startup.  
**Always add patches here** when introducing new columns/tables for existing local DBs.

---

## 5. Auth & security

Config section: `Authentication`

| Mode | Behavior |
|------|----------|
| `Jwt` | Bearer only |
| `ApiKey` | `X-Api-Key` only |
| `JwtAndApiKey` | **default** — either works |

- Token: `POST /api/auth/token` with email + demo password  
- Whoami: `GET /api/auth/me`  
- Permissions: claims `permission` / `RLogisticsPermissions.*` policies  
- Lab only: do not deploy demo keys/password to production

Related: `Security/*`, `Middleware/SecurityMiddlewares.cs`, FluentValidation on key DTOs

---

## 6. Notifications (Mail + Teams) — critical for mods

**Status:** Implemented 3-mode email + multi-provider Teams. Default = full Mock (no Entra required).

### Config (`Notifications` in appsettings.json)

```json
"Notifications": {
  "Mode": "Mock",                 // Mock | PersonalMicrosoft | EnterpriseGraph
  "AlwaysAuditToOutbox": true,
  "NotifyTeamsOnEmail": true,
  "Graph": {
    "ClientId": "",
    "TenantId": "common",
    "ClientSecret": "",
    "SenderUserId": "me",
    "PersonalTokenCachePath": ".rlogistics-graph-token-cache.bin"
  },
  "Teams": {
    "Provider": "MockOutbox",     // MockOutbox | IncomingWebhook | Graph
    "IncomingWebhookUrl": "",
    "TeamId": "",
    "ChannelId": "",
    "ChatId": ""
  }
}
```

### Email modes

| Mode | Transport | Needs |
|------|-----------|--------|
| `Mock` | SQL EmailOutbox only | Nothing |
| `PersonalMicrosoft` | MSAL device-code + Graph `me/sendMail` | Public Entra app ClientId, personal account login |
| `EnterpriseGraph` | Client secret + `users/{id}/sendMail` | Confidential app, secret, SenderUserId |

Mail scopes for personal login (best chance): `User.Read`, `Mail.Send` only.  
Implementation: `PersonalGraphTokenStore` — mail-only unless Teams Graph needs tokens.

### Teams providers

| Provider | Behavior |
|----------|----------|
| `MockOutbox` | SQL + UI `/Teams/Outbox` |
| `IncomingWebhook` | Real-time post to channel (best local live viz; **no Entra required**) |
| `Graph` | ChatId **or** TeamId+ChannelId via Graph |

`NotifyTeamsOnEmail`: status / quotes / clarification / return-reminder also call `ITeamsNotifier`.

### Key files

```
Integrations/Notifications/
  NotificationOptions.cs
  CompositeEmailTransport.cs
  GraphMailTransport.cs          # + PersonalGraphTokenStore
  CompositeTeamsNotifier.cs
Patterns/Adapter/MockOutboxEmailTransport.cs
Services/EmailNotificationService.cs
Controllers/NotificationsController.cs
Pages/Admin/Notifications.cshtml(.cs)
Pages/Email/Outbox.cshtml(.cs)
Pages/Teams/Outbox.cshtml(.cs)
Domain/TeamsOutbox.cs
```

### API

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/notifications/status` | Mode, device code, UPN |
| POST | `/api/notifications/graph/login` | Admin — device code |
| POST | `/api/notifications/test-email` | Admin — `{ "to": "..." }` |
| POST | `/api/notifications/test-teams` | Admin |
| GET | `/api/notifications/teams-outbox` | Audit list |
| GET | `/api/email-outbox` | Email audit |

Doc: `docs/Notifications-Mail-Teams.md`

### Local real-time without hosting app

App can stay on localhost. Outbound HTTPS to Graph/Webhook only.

- **Teams live:** Incoming Webhook URL + `Provider=IncomingWebhook`
- **Mail live:** Entra public client + `PersonalMicrosoft` + device login  
- Does **not** require Azure hosting/App Service  
- Does require Microsoft identity ability to create **App registration** (for Graph mail) and/or Teams webhook capability

**Never commit:** real ClientSecret, webhook URLs, `.rlogistics-graph-token-cache.bin`

---

## 7. Feature surface (built)

### UI pages (Razor)

| Path | Purpose |
|------|---------|
| `/Persona` | Act as demo user |
| `/Requests/Create` | Multi-step create wizard |
| `/Requests/My`, Detail | Requestor view |
| `/Coordinator/Dashboard` | KPIs, grids, unassigned queue |
| `/Coordinator/Process` | Full process + RLogisticsGENIE assist panel |
| `/Admin/Templates` | Email template CRUD |
| `/Admin/Config` | AppConfig key/value |
| `/Admin/Notifications` | Graph mode/status, tests, device login |
| `/Email/Outbox` | Mock/audit emails |
| `/Teams/Outbox` | Mock/audit Teams |

### Core APIs (selection)

- CRUD-ish requests: list, get, create, assign, status, fields, plan, clarifications, reply  
- Vendor quotes, return reminders, run overdue reminders  
- Vendors list, email outbox, admin templates/config  
- Auth + RLogisticsGENIE proxy + notifications  

Full samples: `RLogistics.http`

### Caching

- `ICacheService` / Redis or memory  
- `CachingRequestServiceDecorator` (request detail, vendors)

---

## 8. RLogisticsGENIE (Python)

| Path | Role |
|------|------|
| `src/RLogisticsGENIE/app/main.py` | FastAPI entry |
| `app/core_client.py` | Calls Core APIs |
| `app/skills.py` | Completeness, summary, drafts, vendor recommend, quote parse |
| `app/graphs.py` | **LangGraph** intake + quote (`invoke`) |
| `app/rag.py` | Chunked RAG (fastembed / TF-IDF / hash) |
| `app/tools.py` | Shared tool implementations |
| `app/mcp_stdio.py` | Official MCP stdio server |
| `app/mcp_client.py` | In-repo MCP client |
| `kb/*.md` | Policy/SOP snippets |

**Rule:** Any “send email / update request” skill must call Core endpoints, not invent a second channel.

---

## 9. On hold / not priority

Do not build these unless user asks:

- Excel / SSIS vendor status feeds  
- CoC OCR polish  
- **Azure AI Search** (top of deferred)  
- ServiceNow polish  
- Production Azure hosting for Core

Preferred next work is still **local-capable**: Graph/Teams viz, optional real LLM wiring with offline fallback.

---

## 10. Conventions for Claude Code changes

1. **Composition:** Register new services in `DependencyInjection.cs`.  
2. **Domain writes:** Prefer services/repositories over fat page models.  
3. **API + UI:** Keep REST parity when adding request operations (`ApiControllers` + pages + http samples).  
4. **Emails:** Templates in DB + tokens in `EmailNotificationService.ApplyTokens`; transport layer never reimplements business fan-out.  
5. **Config:** New knobs under typed options + appsettings section; document in `docs/`.  
6. **Build:** `dotnet build src/RLogistics` — if file locked by running `RLogistics`, stop process then rebuild.  
7. **NU1605 / package versions:** Align Microsoft.Identity.Client with Extensions.Msal; Azure.Identity carefully.  
8. **UI style:** Existing simple CSS in `wwwroot/css/site.css`; no redesign unless asked.  
9. **Comments:** Only when non-obvious; no narrating noise.  
10. **Scope:** Don’t drive-by refactor unrelated modules.

---

## 11. Common modification recipes

### A) Add a new email-triggering event
1. Template seed/code in `DbSeeder` or admin UI.  
2. Method on `IEmailNotificationService` / implementation.  
3. Call from `RequestService` or facade.  
4. Optional `MaybeTeamsAsync` (already pattern for status/quotes/etc.).  
5. Verify both Mock outbox and that Composite still respects Mode.

### B) Change Graph scopes / login
- `NotificationOptions.Graph.MailScopes` / `TeamsScopes`  
- `PersonalGraphTokenStore` in `GraphMailTransport.cs`  
- Prefer mail-only for personal Outlook success.

### C) New DB column
1. Entity + `RLogisticsDbContext`  
2. **SchemaPatcher** SQL  
3. DTOs / validators / UI  
4. Seeder defaults if needed  

### D) New RLogisticsGENIE skill
1. Skill function in `skills.py`  
2. Wire route or tool  
3. Core proxy if UI needs it  
4. Use `core_client` only for state changes  

### E) Toggle Redis offline
`"Redis": { "Enabled": false }` → memory cache fallback registration already handled in DI.

---

## 12. Important file index

```
src/RLogistics/
  Program.cs
  DependencyInjection.cs
  appsettings.json
  Controllers/ApiControllers.cs
  Controllers/NotificationsController.cs
  Controllers/AuthController.cs
  Controllers/GenieProxyController.cs
  Services/RequestService.cs
  Services/EmailNotificationService.cs
  Data/RLogisticsDbContext.cs, SchemaPatcher.cs, DbSeeder.cs
  Integrations/Notifications/*
  Patterns/*
  Security/*
  Domain/*
  Pages/**

src/RLogisticsGENIE/
  app/*

infra/docker-compose.yml
docs/*
```

---

## 13. Smoke checks (after changes)

```powershell
# Build
dotnet build src/RLogistics

# Run Core, then:
# GET  http://localhost:5088/api/auth/schemes
# POST /api/auth/token { "email":"admin@demo.local", "password":"Demo@RLogistics2026!" }
# GET  /api/notifications/status   (X-Api-Key: rlogistics-demo-admin-key-change-me)
# POST /api/notifications/test-teams
# POST /api/notifications/test-email  { "to":"user@demo.local" }
# UI: /Email/Outbox, /Teams/Outbox
```

Mock path must work with empty `Graph:ClientId`.

---

## 14. Known caveats

- Machine-specific SQL connection string.  
- Redis Enabled=true assumes Docker Redis running.  
- Personal MSA Graph mail: requires correct Entra public client; Teams scopes on login used to break personal mail (fixed: mail-only for mail path).  
- Microsoft.Kiota security advisory via Microsoft.Graph package — warning only unless treated as error.  
- `.demo.local` addresses are **not** real mailboxes — use real addresses when testing Graph.  
- Token cache file in project dir: gitignored; do not commit.

---

## 15. Document map

| Doc | Content |
|-----|---------|
| `docs/CLAUDE-CODE-CONTEXT.md` | **This handoff — start here for AI** |
| `docs/local-simulation.md` | Run, personas, features |
| `docs/Notifications-Mail-Teams.md` | Graph/Teams setup |
| `docs/RLogisticsGENIE-Runbook.md` | RLogisticsGENIE run & skills |
| `docs/RLogisticsGENIE-Architecture-Plan.md` | Architecture planning |
| `docs/Design-Patterns.md` / `SOLID-Principles.md` | Patterns usage in repo |
| `docs/Docker-Desktop-D-Drive.md` | Docker installed on D: |
| `docs/RLogistics-GenAI-Opportunity-Map-v1.txt` | Opportunity catalog (business) |
| `docs/as-is-flow.md` / `genai-opportunities.md` | Domain/process notes |

---

## 16. Pasteable short task templates

### Task: fix / extend notifications
```
Read docs/CLAUDE-CODE-CONTEXT.md §6 and docs/Notifications-Mail-Teams.md.
Follow Composite* transport design. Keep Mock working with empty ClientId.
Change: <describe>.
Build with dotnet build src/RLogistics and summarize files touched.
```

### Task: extend request workflow / API
```
Read docs/CLAUDE-CODE-CONTEXT.md. Keep auth policies, SchemaPatcher if DB changes,
API + Razor + RLogistics.http parity. Change: <describe>.
```

### Task: RLogisticsGENIE skill
```
RLogisticsGENIE must only call Core HTTP (core_client). No SQL. Offline-safe defaults.
Change: <describe>. Document any new env vars in RLogisticsGENIE-Runbook.md.
```

---

*Last updated: 2026-08 (MCP SDK stdio + client; chunked RAG; LangGraph invoke; notifications; tests)*
