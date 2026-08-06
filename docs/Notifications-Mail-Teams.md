# Mail + Teams notifications (Mock / Personal Microsoft / Enterprise Graph)

RLogistics Core can deliver the same domain events three ways for email, and independently for Teams.

## Modes (email)

| Mode | Config `Notifications:Mode` | What you see |
|------|-----------------------------|--------------|
| **Mock** (default) | `Mock` | Rows in **Email Outbox** only. No Graph, no secrets. |
| **PersonalMicrosoft** | `PersonalMicrosoft` | Real Outlook mail via Graph **delegated** auth (device code + local token cache). Audit still written to outbox when `AlwaysAuditToOutbox` is true. |
| **EnterpriseGraph** | `EnterpriseGraph` | App-only Graph (`ClientSecret`) from a service mailbox (`SenderUserId`). |

## Teams providers

| Provider | Config `Notifications:Teams:Provider` | Best for |
|----------|----------------------------------------|----------|
| **MockOutbox** | `MockOutbox` | Local UI at `/Teams/Outbox` |
| **IncomingWebhook** | `IncomingWebhook` | **Live channel posts** with personal/work Teams — easiest real-time viz |
| **Graph** | `Graph` | ChatId or TeamId+ChannelId via Microsoft Graph |

Email status / quotes / reminders also fan out to Teams when `NotifyTeamsOnEmail` is `true`.

---

## 1) Mock only (works out of the box)

```json
"Notifications": {
  "Mode": "Mock",
  "AlwaysAuditToOutbox": true,
  "NotifyTeamsOnEmail": true,
  "Teams": { "Provider": "MockOutbox" }
}
```

1. Run Core → act as coord/admin  
2. Change a request status → open **Email Outbox** and **Teams Outbox**  
3. Admin → **Notifications** → Test email / Test Teams  

API: `GET /api/notifications/status` (API key or JWT).

---

## 2) Personal Microsoft account → real Outlook

### Entra app (once)

1. Azure Portal → **Microsoft Entra ID** → **App registrations** → **New registration**  
2. Name e.g. `RLogistics-Local`, supported accounts: **personal Microsoft accounts** or **Accounts in any org + personal**  
3. Platform: **Mobile and desktop** / public client (or enable **Allow public client flows** = Yes)  
4. **API permissions** → Microsoft Graph **delegated**:  
   - `User.Read`  
   - `Mail.Send`  
   - (optional for Graph Teams) `Chat.ReadWrite`, `ChannelMessage.Send`  
5. Grant admin consent if your tenant requires it (personal accounts usually self-consent).  
6. Copy **Application (client) ID**.

> Consumer MSA mail via Graph is supported for many mail scopes when the app allows personal accounts. If consent fails, use a work/school account in a free Entra developer tenant instead.

### appsettings

```json
"Notifications": {
  "Mode": "PersonalMicrosoft",
  "AlwaysAuditToOutbox": true,
  "Graph": {
    "ClientId": "<your-app-client-id>",
    "TenantId": "common",
    "PersonalTokenCachePath": ".rlogistics-graph-token-cache.bin"
  }
}
```

Put real ClientIds in `appsettings.Development.json` (gitignored) if you prefer not to commit them.

### Sign in once

1. Restart Core  
2. Open **Admin → Notifications** → **Start Graph device login**  
3. Open the verification URL, enter the code, sign in with your Microsoft account  
4. **Send test email** to your own Outlook address → check **Sent** and inbox in outlook.com / Outlook desktop  

Or API:

```http
POST /api/notifications/graph/login
GET  /api/notifications/status
POST /api/notifications/test-email
{ "to": "you@outlook.com" }
```

Token cache file `.rlogistics-graph-token-cache.bin` is local — do not commit.

---

## 3) Teams real-time (Incoming Webhook — recommended locally)

1. Teams (desktop/web) → pick a team/channel you own  
2. **⋯** → **Connectors** / **Manage channel** → **Incoming Webhook** → configure → copy URL  
3. Config:

```json
"Teams": {
  "Provider": "IncomingWebhook",
  "IncomingWebhookUrl": "https://....logic.azure.com/..."
}
```

4. Restart → change a request status or Admin → **Send test Teams**  
5. Watch the channel live  

Audit row still appears in **Teams Outbox**.

---

## 4) Enterprise Graph (corp service mailbox)

```json
"Mode": "EnterpriseGraph",
"Graph": {
  "ClientId": "...",
  "TenantId": "<tenant-guid>",
  "ClientSecret": "<secret>",
  "SenderUserId": "rlogistics-noreply@yourcompany.com"
}
```

App permissions (application, not delegated): `Mail.Send` (and Teams app perms if needed). Admin consent required. Client secret never in shared repo.

---

## Architecture

```
EmailNotificationService
  → IEmailTransport = CompositeEmailTransport
        → MockOutboxEmailTransport (SQL EmailOutbox)
        → GraphMailTransport (when Mode ≠ Mock)
  → ITeamsNotifier = CompositeTeamsNotifier
        → always SQL TeamsOutbox
        → IncomingWebhook HTTP POST  and/or  Graph chat/channel
```

RLogisticsGENIE should keep calling Core for “send reminder / quotes” so delivery stays auditable in outboxes.

---

## API surface

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/notifications/status` | Mode, device code, signed-in UPN |
| POST | `/api/notifications/graph/login` | Admin — start device code |
| POST | `/api/notifications/test-email` | Admin — `{ "to": "..." }` |
| POST | `/api/notifications/test-teams` | Admin |
| GET | `/api/notifications/teams-outbox` | Last 200 rows |
| GET | `/api/email-outbox` | Existing email outbox API |

Auth: same as Core (`JwtAndApiKey` + persona/key with admin permissions for test/login).
