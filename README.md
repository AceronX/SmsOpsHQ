# SmsOps HQ

SMS operations management system for pawn shops. Handles customer messaging, payment reminders, conversation tracking, and multi-store communication -- built with ASP.NET Core 8, WPF, and SQLite.

---

## Prerequisites

- **.NET 8 SDK** -- download from https://dotnet.microsoft.com/download/dotnet/8.0 (verify: `dotnet --version` shows 8.0.x)
- **Visual Studio 2022** with the **.NET desktop development** and **ASP.NET and web development** workloads (Community edition is fine). Alternatively, VS Code with C# Dev Kit works for everything except the WPF visual designer.
- **Twilio account** -- needed only for production SMS sending, not for development or tests.

SQLite is bundled via NuGet. No separate database install required.

---

## Project Structure

```
SmsOpsHQ/
  SmsOpsHQ.Core/            Domain entities, DTOs, interfaces, utilities
  SmsOpsHQ.Infrastructure/  EF Core persistence, repositories, services (Twilio, sync, auth)
  SmsOpsHQ.Api/             ASP.NET Core Web API -- JWT auth, SignalR, Swagger
  SmsOpsHQ.Desktop/         WPF desktop client (MVVM, CommunityToolkit.Mvvm)
  SmsOpsHQ.Tests/           xUnit test suite (445 tests)
```

**Core** defines the contracts. **Infrastructure** implements them. **Api** exposes HTTP endpoints. **Desktop** is the user-facing client. **Tests** covers everything.

---

## Quick Start

### 1. Build

```powershell
cd SmsOpsHQ
dotnet build SmsOpsHQ.sln
```

### 2. Run the API

```powershell
dotnet run --project SmsOpsHQ.Api
```

The API starts at `http://localhost:5000`. The database file (`smsops.db`) is created automatically on first run with seed data.

Open `http://localhost:5000/swagger` for interactive API docs.

### 3. Run the Desktop Client

```powershell
dotnet run --project SmsOpsHQ.Desktop
```

Log in with the default credentials below. The client connects to the API on localhost:5000.

### Default Login

| Field    | Value      |
|----------|------------|
| Username | `admin`    |
| Password | `password` |
| Role     | HQAdmin    |

Change these immediately in any non-development environment.

---

## Testing

The test suite has **445 tests** organized into three categories:

### Run All Tests

```powershell
dotnet test SmsOpsHQ.sln
```

Or with more detail:

```powershell
dotnet test SmsOpsHQ.sln --verbosity normal
```

### Run a Specific Test Class

```powershell
dotnet test SmsOpsHQ.sln --filter "FullyQualifiedName~MessagesIntegrationTests"
```

### Run Only Unit Tests (fast, no server)

```powershell
dotnet test SmsOpsHQ.sln --filter "FullyQualifiedName~Tests&FullyQualifiedName!~Integration&FullyQualifiedName!~Performance"
```

### Run Only Integration Tests

```powershell
dotnet test SmsOpsHQ.sln --filter "FullyQualifiedName~IntegrationTests"
```

### Test Categories

**Unit tests** -- test individual functions in isolation, no HTTP server involved:

| Test File | What It Covers |
|-----------|----------------|
| `PhoneUtilsTests` | Phone number formatting and validation |
| `MessageClassifierTests` | Message categorization logic |
| `PawnCalculatorTests` | Pawn payment calculations |
| `PasswordValidatorTests` | Password complexity rules |
| `IdentityResolverTests` | Customer-to-thread identity matching |
| `StorePhoneResolverTests` | Store phone number routing |
| `FeatureFlagsTests` | Feature flag configuration |
| `XpdConcurrencyLimiterTests` | XPD sync rate limiting |
| `AuthServiceTests` | JWT generation and login logic |
| `TwilioServiceTests` | SMS sending logic (mocked) |
| `RealtimeServiceTests` | SignalR broadcast logic |
| `QuarantineServiceTests` | Message quarantine logic |
| `ReminderServiceTests` | Reminder scheduling logic |
| `PhoneValidationServiceTests` | Phone validation rules |
| `*RepositoryTests` (6 files) | Database CRUD for each entity |

**Integration tests** -- spin up the full API via `WebApplicationFactory` with an isolated SQLite database, make real HTTP requests:

| Test File | What It Covers |
|-----------|----------------|
| `MessagesIntegrationTests` | GET/POST messages, categories, auth |
| `ThreadsIntegrationTests` | Inbox, thread detail, bulk ops, filters |
| `CustomersIntegrationTests` | Customer search, context, late/PFX |
| `TemplatesIntegrationTests` | Full CRUD lifecycle |
| `StoresIntegrationTests` | Twilio number management |
| `RemindersIntegrationTests` | Scheduler start/stop, exclusions |
| `QuarantineSyncIntegrationTests` | Quarantine, sync, store isolation |
| `SecurityIntegrationTests` | Swagger, auth enforcement, login security |
| `ProblemDetailsIntegrationTests` | RFC 7807 error responses |

**Performance tests** -- measure response times against budgets:

| Test File | What It Covers |
|-----------|----------------|
| `PerformanceTests` | Inbox load (<1s), thread load (<200ms), message counts (<100ms), customer search (<100ms) |

### How Integration Tests Work

Integration tests use `WebApplicationFactory<Program>` to host the API in-process. Each test fixture creates its own temporary SQLite database so tests are fully isolated. The `IntegrationTestBase` class handles:

- Creating an authenticated HTTP client (JWT token cached per fixture)
- Helper methods: `GetJsonAsync`, `PostJsonAsync`, `GetAsync`, `PostAsync`, `PutAsync`, `DeleteAsync`
- `ClearAuth()` for testing unauthenticated access
- `AuthenticateAsUserAsync(username, password)` for testing different roles

To write a new integration test:

```csharp
[Collection("Integration")]
public class MyNewTests : IntegrationTestBase
{
    public MyNewTests(IntegrationTestFixture factory) : base(factory) { }

    [Fact]
    public async Task MyEndpoint_ReturnsOk()
    {
        HttpResponseMessage response = await GetAsync("/api/my-endpoint?store_id=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MyEndpoint_RequiresAuth()
    {
        ClearAuth();
        HttpResponseMessage response = await GetAsync("/api/my-endpoint?store_id=1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Add `[Collection("Integration")]` to share the test fixture and serialize execution.

---

## Configuration

All settings are in `SmsOpsHQ.Api/appsettings.json`.

### Core Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `ConnectionStrings:DefaultConnection` | SQLite database path | `Data Source=smsops.db` |
| `Jwt:Secret` | JWT signing key (min 32 chars) | Dev key -- change in production |
| `Jwt:Issuer` | Token issuer | `SmsOpsHQ` |
| `Jwt:Audience` | Token audience | `SmsOpsHQ` |
| `Jwt:ExpiresInMinutes` | Token lifetime | `60` |
| `Cors:AllowedOrigins` | Allowed origins (production) | localhost only |
| `Hub:Enabled` | Report heartbeats to SmsOpsHQ.Hub | `false` |
| `Hub:Url` | Hub base URL | — |
| `Hub:StoreKey` | Per-store API key from Hub | — |
| `Hub:DeploymentId` | Store deployment ID from Hub | — |

Runtime overlays in `%AppData%\SmsOpsHQ\`: `twilio_config.json`, `xpd_config.json`, `hub_config.json` (merged at startup; restart API after editing).

### Environment Variables

| Variable | Description |
|----------|-------------|
| `SMSOPSHQ_JWT_SECRET` | Overrides `Jwt:Secret`. Use this in production so the secret never appears in config files. |

### Twilio (production only)

Add to `appsettings.json`:

```json
{
  "Twilio": {
    "AccountSid": "ACxxxxx",
    "AuthToken": "your-auth-token"
  }
}
```

Credentials can also be stored in `%AppData%\SmsOpsHQ\twilio_config.json` (merged at API startup when `appsettings` fields are empty).

**Outbound** SMS goes directly from this store API to Twilio — no public URL required on the store PC.

**Inbound** SMS in production is delivered through **SmsOpsHQ.Hub**, which exposes a single public webhook URL for the entire deployment. Each store reports its Twilio numbers to the Hub via the heartbeat, the Hub looks up the owning store from the `To` number, and forwards the payload to this store over the existing SignalR agent channel. As a result, **store PCs no longer need ngrok or a per-store public URL in production**.

Twilio webhook configuration (set once, on your Twilio Messaging Service, against the Hub's public URL — not the store):

| Callback | URL |
|----------|-----|
| Inbound SMS | `https://<your-hub-host>/twilio-sms` |
| Status updates | `https://<your-hub-host>/twilio-sms/status` |

The local store endpoints at `POST /twilio-sms` and `POST /twilio-sms/status` remain available for development, manual testing, and emergency fallback if the Hub is unreachable. In production they should not be publicly exposed.

See [`docs/CENTRAL_TWILIO_WEBHOOK_REQUIREMENTS.md`](../docs/CENTRAL_TWILIO_WEBHOOK_REQUIREMENTS.md) for the full architecture, [`docs/TWILIO_CUTOVER.md`](../docs/TWILIO_CUTOVER.md) for the one-time switch from per-store ngrok to the central Hub URL, and [`docs/TWILIO_E2E_TEST_SCRIPT.md`](../docs/TWILIO_E2E_TEST_SCRIPT.md) for end-to-end verification.

> **Trade-off:** because inbound SMS now flows through the Hub, the Hub server and its public tunnel must be available **24/7** for incoming messages. **Outbound** SMS is unaffected by Hub downtime. If the store agent is offline when an inbound webhook arrives, the Hub queues it and replays automatically when the agent reconnects.

---

## API Overview

Swagger docs are at `/swagger` when the API is running.

All endpoints require JWT Bearer auth except login, health, and root.

**Auth:** `POST /api/auth/login` -- returns `{ accessToken, user }`. Rate limited to 5 attempts/minute per IP.

**Messaging:** `GET /api/inbox`, `GET /api/thread/{id}`, `POST /api/send`, `GET /api/messages`, `GET /api/messages/counts`

**Customers:** `GET /api/customers/search`, `GET /api/customer/{id}/context`, `GET /api/customers/late`, `GET /api/customers/pfx`

**Templates:** `GET /api/templates`, `POST /api/templates`, `PUT /api/templates/{id}`, `DELETE /api/templates/{id}`

**Stores:** `GET /api/stores/{id}/numbers`, `POST /api/stores/{id}/numbers`

**Reminders:** `GET /api/reminders/scheduler/status`, `POST /api/reminders/scheduler/start`, `POST /api/reminders/scheduler/stop`

**Quarantine:** `GET /api/quarantine/list`, `POST /api/quarantine/{id}/resolve`

**Sync:** `POST /api/sync/full`, `GET /api/sync/status`, `GET /api/sync/progress`

**XPD data model (summary):** Customers, Tickets, Items, and PawnPayments are synced from the pawn POS (XPD). CustomerPhones is an application-built index from customer phone fields for fast identity matching. Messaging tables (Threads, Messages, Templates) are owned by SmsOpsHQ.

**Health:** `GET /health`

**Real-time:** SignalR hub at `/hubs/smsops` -- pushes new messages, status updates, and thread changes to connected clients.

**Reviews:** Review channels, review requests, and optional automation for new XPD tickets.

**HQ Hub (required for inbound SMS in production):** When `Hub:Enabled` is true, the API reports heartbeats (now including the store's Twilio numbers) and maintains a SignalR agent connection to [SmsOpsHQ.Hub](../SmsOpsHQ.Hub). The Hub provides central monitoring, remote commands, and acts as the **single public Twilio webhook endpoint** for the entire deployment, routing inbound SMS and delivery-status callbacks to this store over SignalR. Outbound SMS is independent of the Hub. See the Hub README for setup.

---

## Database and migrations

Schema is managed with **EF Core migrations** in `SmsOpsHQ.Infrastructure/Persistence/Migrations/`. On startup, `SeedData.InitializeAsync` applies pending migrations automatically.

**New installs:** `MigrateAsync` creates the full schema, then seeds the default HQ admin user.

**Existing databases** created before migrations (legacy `EnsureCreated` + manual SQL upgrades) are detected at startup: idempotent legacy patches run once, the baseline migration is recorded in `__EFMigrationsHistory`, and future changes ship as normal EF migrations.

**Add a migration** (after changing `AppDbContext` or entities):

```powershell
cd SmsOpsHQ
dotnet ef migrations add <MigrationName> --project SmsOpsHQ.Infrastructure --startup-project SmsOpsHQ.Api --output-dir Persistence/Migrations
dotnet ef database update --project SmsOpsHQ.Infrastructure --startup-project SmsOpsHQ.Api
```

Default SQLite path: `Database:SqlitePath` or `ConnectionStrings:DefaultConnection` in `SmsOpsHQ.Api/appsettings.json` (typically `smsops.db`).

---

## Security

- JWT Bearer authentication on all protected endpoints
- BCrypt password hashing
- Rate limiting on login (5 per minute per IP)
- Password complexity: min 8 characters, mixed case, at least one digit
- CORS restricted to configured origins in production (open in development)
- Store isolation: users can only access their assigned store, HQ users access all
- Structured logging via Serilog (console + rolling daily file at `logs/`)
- RFC 7807 Problem Details for all error responses

---

## Deployment

### Windows setup installer (recommended for store PCs)

Build a self-contained **setup.exe** (Desktop + bundled local API, no .NET install on target machines):

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Inno Setup 6](https://jrsoftware.org/isdl.php)
2. From the repo root:

```powershell
.\build-setup.ps1
```

If scripts are blocked by execution policy, run `build-setup.cmd` instead (or `powershell -ExecutionPolicy Bypass -File .\build-setup.ps1`).

Outputs:

- `SmsOpsHQ.Desktop\bin\Publish\SmsOpsHQ-Setup-1.0.0-x64.exe` — installer
- `SmsOpsHQ.Desktop\bin\Publish\SmsOpsHQ-Desktop-StoreTest.zip` — portable ZIP

See `installer/README.md` for store deployment and first-run configuration.

### Publish (manual / server-only)

```powershell
# API
dotnet publish SmsOpsHQ.Api -c Release -o ./publish/api

# Desktop client
dotnet publish SmsOpsHQ.Desktop -c Release -r win-x64 -o ./publish/desktop

# Or store bundle (same as build-setup step 1):
.\SmsOpsHQ.Desktop\publish-store.ps1
```

### Run in Production

```powershell
# Set the JWT secret (never use the dev default)
$env:SMSOPSHQ_JWT_SECRET = "your-secure-random-secret-at-least-32-characters"

# Start the API
cd publish/api
dotnet SmsOpsHQ.Api.dll --urls http://localhost:5000
```

Or install as a Windows service:

```powershell
dotnet publish SmsOpsHQ.Api -c Release -r win-x64 --self-contained -o ./publish/api
sc.exe create SmsOpsHQ binPath="C:\path\to\publish\api\SmsOpsHQ.Api.exe"
sc.exe start SmsOpsHQ
```

### Pre-Deploy Checklist

- [ ] `dotnet test SmsOpsHQ.sln` passes
- [ ] `dotnet build -c Release` succeeds
- [ ] `SMSOPSHQ_JWT_SECRET` env var is set to a strong random value
- [ ] Twilio Account SID + Auth Token configured (for **outbound** sending)
- [ ] `Cors:AllowedOrigins` set to actual production origins
- [ ] Default admin password changed
- [ ] SQLite database file path is writable
- [ ] `Hub:Enabled = true` and `Hub:Url`, `Hub:StoreKey`, `Hub:DeploymentId` configured (this store joins the central Hub for monitoring and **inbound SMS**)
- [ ] Twilio webhook URL configured **on the Hub's public URL** (`https://<hub-host>/twilio-sms` and `/twilio-sms/status`), **not** on this store's public address
- [ ] No ngrok / per-store public tunnel started on the store PC (no longer required in the central-Hub architecture)
- [ ] At least one row exists in `TwilioNumbers` for this store (otherwise the Hub will not know to route inbound SMS here)
- [ ] After first heartbeat, the **Stores → Store detail** page on the Hub shows this store's number(s) without the "No Twilio numbers reported" warning

### Side-by-Side with Legacy Python API

The .NET API (port 5000) and legacy Python API (port 8000) can run simultaneously against the same SQLite database. This allows gradual migration:

1. Start both APIs
2. Point the desktop client at either one via Settings
3. Compare behavior
4. Switch over when confident

**Rollback:** stop the .NET API, start the Python API. The database is shared so no data migration is needed.

---

## Logging

Serilog writes to:

- **Console** -- real-time during development
- **File** -- `logs/smsops-{date}.log`, rolling daily, 30 days retention

Log levels are configured in `appsettings.json` under the `Serilog` section. Set `Serilog:MinimumLevel:Default` to `Debug` for verbose output during development.

---

## Development Guide

### Architecture

```
Desktop (WPF/MVVM)  -->  API (ASP.NET Core)  -->  Infrastructure (EF Core + Services)
                                                        |
                                                   Core (Entities, Interfaces)
```

**Core** has zero dependencies. **Infrastructure** depends on Core. **Api** depends on Infrastructure. **Desktop** depends on Core (for DTOs) and talks to the API over HTTP.

### Adding a New Feature

1. Define the entity in `Core/Entities/`
2. Add EF entity mapping in `Infrastructure/Persistence/Entities/` and `AppDbContext`
3. Add an EF migration (`dotnet ef migrations add ...`)
4. Add the repository interface in `Core/Repositories/`
5. Implement the repository in `Infrastructure/Repositories/`
6. Create the API controller in `Api/Controllers/`
7. Add a ViewModel in `Desktop/ViewModels/`
8. Create the View in `Desktop/Views/`
9. Register the DataTemplate mapping in `Desktop/App.xaml`
10. Write tests in `Tests/`

### Key Patterns

- **Repository pattern** -- all database access goes through `I*Repository` interfaces
- **Dependency injection** -- services are registered in `Program.cs` and `AddInfrastructure()`
- **MVVM** -- desktop views bind to ViewModels via `DataContext`, commands use `[RelayCommand]`
- **DataTemplates** -- `App.xaml` maps ViewModels to Views so navigation is ViewModel-driven
