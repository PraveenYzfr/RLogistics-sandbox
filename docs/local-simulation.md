# Local RLogistics Simulation — Phase 0 complete

Personal sandbox that mimics Acme Bank **RLogistics** roles and reverse-logistics flow.  
**Not** the bank production application.

## Run

```powershell
cd d:\Praveen\Projects\RLogistics
dotnet run --project src/RLogistics
```

| Surface | URL |
|---|---|
| Home | http://localhost:5088/ |
| Personas | http://localhost:5088/Persona |
| Swagger | http://localhost:5088/swagger |
| Coordinator dashboard | http://localhost:5088/Coordinator/Dashboard |
| Admin email templates | http://localhost:5088/Admin/Templates |
| Admin notifications (Mail/Teams modes) | http://localhost:5088/Admin/Notifications |
| Admin configuration | http://localhost:5088/Admin/Config |
| Email outbox | http://localhost:5088/Email/Outbox |
| Teams outbox | http://localhost:5088/Teams/Outbox |

**SQL Server:** `LAPTOP-R6U8H616` · database **`RLogistics`** · Windows auth

## Personas

| Email | Role |
|---|---|
| `user@demo.local` | User — create + track own requests + answer clarifications |
| `coord1@demo.local` / `coord2@demo.local` | Coordinator — dashboard, process, assign, plan pickup |
| `admin@demo.local` | Admin — everything coord + templates + config |

API header: `X-RLogistics-User-Id: <id>` (from `GET /api/users`).

## Request types

- US Surplus  
- Point to Point  
- International  
- Request a Box  

## Workflow status

1. Created / New  
2. Assigned  
3. Pickup Scheduled  
4. Picked Up  
5. Delivered  
6. PO Approval (rare)  
7. On Hold (rare)  
(+ Cancelled)

## Features delivered (Phase 0)

### User
- 5-step create wizard: contact → pickup → facility (type) → equipment (**Device GUID** required) → submit  
- My requests + detail view  
- Reply to coordinator queries (On Hold → Assigned)

### Partner / ServiceNow sim
- `POST /api/requests` with full payload (see `src/RLogistics/RLogistics.http`)

### Coordinator
- Dashboard: KPI tiles, my grid, all open grid, **unassigned queue at bottom**  
- Process screen: full request review, notes, query user, vendors, **editable pickup schedule**, workflow status, audit  

### Admin
- Email templates: create / edit / activate  
  - Seeded: `StatusChanged`, `ClarificationSent`, `PickupScheduled`  
- Configuration key/value: create / edit / delete  

### Platform
- SQL Server EF Core, schema patcher for upgrades  
- **Mail/Teams 3-mode:** Mock outbox · Personal Microsoft Graph · Enterprise Graph; Teams MockOutbox · Incoming Webhook · Graph  
- See `docs/Notifications-Mail-Teams.md`  
- Audit log per request  

## RLogisticsGENIE (built as sidecar)

- FastAPI on port 8090 · Core proxy `/api/genie/*`  
- See `docs/RLogisticsGENIE-Runbook.md`  

## Optional live visualization

1. Default Mock: use Email + Teams outbox UIs  
2. Teams channel: set `Notifications:Teams:Provider=IncomingWebhook` + webhook URL  
3. Personal Outlook: set `Mode=PersonalMicrosoft` + Entra ClientId + device login on Admin → Notifications  

Still on hold for later waves: Excel/SSIS feeds, Azure AI Search, CoC OCR polish. RLogisticsGENIE sits **on top of** `RLogistics` HTTP APIs with human-in-the-loop.
