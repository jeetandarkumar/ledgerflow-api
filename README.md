# LedgerFlow API

A multi-tenant financial SaaS API built with .NET 8. Handles invoice lifecycle management and payment recording for multiple independent tenants on a shared platform.

## Overview

LedgerFlow lets tenant businesses create, issue, and track invoices, record payments and refunds, and manage users within their own isolated workspace. Every piece of data is strictly tenant-scoped — there's no way for one tenant to access another's data.

**Core capabilities:**
- Invoice lifecycle: Draft → Issued → PartiallyPaid → Paid (or Voided)
- Payment and refund recording with idempotency (safe webhook replay)
- JWT-based multi-tenant authentication with role-based access control
- Full audit log for all financial state changes
- Account lockout after repeated failed login attempts

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 8, ASP.NET Core |
| Architecture | Clean Architecture, CQRS via MediatR |
| Database | SQL Server + Entity Framework Core 8 |
| Cache | Redis (falls back to in-memory if not configured) |
| Auth | JWT Bearer (HMAC-SHA256), BCrypt password hashing |
| Validation | FluentValidation (pipeline behavior) |
| Logging | Serilog (console + rolling file) |
| Testing | xUnit, Moq, FluentAssertions, WebApplicationFactory |

## Project Structure

```
src/
├── ledgerflowApi.Domain/           # Entities, value objects, domain exceptions, interfaces
├── ledgerflowApi.Application/      # Commands, queries, validators, MediatR pipeline behaviors
├── ledgerflowApi.Infrastructure/   # EF Core, repositories, JWT, BCrypt, Redis
└── ledgerflowApi.API/              # Controllers, middleware, startup
tests/
├── LedgerFlow.UnitTests/           # Domain logic, command handlers, validators
└── LedgerFlow.IntegrationTests/    # Full HTTP pipeline against in-memory database
```

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server (local or Docker)
- Redis (optional — falls back to in-memory cache if not configured)

### Run with Docker Compose

The easiest way to get everything running:

```bash
docker compose up -d
```

API: `http://localhost:5000`  
Swagger UI: `http://localhost:5000/swagger`

### Run Locally

**1. Configure secrets**

Never commit secrets to source control. Use .NET user secrets for local development:

```bash
cd src/ledgerflowApi.API

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=ledgerflowApiDb;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet user-secrets set "JwtSettings:SecretKey" "your-local-secret-at-least-32-chars"
```

**2. Apply migrations**

```bash
dotnet ef database update \
  --project src/ledgerflowApi.Infrastructure \
  --startup-project src/ledgerflowApi.API
```

**3. Start the API**

```bash
dotnet run --project src/ledgerflowApi.API
```

## Configuration Reference

All settings can be overridden via environment variables using `__` as the separator (e.g. `JwtSettings__SecretKey`).

| Setting | Description | Required |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string | Yes |
| `ConnectionStrings:Redis` | Redis connection string | No (uses in-memory) |
| `JwtSettings:SecretKey` | Signing key, min 32 characters | Yes |
| `JwtSettings:Issuer` | Token issuer claim | Yes |
| `JwtSettings:Audience` | Token audience claim | Yes |
| `JwtSettings:ExpirationMinutes` | Access token lifetime (default: 60) | No |
| `Cors:AllowedOrigins` | Array of allowed CORS origins | No |

> **Production:** Always supply `JwtSettings:SecretKey` via an environment variable or secrets manager — never commit it to source control.

## API Endpoints

All endpoints are prefixed with `/api/v1`.

### Auth

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/auth/login` | None | Authenticate and receive a JWT. Requires `X-Tenant-Id` header. |
| `POST` | `/auth/register` | Admin | Create a new user within the caller's tenant. |

### Invoices

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/invoices` | Any | List invoices with optional `?status=` filter. Paginated. |
| `GET` | `/invoices/{id}` | Any | Get a single invoice. |
| `POST` | `/invoices` | Member+ | Create a new Draft invoice. |
| `POST` | `/invoices/{id}/issue` | Member+ | Issue a Draft invoice (sets due date, freezes line items). |
| `POST` | `/invoices/{id}/void` | Admin | Void an invoice permanently. Requires a reason. |
| `POST` | `/invoices/{id}/payments` | Member+ | Record a payment or refund. |

### Users

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/users/{id}` | Any | Get a user profile (tenant-scoped). |

### Health

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Liveness check. Returns 200 when the app is running. |

### Roles

There are four roles in ascending order of permissions:

- **Viewer** — read-only access
- **Member** — can create and issue invoices, record payments
- **Admin** — all Member permissions + void invoices, register users
- **SuperAdmin** — platform-level; not assignable via the API

### Invoice Status Flow

```
Draft ──► Issued ──► PartiallyPaid ──► Paid
            │                           
            └──► Overdue ──► Paid       
                                        
Any non-Paid status ──► Voided
```

## Testing

### Run All Tests

```bash
dotnet test
```

### Run Only Unit Tests

```bash
dotnet test tests/LedgerFlow.UnitTests
```

### Run Only Integration Tests

```bash
dotnet test tests/LedgerFlow.IntegrationTests
```

Integration tests use EF Core's in-memory provider and a `WebApplicationFactory` — no SQL Server required.

### Test Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
```

## CI/CD Workflow

The pipeline lives in `.github/workflows/ci-cd.yml` and runs on the `Dev` branch. It uses a **self-hosted runner** and deploys directly to Windows server paths.

**Three-stage pipeline:**

```
Dev push
  └── DEV: build + unit tests + deploy to D:\Projects\ledgerflowApi\DEV
        └── [QA approval gate]
              └── UAT: build + integration tests + deploy to ...\UAT
                    └── [PM approval gate]
                          └── PROD: build + deploy to ...\PROD
```

Each stage requires the previous one to succeed. UAT and PROD deployments require manual approval in GitHub Environments before they run.

Test results are published as GitHub Checks via `dorny/test-reporter`.

## Adding a New Feature

1. **Domain** — Add entity in `ledgerflowApi.Domain/Entities`, repository interface in `ledgerflowApi.Domain/Interfaces`
2. **Application** — Add command or query in `ledgerflowApi.Application/Features/{Feature}`, with validator in the same file
3. **Infrastructure** — Add EF config in `Persistence/Configurations`, repository in `Persistence/Repositories`, register in `InfrastructureServiceExtensions`
4. **API** — Add controller action, map request DTO to command

## Roadmap / Planned Improvements

- **Refresh token storage** — refresh tokens are currently generated but not persisted; a `RefreshTokens` table and `/auth/refresh` endpoint are needed
- **Overdue job** — a background service (Hangfire or hosted service) to call `invoice.MarkAsOverdue()` on past-due invoices nightly
- **Email notifications** — `InvoiceIssuedEvent` and `InvoicePaidEvent` domain events are raised but have no handlers; an email delivery handler is the natural next step
- **Tenant onboarding** — tenant creation is currently database-seeded only; a self-service signup flow with Stripe billing is planned
- **Pagination improvement** — `ListInvoices` currently loads all matching invoices then paginates in-memory; it should push the `SKIP`/`TAKE` to the database query
- **Currency support** — the platform supports multi-currency invoices but aggregate totals in the list endpoint assume a single currency per tenant
