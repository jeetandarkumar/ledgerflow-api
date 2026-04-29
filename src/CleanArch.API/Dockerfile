# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore dependencies (layer caching)
COPY ["src/CleanArch.Domain/CleanArch.Domain.csproj", "src/CleanArch.Domain/"]
COPY ["src/CleanArch.Application/CleanArch.Application.csproj", "src/CleanArch.Application/"]
COPY ["src/CleanArch.Infrastructure/CleanArch.Infrastructure.csproj", "src/CleanArch.Infrastructure/"]
COPY ["src/CleanArch.API/CleanArch.API.csproj", "src/CleanArch.API/"]

RUN dotnet restore "src/CleanArch.API/CleanArch.API.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/src/CleanArch.API"
RUN dotnet build "CleanArch.API.csproj" -c Release -o /app/build

# ---- Publish Stage ----
FROM build AS publish
RUN dotnet publish "CleanArch.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create a non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Create logs directory with proper permissions
RUN mkdir -p /app/logs && chown -R appuser:appgroup /app/logs

COPY --from=publish /app/publish .
RUN chown -R appuser:appgroup /app

USER appuser

EXPOSE 8080
EXPOSE 8081

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "CleanArch.API.dll"]
