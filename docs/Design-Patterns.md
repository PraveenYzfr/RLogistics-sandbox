# RLogistics Core — Design Patterns

Catalog of patterns used in Phase-0+ hardenes security / architecture. Each section: **problem solved**, **how implemented**, **file locations**, **hints**.

---

## 1. Dependency Injection (DI)

**Problem:** Hard-coded `new` prevents testing, swapping mocks, and layered security.

**How:** Composition root registers interfaces → implementations in `DependencyInjection.AddRLogistics`. Controllers/pages take constructor parameters.

**Where:** `src/RLogistics/DependencyInjection.cs`, `Program.cs`

**Hint:** Register lifetimes intentionally — `Scoped` for DbContext-related; `Singleton` only for stateless strategies/factories.

---

## 2. Adapter

**Problem:** Email delivery can be mock-outbox, personal Graph, or enterprise Graph without rewriting status/quote/reminder logic.

**How:** `IEmailTransport` is the target. `MockOutboxEmailTransport` adapts domain `EmailMessage` → `EmailOutbox`. `GraphMailTransport` adapts to Microsoft Graph. `CompositeEmailTransport` chooses mode from `Notifications:Mode` and always (optionally) audits to the outbox. Same pattern for Teams via `ITeamsNotifier` / `CompositeTeamsNotifier`.

**Where:**  
- `Abstractions/IServices.cs` (`IEmailTransport`)  
- `Abstractions/ITeamsNotifier.cs`  
- `Patterns/Adapter/MockOutboxEmailTransport.cs`  
- `Integrations/Notifications/GraphMailTransport.cs`  
- `Integrations/Notifications/CompositeEmailTransport.cs`  
- `Services/EmailNotificationService.cs`

**Hint:** `docs/Notifications-Mail-Teams.md` for Mock vs Personal vs Webhook setup.

---

## 3. Repository

**Problem:** Scatter EF `Include`s and query details across services, making persistence changes painful.

**How:** `IRequestRepository` / `RequestRepository` encapsulate load-tracked, detail graphs, next number.

**Where:** `Patterns/Repository/RequestRepository.cs`, registered in DI.

**Hint:** Gradually move remaining `db.Requests` access in `RequestService` behind repository methods (incremental).

---

## 4. Builder

**Problem:** Creating a `DisposalRequest` with contact/facility/pickup/assets is multi-step and error-prone in one constructor blob.

**How:** `DisposalRequestBuilder` fluent chain: `WithRequestor` → contact/facility/pickup → `WithAssets` → `Build(number)`. Factory yields a new builder per create.

**Where:** `Patterns/Builder/DisposalRequestBuilder.cs`  
**Used by:** `RequestService.CreateAsync`

**Hint:** Add `WithCoordinatorNotes` etc. on builder rather than expanding Create method bodies.

---

## 5. Decorator

**Problem:** Need cross-cutting logging (or metrics) without polluting domain service methods.

**How:** `LoggingRequestServiceDecorator` implements `IRequestService`, wraps real `RequestService`, logs list/create/status/quotes, then forwards.

**Where:** `Patterns/Decorator/LoggingRequestServiceDecorator.cs`  
**Wiring:** `DependencyInjection.cs` (`IRequestService` factory wraps `RequestService`)

**Hint:** Stack decorators: Cache → Logging → Service (order matters).

---

## 6. Facade

**Problem:** UI/API sometimes needs a short “do the coordinator happy path” without chaining many service calls.

**How:** `RequestWorkflowFacade` exposes claim / status / quotes as a thin facade over `IRequestService`.

**Where:** `Patterns/Facade/RequestWorkflowFacade.cs`

**Hint:** Use from Process page or RLogisticsGENIE tools instead of re-coding assign+status sequences.

---

## 7. Strategy (+ Factory Method for selection)

**Problem:** Auth mode and disposition messaging differ by situation; `if/else` soup gets unmaintainable.

**How:**  
- `IAuthPresentationStrategy` (JWT vs API key how-to) selected by `AuthPresentationStrategyFactory` from config mode.  
- `IDispositionMessageStrategy` for Sanitize vs Destroy headlines via `DispositionMessageResolver`.

**Where:** `Patterns/Strategy/AuthAndDispositionStrategies.cs`  
**Used by:** `AuthController` (`/api/auth/schemes`)

**Hint:** Auth **runtime** selection also uses ASP.NET Core policy scheme as a strategy router (`ForwardDefaultSelector` in `SecurityServiceCollectionExtensions`).

---

## 8. Middleware (Pipeline / Chain of Responsibility style)

**Problem:** Correlation IDs, security headers, exception shaping, and persona hydration must run for every request consistently.

**How:** Ordered pipeline: Exception → Correlation → SecurityHeaders → Auth → Persona → Authorization.

**Where:**  
- `Middleware/SecurityMiddlewares.cs`  
- `Services/PersonaContext.cs` (`PersonaMiddleware`)  
- `DependencyInjection.UseRLogisticsPipeline`

**Hint:** Keep middleware focused; business rules stay in services.

---

## 9. Options pattern (supporting)

**Problem:** Magic strings for JWT/API keys.

**How:** `AuthenticationOptions` bound from `appsettings.json` section `Authentication`.

**Where:** `Security/AuthenticationOptions.cs`, `appsettings.json`

---

## Pattern ↔ SOLID pairing (cheat sheet)

| Pattern | SOLID mostly reinforced |
|---------|-------------------------|
| DI | D |
| Adapter | O, D, S |
| Repository | S, D |
| Builder | S |
| Decorator | O, L |
| Facade | S, I |
| Strategy/Factory | O, D |
| Middleware | S (per hop) |

---

## Dual authentication (architecture note)

Not a classic GoF name, but **selectable Strategy**:

| Mode (`Authentication:Mode`) | Behavior |
|------------------------------|----------|
| `Jwt` | Only Bearer JWT |
| `ApiKey` | Only `X-Api-Key` |
| `JwtAndApiKey` | Prefer API key if header present, else JWT |

Permissions: claims type `permission`, policies named after `RLogisticsPermissions.*` constants. Roles map via `PermissionCatalog`.

Demo password: `Authentication:DemoPassword`  
Demo keys: `Authentication:ApiKeys[]`

---

## 10. Caching decorator (+ Redis)

**Problem:** Hot reads hit SQL every request; multi-instance deploys need shared session/cache.

**How:** `CachingRequestServiceDecorator` wraps `IRequestService` and uses `ICacheService` over `IDistributedCache`. When `Redis:Enabled=true`, Core uses StackExchange Redis; otherwise in-memory. Request detail keys invalidate on writes.

**Where:** `Caching/`, `Patterns/Decorator/CachingRequestServiceDecorator.cs`, `DependencyInjection.cs`



## 10. Caching decorator (+ Redis)

**Problem:** Hot reads hit SQL every time; multi-instance needs shared session/cache.

**How:** CachingRequestServiceDecorator + ICacheService over IDistributedCache. Redis when Redis:Enabled=true, else memory. Invalidates request detail on writes.

**Where:** Caching/, Patterns/Decorator/CachingRequestServiceDecorator.cs

