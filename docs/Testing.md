# Testing

## .NET (RLogistics)

```powershell
cd d:\Praveen\Projects\RLogistics
dotnet test MdtGenie.slnx
# or
dotnet test tests/RLogistics.Tests/RLogistics.Tests.csproj
```

### What is covered

| Layer | Areas |
|-------|--------|
| **Unit** | PermissionCatalog, options parsing, FluentValidation, JWT create/validate, Builder, Strategies, Mock email/Teams transports, EmailNotificationService fan-out, cache decorator, DistributedCacheService |
| **Integration** | WebApplicationFactory + EF InMemory: auth JWT/API key, requests create→assign→plan→quotes→status, clarifications, notifications/outbox, vendors, admin templates, correlation + security headers |

Tests force `Testing:InMemoryDatabaseName` so **SQL Server is not required**. Redis is disabled. Notifications stay `Mock`.

### Important production hooks for tests

- `Program` is `public partial` for `WebApplicationFactory`
- `DependencyInjection` uses InMemory when `Testing:InMemoryDatabaseName` is set
- `SchemaPatcher` skips non-SQL Server providers

## Python (RLogisticsGENIE)

```powershell
cd d:\Praveen\Projects\RLogistics
.\src\RLogisticsGENIE\.venv\Scripts\python.exe -m pip install -r src/RLogisticsGENIE/requirements.txt
.\src\RLogisticsGENIE\.venv\Scripts\python.exe -m pytest tests/RLogisticsGENIE.Tests -q
```

Covers: skills, chunked RAG, LangGraph intake/quote, shared tools, MCP stdio client↔server.

## Adding tests

- Put isolated logic under `tests/RLogistics.Tests/Unit/`
- Put API flows under `tests/RLogistics.Tests/Integration/` using `RLogisticsWebApplicationFactory`
- Prefer Arrange–Act–Assert; keep Mock transport defaults
