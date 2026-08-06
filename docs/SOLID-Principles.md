# RLogistics Core — SOLID Principles

This document explains how **SOLID** is applied in `src/RLogistics`, where to look, and small **implementation hints** for future changes (RLogisticsGENIE, Graph mail, etc.).

---

## S — Single Responsibility Principle

**Meaning:** A type has one reason to change.

| Component | Responsibility | Location |
|-----------|----------------|----------|
| `RequestService` | RLogistics request workflow / domain rules | `Services/RequestService.cs` |
| `EmailNotificationService` | Template resolution + recipients | `Services/EmailNotificationService.cs` |
| `MockOutboxEmailTransport` | Persist fake email only | `Patterns/Adapter/` |
| `RequestRepository` | EF loading/saving requests | `Patterns/Repository/` |
| `JwtTokenService` | Create/validate JWT | `Security/JwtTokenService.cs` |
| `ApiKeyAuthenticationHandler` | Authenticate X-Api-Key | `Security/ApiKeyAuthenticationHandler.cs` |
| `ExceptionHandlingMiddleware` | API exception → JSON | `Middleware/SecurityMiddlewares.cs` |
| Validators | Input shape rules only | `Validation/Validators.cs` |

**Hint:** When adding Graph mail, implement a new `IEmailTransport` — do **not** put Graph SDK code inside `EmailNotificationService`.

---

## O — Open/Closed Principle

**Meaning:** Open for extension, closed for modification.

| Extension point | How to extend without changing callers |
|-----------------|----------------------------------------|
| `IEmailTransport` | Add `GraphEmailTransport`; swap in DI |
| `IRequestService` + Decorator | Stack new decorators (metrics, cache) |
| Auth mode | `Authentication:Mode` = Jwt / ApiKey / JwtAndApiKey |
| `IDispositionMessageStrategy` | Add new disposition strategies and register |
| Permission policies | Add claim constants + `PermissionCatalog` entry |

**Hint:** Prefer new classes + DI registration over `if (vendor == X)` sprawl in services.

---

## L — Liskov Substitution Principle

**Meaning:** Implementations of an interface must be substitutable.

| Interface | Implementations | Substitute safely |
|-----------|-----------------|-------------------|
| `IRequestService` | `RequestService`, `LoggingRequestServiceDecorator` | Decorator forwards all members |
| `IEmailTransport` | `MockOutboxEmailTransport` (+ future Graph) | Same `SendAsync` contract |
| `IAuthPresentationStrategy` | JWT / API key presenters | Different text, same shape |

**Hint:** Decorators must not weaken preconditions (e.g. require extra params) or swallow errors silently without rethrow when contract expects throws.

---

## I — Interface Segregation Principle

**Meaning:** Prefer focused interfaces over fat ones.

| Interface | Why small |
|-----------|-----------|
| `IEmailTransport` | Only send messages |
| `IJwtTokenService` | Only tokens |
| `IPermissionService` | Only permission checks |
| `IRequestRepository` | Only persistence |
| `IRequestWorkflowFacade` | Only high-level coordinator actions |
| Controllers | Depend on `IRequestService`, not full `RLogisticsDbContext` (except admin/outbox read) |

**Hint:** Do not grow `IRequestService` with AI methods — put RLogisticsGENIE behind separate ports.

---

## D — Dependency Inversion Principle

**Meaning:** High-level modules depend on abstractions; compositions wire concretes in one place.

| High-level | Abstraction | Concrete wired in |
|------------|-------------|-------------------|
| Controllers | `IRequestService` | `DependencyInjection.cs` + Decorator |
| `RequestService` | `IEmailNotificationService` | `EmailNotificationService` |
| Email service | `IEmailTransport` | `MockOutboxEmailTransport` |
| Facade | `IRequestService` | Decorator chain |
| Auth UI help | `IAuthPresentationStrategyFactory` | Factory |

**Composition root:** `DependencyInjection.AddRLogistics` + `UseRLogisticsPipeline`.

**Hint:** Never `new RequestService()` inside controllers. Always resolve from DI so logging/auth swaps apply.

---

## Related security SOLID-ish practices

- **Permissions as claims** (`RLogisticsPermissions`) separate role mapping (`PermissionCatalog`) from enforcement (`[Authorize(Policy=...)]`).
- **Dual authentication** selectable by config without rewriting controllers.
- **FluentValidation** keeps validation out of controller bodies (SRP).

---

## Quick map for reviewers

```
Controller → IRequestService (Decorator) → RequestService
                → IEmailNotificationService → IEmailTransport (Adapter)
                → IDisposalRequestBuilderFactory (Builder)
Repository abstraction available: IRequestRepository
Facade: IRequestWorkflowFacade
Auth strategies: Jwt / ApiKey (+ Policy scheme factory selector)
```
