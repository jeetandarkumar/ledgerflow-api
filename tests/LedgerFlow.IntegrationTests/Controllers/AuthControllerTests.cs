using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LedgerFlow.IntegrationTests.Infrastructure;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Infrastructure.Identity;
using ledgerflowApi.Infrastructure.Persistence;
using Xunit;

namespace LedgerFlow.IntegrationTests.Controllers;

/// <summary>
/// Integration tests for POST /api/auth/login and POST /api/auth/register.
/// These tests boot the full ASP.NET pipeline and talk to an in-memory database,
/// so they test the actual HTTP response codes, JSON shapes, and middleware
/// (rate limiting, global exception handling, validation) together.
/// </summary>
[Collection("Integration")]
public class AuthControllerTests : IntegrationTestBase
{
    // ── Seed helpers ──────────────────────────────────────────────────────────

    private async Task<(Tenant tenant, User user, string plainPassword)> SeedUserAsync(
        TenantStatus tenantStatus = TenantStatus.Active)
    {
        var plainPassword = "ValidPass123!";
        var hasher = new PasswordHasher();

        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            tenant = Tenant.Create("Acme Corp", "acme", "USD");
            if (tenantStatus != TenantStatus.Active)
                typeof(Tenant).GetProperty("Status")!.SetValue(tenant, tenantStatus);

            await db.Tenants.AddAsync(tenant);
            await db.SaveChangesAsync();

            user = User.Create(tenant.Id, "Alice", "Smith", "alice@acme.com",
                hasher.Hash(plainPassword));
            await db.Users.AddAsync(user);
        });

        return (tenant!, user!, plainPassword);
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        // Arrange
        var (tenant, _, password) = await SeedUserAsync();

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            tenantId = tenant.Id,
            email = "alice@acme.com",
            password
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body!.RefreshToken.Should().NotBeNullOrEmpty();
        body!.User.Email.Should().Be("alice@acme.com");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithGenericMessage()
    {
        // Arrange
        var (tenant, _, _) = await SeedUserAsync();

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            tenantId = tenant.Id,
            email = "alice@acme.com",
            password = "WrongPassword!"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        // Message should NOT reveal whether email or password was wrong
        body.Should().Contain("incorrect");
        body.Should().NotContain("email does not exist");
        body.Should().NotContain("wrong password");
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401WithSameGenericMessage()
    {
        // Arrange
        var (tenant, _, _) = await SeedUserAsync();

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            tenantId = tenant.Id,
            email = "nobody@acme.com",
            password = "SomePassword!"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_SuspendedTenant_Returns403()
    {
        // Arrange
        var (tenant, _, password) = await SeedUserAsync(TenantStatus.Suspended);

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            tenantId = tenant.Id,
            email = "alice@acme.com",
            password
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("", "alice@acme.com", "Pass123!")]
    [InlineData("not-a-guid", "alice@acme.com", "Pass123!")]
    public async Task Login_InvalidRequest_Returns400(
        string tenantId, string email, string password)
    {
        // Arrange — no seeding needed, validation fires before any DB lookup
        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            tenantId,
            email,
            password
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_EmptyBody_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/auth/register ───────────────────────────────────────────────

    [Fact]
    public async Task Register_ValidRequest_Returns201()
    {
        // Arrange — create a tenant to register into
        Tenant? tenant = null;
        await SeedAsync(async db =>
        {
            tenant = Tenant.Create("Test Corp", "testcorp", "USD");
            await db.Tenants.AddAsync(tenant!);
        });

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            tenantId = tenant!.Id,
            firstName = "Bob",
            lastName = "Jones",
            email = "bob@testcorp.com",
            password = "Secure123!@",
            confirmPassword = "Secure123!@"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        // Arrange
        var (tenant, _, _) = await SeedUserAsync();

        // Act — try to register alice again
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            tenantId = tenant.Id,
            firstName = "Alice",
            lastName = "Again",
            email = "alice@acme.com",  // already exists
            password = "Secure123!@",
            confirmPassword = "Secure123!@"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_PasswordMismatch_Returns400()
    {
        // Arrange
        Tenant? tenant = null;
        await SeedAsync(async db =>
        {
            tenant = Tenant.Create("Corp", "corp", "USD");
            await db.Tenants.AddAsync(tenant!);
        });

        // Act
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            tenantId = tenant!.Id,
            firstName = "Dave",
            lastName = "Green",
            email = "dave@corp.com",
            password = "Password1!",
            confirmPassword = "Password2!"  // mismatch
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Response DTOs ─────────────────────────────────────────────────────────

    private sealed record LoginResponseDto(
        string AccessToken,
        string RefreshToken,
        DateTime ExpiresAt,
        UserInfoDto User);

    private sealed record UserInfoDto(
        Guid Id,
        string FullName,
        string Email,
        string Role,
        Guid TenantId,
        string TenantName);
}
