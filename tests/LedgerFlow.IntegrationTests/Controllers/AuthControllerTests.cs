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
/// Integration tests for POST /api/v1/auth/login and POST /api/v1/auth/register.
///
/// Key design notes:
/// - Login reads the tenant from the X-Tenant-Id request header, NOT the body.
/// - Register is an authenticated Admin-only endpoint that creates a new user
///   inside the calling admin's own tenant. It is NOT a public signup endpoint.
/// - Tenant.Create() signature: (name, slug, billingEmail, defaultCurrency).
/// </summary>
[Collection("Integration")]
public class AuthControllerTests : IntegrationTestBase
{
    // ── Seed helpers ──────────────────────────────────────────────────────────

    private async Task<(Tenant tenant, User user, string plainPassword)> SeedUserAsync(
        TenantStatus tenantStatus = TenantStatus.Active,
        UserRole role = UserRole.Admin)
    {
        var plainPassword = "ValidPass123!";
        var hasher = new PasswordHasher();

        Tenant? tenant = null;
        User? user = null;

        await SeedAsync(async db =>
        {
            // Correct signature: (name, slug, billingEmail, defaultCurrency)
            tenant = Tenant.Create("Acme Corp", "acme", "billing@acme.com", "USD");

            if (tenantStatus != TenantStatus.Active)
                typeof(Tenant).GetProperty("Status")!.SetValue(tenant, tenantStatus);

            await db.Tenants.AddAsync(tenant);
            await db.SaveChangesAsync();

            user = User.Create(tenant.Id, "Alice", "Smith", "alice@acme.com",
                hasher.Hash(plainPassword), role);
            await db.Users.AddAsync(user);
        });

        return (tenant!, user!, plainPassword);
    }

    // ── Helper: build login request with X-Tenant-Id header ──────────────────

    private static HttpRequestMessage BuildLoginRequest(Guid tenantId, string email, string password)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        msg.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return msg;
    }

    // ── POST /api/v1/auth/login ───────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        // Arrange
        var (tenant, _, password) = await SeedUserAsync();

        // Act
        var response = await Client.SendAsync(
            BuildLoginRequest(tenant.Id, "alice@acme.com", password));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body!.RefreshToken.Should().NotBeNullOrEmpty();
        body!.User.Email.Should().Be("alice@acme.com");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns400WithGenericMessage()
    {
        // Arrange
        var (tenant, _, _) = await SeedUserAsync();

        // Act
        var response = await Client.SendAsync(
            BuildLoginRequest(tenant.Id, "alice@acme.com", "WrongPassword!"));

        // Assert
        // Handler returns Result.Failure → controller maps to 400 BadRequest
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("email does not exist");
        body.Should().NotContain("wrong password");
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns400()
    {
        // Arrange
        var (tenant, _, _) = await SeedUserAsync();

        // Act
        var response = await Client.SendAsync(
            BuildLoginRequest(tenant.Id, "nobody@acme.com", "SomePassword!"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_SuspendedTenant_Returns400()
    {
        // Arrange
        var (tenant, _, password) = await SeedUserAsync(TenantStatus.Suspended);

        // Act
        var response = await Client.SendAsync(
            BuildLoginRequest(tenant.Id, "alice@acme.com", password));

        // Assert — handler returns failure for suspended tenant → 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_MissingTenantIdHeader_Returns400()
    {
        // Act — no X-Tenant-Id header; controller returns 400 explicitly
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = "alice@acme.com",
            password = "Pass123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] 
    public async Task Login_EmptyBody_Returns400()
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { })
        };
        msg.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await Client.SendAsync(msg);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ── POST /api/v1/auth/register ────────────────────────────────────────────

    [Fact]
    public async Task Register_ValidRequest_AdminJwt_Returns201()
    {
        // Arrange
        var (tenant, _, password) = await SeedUserAsync(role: UserRole.Admin);
        var token = await GetAuthTokenAsync("alice@acme.com", password, tenant.Id);
        var authClient = CreateClientWithToken(token);

        // Act
        var response = await authClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Bob",
            lastName = "Jones",
            email = "bob@acme.com",
            password = "Secure123!@",
            role = "Member"
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_Unauthenticated_Returns401()
    {
        // Act — no JWT at all
        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Bob",
            lastName = "Jones",
            email = "bob@acme.com",
            password = "Secure123!@"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_MemberRole_Returns403()
    {
        // Arrange — Member cannot call register (RequireAdmin policy)
        var (tenant, _, password) = await SeedUserAsync(role: UserRole.Member);
        var token = await GetAuthTokenAsync("alice@acme.com", password, tenant.Id);
        var authClient = CreateClientWithToken(token);

        // Act
        var response = await authClient.PostAsJsonAsync("/api/v1/auth/register", new
        {
            firstName = "Bob",
            lastName = "Jones",
            email = "bob@acme.com",
            password = "Secure123!@"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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