# LedgerFlow API

A multi-tenant financial API built with .NET 8, Clean Architecture, and a full DDD domain model. It covers the complete invoice lifecycle, payment processing (including refunds), JWT auth with per-tenant context, and a layered audit trail backed by SQL Server triggers and application-level audit logs.

Built as a portfolio project — not a toy CRUD app.

-----

## What's in here

The domain handles the full invoice lifecycle: Draft → Issued → PartiallyPaid / Paid / Overdue → Voided. Payments and refunds are separate aggregates that reference invoices by ID, keeping the invoice's paid amount updated atomically with the payment record.

Multi-tenancy is enforced at the type level via `TenantEntity` — every financial record carries a `TenantId` and the domain throws `TenantMismatchException` if records from different tenants are mixed. The `TenantResolutionMiddleware` rejects authenticated requests with no valid `tenant_id` claim before they reach any handler.

Auth uses BCrypt (work factor 11) with a timing-safe dummy hash path for missing users so response time doesn't leak whether an email exists. Accounts lock after 5 failed attempts for 30 minutes.

---

## Project Structure

```
ledgerflowApi/
├── src/
│   ├── ledgerflowApi.Domain/           # Entities, value objects, domain exceptions, events
│   ├── ledgerflowApi.Application/      # CQRS handlers, FluentValidation, MediatR behaviors
│   ├── ledgerflowApi.Infrastructure/   # EF Core, repositories, Redis, JWT, BCrypt
│   └── ledgerflowApi.API/              # Controllers, middleware, rate limiting, Swagger
└── tests/
    ├── CleanArch.UnitTests/
    └── CleanArch.IntegrationTests/
```

---

## Tech Stack

| Concern | Choice |
|---|---|
| Framework | .NET 8 / ASP.NET Core |
| ORM | Entity Framework Core 8 (SQL Server) |
| CQRS | MediatR 12 |
| Validation | FluentValidation 11 |
| Auth | JWT Bearer (HMAC-SHA256) |
| Password hashing | BCrypt.Net-Next (work factor 11) |
| Caching | Redis via StackExchange.Redis |
| Logging | Serilog (console + rolling file) |
| API Docs | Swagger / OpenAPI |
| Containers | Docker + Docker Compose |

---

## Getting Started

### Run with Docker (quickest)

```bash
docker-compose up -d
```

API: `http://localhost:5000`
Swagger: `http://localhost:5000/swagger`

SQL Server and Redis start first; the API waits for their health checks before accepting connections. On first startup the database is migrated and demo seed data is created automatically.

### Run locally

**Prerequisites:** .NET 8 SDK, SQL Server (local or Docker), Redis (optional — falls back to in-memory cache)

1. Update `src/ledgerflowApi.API/appsettings.Development.json` with your connection strings (already filled with sensible local defaults).

2. Run the API — migrations and seed data are applied automatically on startup in Development:

```bash
cd src/ledgerflowApi.API
dotnet run
```

On first run the console will print the seeded Tenant ID:

```
DbSeeder: seed complete.
  Tenant ID : xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
  Admin     : admin@democorp.example  / Demo@1234!
  Member    : member@democorp.example / Demo@1234!
  Use TenantId in the X-Tenant-Id header when calling /api/v1/auth/login
```

Copy that Tenant ID — you need it for the `X-Tenant-Id` header on every login request.

### Environment Variables

| Variable | Description |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `ConnectionStrings__Redis` | Redis connection string (optional) |
| `JwtSettings__SecretKey` | Signing key — min 32 chars |
| `JwtSettings__Issuer` | Token issuer |
| `JwtSettings__Audience` | Token audience |
| `JwtSettings__ExpirationMinutes` | Access token lifetime (default: 60) |

---

## API Overview

All routes are tenant-scoped. The tenant is resolved from the `tenant_id` claim in the JWT — there is no way to access another tenant's data even with a valid token.

### Authentication

| Method | Route | Auth | Description |
|---|---|---|---|
| POST | `/api/v1/auth/login` | None | Returns a JWT access token |
| POST | `/api/v1/auth/register` | Admin | Creates a new user in the caller's tenant |

Login requires the `X-Tenant-Id` header (tenant GUID). On success you get an `accessToken`
that expires after 60 minutes — there is no refresh token (see Known Limitations below).

**Login request:**
```json
POST /api/v1/auth/login
X-Tenant-Id: <tenant-guid>

{
  "email": "admin@democorp.example",
  "password": "Demo@1234!"
}
```

**Response:**
```json
{
  "accessToken": "eyJ...",
  "expiresAt": "2025-01-01T01:00:00Z",
  "user": {
    "id": "...",
    "fullName": "Admin User",
    "email": "admin@democorp.example",
    "role": "Admin",
    "tenantId": "...",
    "tenantName": "Demo Corp"
  }
}
```

---

### Invoices

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/v1/invoices` | Any | Paginated list, optional `?status=` filter |
| GET | `/api/v1/invoices/{id}` | Any | Single invoice |
| POST | `/api/v1/invoices` | Member+ | Create draft |
| POST | `/api/v1/invoices/{id}/issue` | Member+ | Issue draft to customer |
| POST | `/api/v1/invoices/{id}/void` | Admin | Void (requires reason, min 10 chars) |
| POST | `/api/v1/invoices/{id}/payments` | Member+ | Record payment or refund |

**Create invoice:**
```json
POST /api/v1/invoices
Authorization: Bearer <token>

{
  "customerName": "Acme Corp",
  "customerEmail": "billing@acmecorp.example",
  "currency": "USD",
  "taxRatePercentage": 10,
  "discountPercentage": 0,
  "lineItems": [
    {
      "description": "Consulting – October 2024",
      "unitPrice": 1500.00,
      "quantity": 3,
      "discountPercentage": 0
    }
  ],
  "notes": "Net 30"
}
```

**Invoice status flow:**

```
Draft ──► Issued ──► PartiallyPaid ──► Paid
             │                │
             └──── Overdue ───┘
             │
             └──► Voided  (also reachable from Draft, PartiallyPaid, Overdue)
```

---

### Payments

Payments go against an issued invoice. Refunds are a separate payment record with `"type": "Refund"` linked to the original via `refundedPaymentId`.

Core fields (`Amount`, `Currency`, `Type`) are set once and never change after creation. The one exception: processing a refund updates the *original* payment's `RefundedAmount` — a running total of how much of that payment has been refunded so far — so a payment can never be refunded for more than it was originally worth, even across several partial refunds. Applying a payment or refund also updates the invoice's `PaidAmount` and recalculates its status (`Issued` → `PartiallyPaid` → `Paid`, or back down on a refund); `RecordPayment` rejects any amount that would push `PaidAmount` above the invoice total, and `ApplyRefund` rejects any refund that would exceed what's left refundable on the original payment.

```json
POST /api/v1/invoices/{id}/payments
Authorization: Bearer <token>

{
  "amount": 1500.00,
  "currency": "USD",
  "paymentMethod": "bank_transfer",
  "externalReference": "ch_stripe_xxx",
  "type": "Standard"
}
```

**Idempotency (`externalReference`)** — submitting the same `externalReference` twice returns the existing payment instead of creating a duplicate, including under concurrent submission. This is enforced in layers rather than by a single check:
1. An early lookup before any work begins — the fast path for the common, non-concurrent case.
2. A second lookup immediately before the write, closing the window where a concurrent duplicate request committed in between the first lookup and this request's own write.
3. A database-level unique index (`UX_Payments_ExternalReference`, filtered on non-null references) as the last-resort backstop for relational deployments, paired with concurrency-conflict handling that resolves to the winning payment instead of surfacing an error if two requests still land on layer 3 simultaneously.

**Concurrency** — `Invoice.UpdatedAt` is an optimistic concurrency token, so two payments or refunds racing against the same invoice can't both silently apply: the loser's write is rejected rather than corrupting `PaidAmount`, and is resolved the same way as an idempotency conflict where applicable. `Payment.RowVersion` gives the same protection specifically for two refunds racing against the same original payment. This is exercised directly by integration tests, including genuinely concurrent requests fired with `Task.WhenAll` — see Running Tests for what those tests do and don't prove.

---

### Users

| Method | Route | Auth | Description |
|---|---|---|---|
| GET | `/api/v1/users/{id}` | Authenticated | Get user profile (tenant-scoped) |

---

## Authorization Roles

| Role | Can do |
|---|---|
| Viewer | Read invoices and payments |
| Member | Create and issue invoices, record payments |
| Admin | Everything above + register users, void invoices |
| SuperAdmin | Platform-level (not assignable via API) |

`SuperAdmin` is blocked from assignment at three independent layers — the `RegisterUserCommand` validator, the `User` factory method, and the role-change method — so the restriction holds even if one of those call paths is bypassed or extended later.

---

## Domain Design Notes

**Money value object** — all amounts are `decimal` with `MidpointRounding.AwayFromZero`. Cross-currency arithmetic throws `CurrencyMismatchException` rather than silently converting.

**Invoice totals are computed, not stored** — `Subtotal`, `TaxAmount`, `TotalAmount`, and `OutstandingAmount` are calculated from the JSON line items column at runtime. Only `PaidAmount` is persisted. This means historical invoices are immune to tax rate changes.

**Audit trail** — every state change writes an `AuditLog` entry in the same database transaction as the data change. SQL Server triggers on Invoices, Payments, Users, and Tenants write additional entries as a safety net even for direct DB edits.

**Invoice sequence** — `usp_GetNextInvoiceNumber` uses `UPDLOCK + HOLDLOCK` to generate gap-free `INV-YYYY-NNNNNN` numbers under concurrent load. A sequence number consumed by a rolled-back transaction creates a gap (intentional — no reuse).

**Tenant currency in JWT** — the `tenant_currency` claim is populated from the tenant's `DefaultCurrency` at login time. Controllers can read it from `ICurrentUserService.DefaultCurrency` without a DB lookup on every request.

---

## Rate Limiting

Three policies sit on top of JWT auth:

| Policy | Applies to | Limit |
|---|---|---|
| `auth` | Login / register | 10 req/min per IP |
| `api` | All other endpoints | 120 req/min per user |
| `strict` | Sensitive mutations | 30 req/min per user |

All 429 responses include a `Retry-After` header.

---

## Running Tests

```bash
dotnet test tests/LedgerFlow.UnitTests
dotnet test tests/LedgerFlow.IntegrationTests
```

**Unit tests** (`LedgerFlow.UnitTests`) cover the domain layer directly — invoice lifecycle transitions, the `Money` and `InvoiceStatus` value objects, command handlers (login, create/issue/void invoice, process payment), validators, and the password hasher / token service.

**Integration tests** (`LedgerFlow.IntegrationTests`) run against a real in-process HTTP pipeline via `WebApplicationFactory` (`LedgerFlowWebApplicationFactory`), covering auth, invoice, and payment/tenant-isolation flows end to end through actual controller and middleware code — not mocks. The database underneath is EF Core's **InMemory provider**, not SQL Server: real for exercising application code, controllers, middleware, and EF Core's own optimistic-concurrency tracking (`Invoice.UpdatedAt`, `Payment.RowVersion`), but it does **not** enforce relational constructs like the unique index on `ExternalReference` — so the DB-level idempotency backstop described under Payments is a real, migrated constraint that runs against SQL Server, but is not itself exercised by this suite. What the concurrency and idempotency tests in `PaymentRefundConcurrencyTests` actually verify — including genuinely concurrent requests fired with `Task.WhenAll` against the same invoice, the same original payment, and the same `externalReference` — is the *application-level* idempotency checks and the optimistic-concurrency tokens, end to end through real HTTP requests. That's a meaningful, real guarantee, just not the same claim as "the database constraint is tested."

Together the two suites run close to 300 tests (253 unit, 44 integration, as of the latest verified local run).

---

## CI/CD

GitHub Actions triggers on push to `Dev` and runs a single pipeline with three sequential jobs, gated by GitHub Environments:

1. **DEV** — restores, builds, runs the unit test suite, publishes results, and deploys straight to the DEV IIS site. Runs automatically.
2. **UAT** — waits for DEV to succeed (`needs: dev`), then runs the full integration test suite (`LedgerFlow.IntegrationTests`, via `WebApplicationFactory`) against a build of the solution, publishes results, and deploys to UAT. The `UAT` environment is configured with a required reviewer, so the job pauses for manual approval before it runs.
3. **PROD** — waits for UAT to succeed, then deploys to PROD. The `PROD` environment also requires manual approval.

Each stage only runs after the previous one has passed, so nothing reaches UAT without a green unit-test build, and nothing reaches PROD without a green integration-test build and a human approval at each gate.

See `.github/workflows/ci-cd.yml`.

---

## Postman Collection

Import `ledgerflow-api.postman_collection.json` from the repo root.

All variables (`baseUrl`, `tenantId`, `adminPassword`) are built into the collection — no separate environment file is needed. After import, go to the collection's **Variables** tab and set `tenantId` to the value printed by `DbSeeder` on first startup.

Run requests in this order for the full happy-path flow:

1. **Auth → Login** — saves `accessToken` automatically to the collection variable
2. **Invoices → Create Invoice** — saves `invoiceId` automatically
3. **Invoices → Issue Invoice**
4. **Invoices → Process Payment — Standard** — saves `paymentId` automatically
5. **Invoices → Process Payment — Refund**

The **Error Scenarios** folder covers 401, 403, 422, and 429 responses.

---

## Known Limitations

- No refresh tokens — an earlier version issued one on login, but nothing persisted or
  validated it server-side, so it was a token in the API contract that could never actually
  be redeemed. Removed until refresh persistence, rotation, and a `/auth/refresh` endpoint are
  built together as one real feature. `ITokenService.GenerateRefreshToken` still exists and is
  unit-tested, ready for that work — access tokens currently expire after 60 minutes and the
  client must log in again.
- `GET /users` list endpoint not yet implemented — only `GET /users/{id}` exists
