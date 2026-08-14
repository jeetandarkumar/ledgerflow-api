# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore dependencies (layer caching)
COPY ["src/ledgerflowApi.Domain/ledgerflowApi.Domain.csproj", "src/ledgerflowApi.Domain/"]
COPY ["src/ledgerflowApi.Application/ledgerflowApi.Application.csproj", "src/ledgerflowApi.Application/"]
COPY ["src/ledgerflowApi.Infrastructure/ledgerflowApi.Infrastructure.csproj", "src/ledgerflowApi.Infrastructure/"]
COPY ["src/ledgerflowApi.API/ledgerflowApi.API.csproj", "src/ledgerflowApi.API/"]

RUN dotnet restore "src/ledgerflowApi.API/ledgerflowApi.API.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/src/ledgerflowApi.API"
RUN dotnet build "ledgerflowApi.API.csproj" -c Release -o /app/build

# ---- Publish Stage ----
FROM build AS publish
RUN dotnet publish "ledgerflowApi.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl is required by the HEALTHCHECK below and is not present on the base
# aspnet runtime image — without this, the HEALTHCHECK instruction fails
# with "curl: not found" and the container is reported unhealthy forever.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create a non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Create logs directory with proper permissions
RUN mkdir -p /app/logs && chown -R appuser:appgroup /app/logs

COPY --from=publish /app/publish .
RUN chown -R appuser:appgroup /app

USER appuser

EXPOSE 8080
EXPOSE 8081

# Liveness only — checks the process is up and responsive, not that its
# dependencies (SQL Server, Redis) are reachable. Use GET /health/ready
# (or /health) from outside the container for a dependency-aware readiness check.
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "ledgerflowApi.API.dll"]
