using ledgerflowApi.Application.Common.Interfaces;
using ledgerflowApi.Domain.Entities;
using ledgerflowApi.Domain.Enums;
using ledgerflowApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ledgerflowApi.API.Persistence;

/// <summary>
/// Seeds a demo tenant and admin user on first run in Development.
///
/// Without this, a fresh clone has no way to log in — there is no Tenant
/// or User in the database, so every login attempt returns 400.
///
/// Seeded credentials (Development only):
///   Tenant:   Demo Corp  (slug: demo-corp)
///   Email:    admin@democorp.example
///   Password: Demo@1234!
///   Role:     Admin
///   TenantId: printed to console on first run — copy it for the X-Tenant-Id header
///
/// This runs ONLY when ASPNETCORE_ENVIRONMENT=Development and the Tenants
/// table is empty. It is a no-op on subsequent startups.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ILogger logger)
    {
        // No-op if data already exists
        if (await context.Tenants.AnyAsync())
        {
            logger.LogDebug("DbSeeder: data already exists, skipping seed.");
            return;
        }

        logger.LogInformation("DbSeeder: seeding demo tenant and admin user...");

        // ── Tenant ────────────────────────────────────────────────────────────
        var tenant = Tenant.Create(
            name:            "Demo Corp",
            slug:            "demo-corp",
            billingEmail:    "billing@democorp.example",
            defaultCurrency: "USD",
            trialDays:       30);

        context.Tenants.Add(tenant);

        // ── Admin user ────────────────────────────────────────────────────────
        var adminUser = User.Create(
            tenantId:     tenant.Id,
            firstName:    "Admin",
            lastName:     "User",
            email:        "admin@democorp.example",
            passwordHash: passwordHasher.Hash("Demo@1234!"),
            role:         UserRole.Admin);

        context.Users.Add(adminUser);

        // ── Member user (for testing role-based access) ───────────────────────
        var memberUser = User.Create(
            tenantId:     tenant.Id,
            firstName:    "Member",
            lastName:     "User",
            email:        "member@democorp.example",
            passwordHash: passwordHasher.Hash("Demo@1234!"),
            role:         UserRole.Member);

        context.Users.Add(memberUser);

        await context.SaveChangesAsync();

        logger.LogInformation(
            "DbSeeder: seed complete.\n" +
            "  Tenant ID : {TenantId}\n" +
            "  Admin     : admin@democorp.example  / Demo@1234!\n" +
            "  Member    : member@democorp.example / Demo@1234!\n" +
            "  Use TenantId in the X-Tenant-Id header when calling /api/v1/auth/login",
            tenant.Id);
    }
}
