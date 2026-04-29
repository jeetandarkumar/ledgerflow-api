# ledgerflow-api

A .NET 8 Web API scaffolded with Clean Architecture.

## Project Structure

```
CleanArchitecture/
├── src/
│   ├── CleanArch.Domain/           # Entities, interfaces, domain exceptions
│   ├── CleanArch.Application/      # Use cases, CQRS, validators, behaviors
│   ├── CleanArch.Infrastructure/   # EF Core, repositories, services, Redis
│   └── CleanArch.API/              # Controllers, middleware, startup
└── tests/
    ├── CleanArch.UnitTests/
    └── CleanArch.IntegrationTests/
```

## Getting Started

### Prerequisites
- .NET 8 SDK
- Docker + Docker Compose

### Run with Docker

```bash
docker-compose up -d
```

API will be available at: http://localhost:5000  
Swagger UI: http://localhost:5000/swagger

### Run locally

1. Update `appsettings.Development.json` with your SQL Server and Redis connection strings.

2. Apply migrations:
```bash
cd src/CleanArch.API
dotnet ef database update --project ../CleanArch.Infrastructure
```

3. Run the API:
```bash
dotnet run
```

## Configuration

| Setting | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `ConnectionStrings:Redis` | Redis connection string |
| `JwtSettings:SecretKey` | Must be at least 32 characters |
| `JwtSettings:Issuer` | Token issuer |
| `JwtSettings:Audience` | Token audience |

## Adding a New Feature

1. **Domain** — Add your entity in `CleanArch.Domain/Entities`
2. **Domain** — Add repository interface in `CleanArch.Domain/Interfaces`
3. **Application** — Add command/query in `CleanArch.Application/Features/YourFeature`
4. **Infrastructure** — Add EF config + repository implementation
5. **API** — Add controller + wire up routes

## Tech Stack

- .NET 8
- MediatR (CQRS)
- FluentValidation
- Entity Framework Core 8 + SQL Server
- Redis (StackExchange.Redis)
- Serilog
- JWT Bearer Auth
- Swagger / OpenAPI
- xUnit + Moq + FluentAssertions
