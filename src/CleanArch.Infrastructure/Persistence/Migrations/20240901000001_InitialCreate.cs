using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArch.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Tenants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                BillingEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                DefaultCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "USD"),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Trial"),
                BillingAddress_Line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                BillingAddress_Line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                BillingAddress_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                BillingAddress_State = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                BillingAddress_CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true),
                BillingAddress_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                TrialEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Tenants", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                PasswordHash = table.Column<string>(type: "char(60)", fixedLength: true, maxLength: 60, nullable: false),
                Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Member"),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                FailedLoginAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.ForeignKey("FK_Users_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Invoices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CustomerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false),
                TaxRatePercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false, defaultValue: 0.00m),
                DiscountPercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false, defaultValue: 0.00m),
                PaidAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, defaultValue: 0.0000m),
                PaidCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false),
                LineItems = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                BillingAddress_Line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                BillingAddress_Line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                BillingAddress_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                BillingAddress_State = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                BillingAddress_CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true),
                BillingAddress_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Invoices", x => x.Id);
                table.ForeignKey("FK_Invoices_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Invoices_Users_CreatedByUserId", x => x.CreatedByUserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "InvoiceSequences",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Year = table.Column<int>(type: "int", nullable: false),
                LastSequence = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InvoiceSequences", x => new { x.TenantId, x.Year });
                table.ForeignKey("FK_InvoiceSequences_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RefundedPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Standard"),
                PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                InitiatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Payments", x => x.Id);
                table.ForeignKey("FK_Payments_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Payments_Invoices_InvoiceId", x => x.InvoiceId,
                    "Invoices", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Payments_Payments_RefundedPaymentId", x => x.RefundedPaymentId,
                    "Payments", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Payments_Users_InitiatedByUserId", x => x.InitiatedByUserId,
                    "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UserDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                StateBefore = table.Column<string>(type: "nvarchar(max)", nullable: true),
                StateAfter = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Metadata = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
                table.ForeignKey("FK_AuditLogs_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        // ── Indexes ───────────────────────────────────────────────────────────
        migrationBuilder.CreateIndex("UX_Tenants_Slug", "Tenants", "Slug", unique: true);
        migrationBuilder.CreateIndex("IX_Tenants_Status", "Tenants", "Status");
        migrationBuilder.CreateIndex("IX_Users_TenantId", "Users", "TenantId");
        migrationBuilder.CreateIndex("UX_Users_TenantId_Email", "Users", new[] { "TenantId", "Email" }, unique: true);
        migrationBuilder.CreateIndex("IX_Users_TenantId_IsActive", "Users", new[] { "TenantId", "IsActive" });
        migrationBuilder.CreateIndex("IX_Invoices_TenantId_InvoiceNumber", "Invoices", new[] { "TenantId", "InvoiceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_Invoices_TenantId_Status", "Invoices", new[] { "TenantId", "Status" });
        migrationBuilder.CreateIndex("IX_Invoices_Status_DueDate", "Invoices", new[] { "Status", "DueDate" });
        migrationBuilder.CreateIndex("IX_Invoices_TenantId_CustomerEmail", "Invoices", new[] { "TenantId", "CustomerEmail" });
        migrationBuilder.CreateIndex("IX_Payments_TenantId_InvoiceId_CreatedAt", "Payments", new[] { "TenantId", "InvoiceId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_AuditLogs_TenantId", "AuditLogs", "TenantId");
        migrationBuilder.CreateIndex("IX_AuditLogs_Entity", "AuditLogs", new[] { "TenantId", "EntityType", "EntityId" });
        migrationBuilder.CreateIndex("IX_AuditLogs_CreatedAt", "AuditLogs", "CreatedAt");

        // ── Stored procedure for invoice sequence generation ──────────────────
        migrationBuilder.Sql(@"
            CREATE OR ALTER PROCEDURE [dbo].[usp_GetNextInvoiceNumber]
                @TenantId     UNIQUEIDENTIFIER,
                @Year         INT,
                @NextSequence INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;
                DECLARE @LocalTransaction BIT = 0;
                IF @@TRANCOUNT = 0 BEGIN BEGIN TRANSACTION; SET @LocalTransaction = 1; END
                BEGIN TRY
                    UPDATE [dbo].[InvoiceSequences] WITH (UPDLOCK, HOLDLOCK)
                    SET [LastSequence] = [LastSequence] + 1, [UpdatedAt] = SYSUTCDATETIME()
                    WHERE [TenantId] = @TenantId AND [Year] = @Year;
                    IF @@ROWCOUNT = 0 BEGIN
                        INSERT INTO [dbo].[InvoiceSequences] ([TenantId],[Year],[LastSequence],[UpdatedAt])
                        VALUES (@TenantId, @Year, 1, SYSUTCDATETIME());
                        SET @NextSequence = 1;
                    END ELSE BEGIN
                        SELECT @NextSequence = [LastSequence] FROM [dbo].[InvoiceSequences]
                        WHERE [TenantId] = @TenantId AND [Year] = @Year;
                    END
                    IF @LocalTransaction = 1 COMMIT TRANSACTION;
                END TRY
                BEGIN CATCH
                    IF @LocalTransaction = 1 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                    THROW;
                END CATCH
            END;
        ");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetNextInvoiceNumber];");
        migrationBuilder.DropTable("AuditLogs");
        migrationBuilder.DropTable("Payments");
        migrationBuilder.DropTable("InvoiceSequences");
        migrationBuilder.DropTable("Invoices");
        migrationBuilder.DropTable("Users");
        migrationBuilder.DropTable("Tenants");
    }
}
