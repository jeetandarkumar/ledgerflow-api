using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ledgerflowApi.Infrastructure.Migrations
{
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
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    BillingAddress_Line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillingAddress_Line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillingAddress_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingAddress_State = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingAddress_CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    BillingAddress_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TrialEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FailedLoginAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BillingAddress_Line1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillingAddress_Line2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BillingAddress_City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingAddress_State = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BillingAddress_CountryCode = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    BillingAddress_PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    TaxRatePercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DiscountPercentage = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PaidCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineItems = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundedPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InitiatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Payments_RefundedPaymentId",
                        column: x => x.RefundedPaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_InitiatedByUserId",
                        column: x => x.InitiatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Entity",
                table: "AuditLogs",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_Action_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "TenantId", "Action", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_UserId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CreatedByUserId",
                table: "Invoices",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status_DueDate",
                table: "Invoices",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_CreatedAt",
                table: "Invoices",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_CustomerEmail",
                table: "Invoices",
                columns: new[] { "TenantId", "CustomerEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_Status",
                table: "Invoices",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Invoices_TenantId_InvoiceNumber",
                table: "Invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InitiatedByUserId",
                table: "Payments",
                column: "InitiatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceId",
                table: "Payments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RefundedPaymentId",
                table: "Payments",
                column: "RefundedPaymentId",
                filter: "[RefundedPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_CompletedAt_Completed",
                table: "Payments",
                columns: new[] { "TenantId", "CompletedAt" },
                filter: "[Status] = 'Completed' AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_InvoiceId_CreatedAt",
                table: "Payments",
                columns: new[] { "TenantId", "InvoiceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Payments_ExternalReference",
                table: "Payments",
                column: "ExternalReference",
                unique: true,
                filter: "[ExternalReference] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Status",
                table: "Tenants",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_IsActive",
                table: "Users",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.Sql(@"
                CREATE PROCEDURE [dbo].[usp_GetNextInvoiceNumber]
                    @TenantId   UNIQUEIDENTIFIER,
                    @Year       INT,
                    @NextSequence INT OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON; -- Automatically roll back the transaction on any error

                    -- We must be inside a transaction for UPDLOCK to provide the isolation we need.
                    -- If the caller hasn't opened one, we open our own.
                    DECLARE @LocalTransaction BIT = 0;

                    IF @@TRANCOUNT = 0
                    BEGIN
                        BEGIN TRANSACTION;
                        SET @LocalTransaction = 1;
                    END

                    BEGIN TRY
                        -- UPDLOCK: prevents other readers from also taking an update lock (eliminates race).
                        -- HOLDLOCK (= SERIALIZABLE): holds the lock until the transaction ends, not just
                        --   until the read is done. This means no other session can INSERT/UPDATE this row
                        --   between our SELECT and our UPDATE.
                        UPDATE [dbo].[InvoiceSequences]
                        WITH (UPDLOCK, HOLDLOCK)
                        SET   [LastSequence] = [LastSequence] + 1,
                              [UpdatedAt]    = GETUTCDATE()
                        WHERE [TenantId] = @TenantId
                          AND [Year]     = @Year;

                        IF @@ROWCOUNT = 0
                        BEGIN
                            -- First invoice of the year for this tenant — insert the seed row.
                            -- Sequence starts at 1.
                            INSERT INTO [dbo].[InvoiceSequences] ([TenantId], [Year], [LastSequence], [UpdatedAt])
                            VALUES (@TenantId, @Year, 1, GETUTCDATE());

                            SET @NextSequence = 1;
                        END
                        ELSE
                        BEGIN
                            -- Return the value we just set.
                            SELECT @NextSequence = [LastSequence]
                            FROM   [dbo].[InvoiceSequences]
                            WHERE  [TenantId] = @TenantId
                              AND  [Year]     = @Year;
                        END

                        IF @LocalTransaction = 1
                            COMMIT TRANSACTION;

                    END TRY
                    BEGIN CATCH
                        IF @LocalTransaction = 1 AND @@TRANCOUNT > 0
                            ROLLBACK TRANSACTION;

                        -- Re-raise the original error so the caller knows something went wrong.
                        THROW;
                    END CATCH
                END;
            ");

            migrationBuilder.Sql(@"
                CREATE PROCEDURE [dbo].[usp_CreateInvoice]
                    -- Inputs
                    @TenantId              UNIQUEIDENTIFIER,
                    @CreatedByUserId       UNIQUEIDENTIFIER,
                    @CustomerName          NVARCHAR(200),
                    @CustomerEmail         NVARCHAR(256),
                    @Currency              CHAR(3),
                    @TaxRatePercentage     DECIMAL(5,2)   = 0.00,
                    @DiscountPercentage    DECIMAL(5,2)   = 0.00,
                    @LineItemsJson         NVARCHAR(MAX),
                    @Notes                 NVARCHAR(2000) = NULL,
                    @BillingAddressJson    NVARCHAR(MAX)  = NULL,
                    @CorrelationId         NVARCHAR(100)  = NULL,

                    -- Outputs
                    @InvoiceId             UNIQUEIDENTIFIER OUTPUT,
                    @InvoiceNumber         NVARCHAR(50)     OUTPUT,
                    @ReturnCode            INT              OUTPUT,
                    @ErrorMessage          NVARCHAR(500)    OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;  -- Auto-rollback on any error

                    -- Initialise outputs
                    SET @InvoiceId    = NULL;
                    SET @InvoiceNumber = NULL;
                    SET @ReturnCode   = 0;
                    SET @ErrorMessage = NULL;

                    -- ── Step 1: Input validation (before opening the transaction) ─────────────
                    -- Cheap checks done outside the transaction to minimise lock time.

                    IF @CustomerName IS NULL OR LTRIM(RTRIM(@CustomerName)) = ''
                    BEGIN
                        SET @ReturnCode = 10; SET @ErrorMessage = 'CustomerName is required.'; RETURN;
                    END

                    IF @CustomerEmail IS NULL OR @CustomerEmail NOT LIKE '%@%'
                    BEGIN
                        SET @ReturnCode = 10; SET @ErrorMessage = 'CustomerEmail must be a valid email address.'; RETURN;
                    END

                    -- Normalise currency to uppercase and validate format
                    SET @Currency = UPPER(LTRIM(RTRIM(@Currency)));
                    IF @Currency IS NULL OR LEN(@Currency) != 3 OR @Currency LIKE '%[^A-Z]%'
                    BEGIN
                        SET @ReturnCode = 5;
                        SET @ErrorMessage = 'Currency must be a 3-character uppercase ISO 4217 code (e.g. USD, EUR, GBP).';
                        RETURN;
                    END

                    IF @TaxRatePercentage < 0 OR @TaxRatePercentage > 100
                    BEGIN
                        SET @ReturnCode = 8; SET @ErrorMessage = 'TaxRatePercentage must be between 0 and 100.'; RETURN;
                    END

                    IF @DiscountPercentage < 0 OR @DiscountPercentage > 100
                    BEGIN
                        SET @ReturnCode = 9; SET @ErrorMessage = 'DiscountPercentage must be between 0 and 100.'; RETURN;
                    END

                    IF @LineItemsJson IS NULL OR ISJSON(@LineItemsJson) = 0
                    BEGIN
                        SET @ReturnCode = 6; SET @ErrorMessage = 'LineItemsJson must be a valid JSON array.'; RETURN;
                    END

                    -- Reject empty line item arrays — an invoice with no lines has no value
                    DECLARE @LineItemCount INT = (
                        SELECT COUNT(*)
                        FROM OPENJSON(@LineItemsJson)
                    );
                    IF @LineItemCount = 0
                    BEGIN
                        SET @ReturnCode = 7; SET @ErrorMessage = 'Invoice must have at least one line item.'; RETURN;
                    END

                    IF @BillingAddressJson IS NOT NULL AND ISJSON(@BillingAddressJson) = 0
                    BEGIN
                        SET @ReturnCode = 10; SET @ErrorMessage = 'BillingAddressJson must be valid JSON when provided.'; RETURN;
                    END

                    -- ── Step 2: Business rule validation ──────────────────────────────────────
                    DECLARE
                        @TenantStatus    NVARCHAR(20),
                        @TenantName      NVARCHAR(200),
                        @UserTenantId    UNIQUEIDENTIFIER,
                        @UserIsActive    BIT;

                    SELECT
                        @TenantStatus = [Status],
                        @TenantName   = [Name]
                    FROM [dbo].[Tenants]
                    WHERE [Id] = @TenantId
                      AND [IsDeleted] = 0;

                    IF @TenantStatus IS NULL
                    BEGIN
                        SET @ReturnCode = 1; SET @ErrorMessage = 'Tenant not found.'; RETURN;
                    END

                    IF @TenantStatus IN ('Suspended', 'Cancelled')
                    BEGIN
                        SET @ReturnCode = 2;
                        SET @ErrorMessage = 'Tenant is ' + @TenantStatus + ' and cannot create new invoices.';
                        RETURN;
                    END

                    SELECT
                        @UserTenantId = [TenantId],
                        @UserIsActive = [IsActive]
                    FROM [dbo].[Users]
                    WHERE [Id] = @CreatedByUserId
                      AND [IsDeleted] = 0;

                    IF @UserTenantId IS NULL
                    BEGIN
                        SET @ReturnCode = 3; SET @ErrorMessage = 'User not found.'; RETURN;
                    END

                    IF @UserTenantId != @TenantId OR @UserIsActive = 0
                    BEGIN
                        SET @ReturnCode = 4;
                        SET @ErrorMessage = 'User is inactive or does not belong to the specified tenant.';
                        RETURN;
                    END

                    -- ── Step 3: Open transaction and do the work ───────────────────────────────
                    BEGIN TRANSACTION;
                    BEGIN TRY

                        -- Generate invoice number: INV-{YEAR}-{SEQUENCE:000000}
                        -- The nested proc uses UPDLOCK + HOLDLOCK to make this race-condition-safe.
                        DECLARE @NextSequence INT;
                        DECLARE @CurrentYear  INT = YEAR(SYSUTCDATETIME());

                        EXEC [dbo].[usp_GetNextInvoiceNumber]
                            @TenantId     = @TenantId,
                            @Year         = @CurrentYear,
                            @NextSequence = @NextSequence OUTPUT;

                        SET @InvoiceNumber = 'INV-' + CAST(@CurrentYear AS NVARCHAR(4))
                                           + '-' + RIGHT('000000' + CAST(@NextSequence AS NVARCHAR(6)), 6);

                        -- Create the invoice ID (application-style GUID, random for security)
                        SET @InvoiceId = NEWID();

                        -- Parse optional billing address fields from JSON
                        DECLARE
                            @BA_Line1       NVARCHAR(200) = NULL,
                            @BA_Line2       NVARCHAR(200) = NULL,
                            @BA_City        NVARCHAR(100) = NULL,
                            @BA_State       NVARCHAR(100) = NULL,
                            @BA_CountryCode CHAR(2)       = NULL,
                            @BA_PostalCode  NVARCHAR(20)  = NULL;

                        IF @BillingAddressJson IS NOT NULL
                        BEGIN
                            SELECT
                                @BA_Line1       = JSON_VALUE(@BillingAddressJson, '$.line1'),
                                @BA_Line2       = JSON_VALUE(@BillingAddressJson, '$.line2'),
                                @BA_City        = JSON_VALUE(@BillingAddressJson, '$.city'),
                                @BA_State       = JSON_VALUE(@BillingAddressJson, '$.state'),
                                @BA_CountryCode = UPPER(JSON_VALUE(@BillingAddressJson, '$.countryCode')),
                                @BA_PostalCode  = JSON_VALUE(@BillingAddressJson, '$.postalCode');
                        END

                        -- Insert the invoice
                        INSERT INTO [dbo].[Invoices] (
                            [Id],
                            [TenantId],
                            [InvoiceNumber],
                            [CustomerName],
                            [CustomerEmail],
                            [Status],
                            [Currency],
                            [TaxRatePercentage],
                            [DiscountPercentage],
                            [PaidAmount],
                            [PaidCurrency],
                            [LineItems],
                            [Notes],
                            [CreatedByUserId],
                            [BillingAddress_Line1],
                            [BillingAddress_Line2],
                            [BillingAddress_City],
                            [BillingAddress_State],
                            [BillingAddress_CountryCode],
                            [BillingAddress_PostalCode],
                            [CreatedAt],
                            [IsDeleted]
                        )
                        VALUES (
                            @InvoiceId,
                            @TenantId,
                            @InvoiceNumber,
                            LTRIM(RTRIM(@CustomerName)),
                            LOWER(LTRIM(RTRIM(@CustomerEmail))),
                            'Draft',                -- Always starts as Draft
                            @Currency,
                            @TaxRatePercentage,
                            @DiscountPercentage,
                            0.0000,                 -- No payments yet
                            @Currency,              -- PaidCurrency must match Currency
                            @LineItemsJson,
                            @Notes,
                            @CreatedByUserId,
                            @BA_Line1,
                            @BA_Line2,
                            @BA_City,
                            @BA_State,
                            @BA_CountryCode,
                            @BA_PostalCode,
                            SYSUTCDATETIME(),
                            0                       -- Not deleted
                        );

                        -- Insert audit log entry
                        -- Captures the user, timestamp, and a JSON snapshot of the invoice header.
                        DECLARE @AuditStateAfter NVARCHAR(MAX) = (
                            SELECT
                                @InvoiceId    AS invoiceId,
                                @InvoiceNumber AS invoiceNumber,
                                @TenantId     AS tenantId,
                                @CustomerName AS customerName,
                                @CustomerEmail AS customerEmail,
                                @Currency     AS currency,
                                'Draft'       AS status,
                                @TaxRatePercentage   AS taxRatePercentage,
                                @DiscountPercentage  AS discountPercentage,
                                @LineItemCount       AS lineItemCount
                            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                        );

                        INSERT INTO [dbo].[AuditLogs] (
                            [Id],
                            [TenantId],
                            [UserId],
                            [Action],
                            [EntityType],
                            [EntityId],
                            [Description],
                            [StateAfter],
                            [CorrelationId],
                            [CreatedAt]
                        )
                        VALUES (
                            NEWID(),
                            @TenantId,
                            @CreatedByUserId,
                            'Created',
                            'Invoice',
                            @InvoiceId,
                            'Invoice ' + @InvoiceNumber + ' created as Draft for customer ' + @CustomerEmail + '.',
                            @AuditStateAfter,
                            @CorrelationId,
                            SYSUTCDATETIME()
                        );

                        COMMIT TRANSACTION;

                        SET @ReturnCode   = 0;
                        SET @ErrorMessage = NULL;

                    END TRY
                    BEGIN CATCH
                        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

                        SET @ReturnCode   = 99;
                        SET @ErrorMessage = 'Unexpected error: ' + ERROR_MESSAGE()
                                          + ' (Line ' + CAST(ERROR_LINE() AS NVARCHAR(10)) + ')';

                        -- Re-raise so the calling layer can log it with full context
                        THROW;
                    END CATCH
                END;
            ");

            migrationBuilder.Sql(@"
                CREATE PROCEDURE [dbo].[usp_ProcessPayment]
                    -- Inputs
                    @TenantId             UNIQUEIDENTIFIER,
                    @InvoiceId            UNIQUEIDENTIFIER,
                    @Amount               DECIMAL(18,4),
                    @Currency             CHAR(3),
                    @PaymentMethod        NVARCHAR(50),
                    @ExternalReference    NVARCHAR(200)    = NULL,
                    @Type                 NVARCHAR(20)     = 'Standard',
                    @RefundedPaymentId    UNIQUEIDENTIFIER = NULL,
                    @InitiatedByUserId    UNIQUEIDENTIFIER = NULL,
                    @Notes                NVARCHAR(1000)   = NULL,
                    @CorrelationId        NVARCHAR(100)    = NULL,

                    -- Outputs
                    @PaymentId            UNIQUEIDENTIFIER OUTPUT,
                    @NewInvoiceStatus     NVARCHAR(20)     OUTPUT,
                    @OutstandingAmount    DECIMAL(18,4)    OUTPUT,
                    @ReturnCode           INT              OUTPUT,
                    @ErrorMessage         NVARCHAR(500)    OUTPUT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    -- Initialise outputs
                    SET @PaymentId         = NULL;
                    SET @NewInvoiceStatus  = NULL;
                    SET @OutstandingAmount = NULL;
                    SET @ReturnCode        = 0;
                    SET @ErrorMessage      = NULL;

                    -- ── Input validation (outside the transaction) ────────────────────────────

                    IF @Amount IS NULL OR @Amount <= 0
                    BEGIN
                        SET @ReturnCode = 9; SET @ErrorMessage = 'Amount must be greater than zero.'; RETURN;
                    END

                    SET @Currency = UPPER(LTRIM(RTRIM(@Currency)));
                    IF @Currency IS NULL OR LEN(@Currency) != 3 OR @Currency LIKE '%[^A-Z]%'
                    BEGIN
                        SET @ReturnCode = 10; SET @ErrorMessage = 'Currency must be a 3-character uppercase ISO 4217 code.'; RETURN;
                    END

                    IF @Type NOT IN ('Standard', 'Refund')
                    BEGIN
                        SET @ReturnCode = 10; SET @ErrorMessage = 'Type must be ''Standard'' or ''Refund''.'; RETURN;
                    END

                    IF @Type = 'Refund' AND @RefundedPaymentId IS NULL
                    BEGIN
                        SET @ReturnCode = 6; SET @ErrorMessage = 'RefundedPaymentId is required for refund payments.'; RETURN;
                    END

                    IF @PaymentMethod IS NULL OR LTRIM(RTRIM(@PaymentMethod)) = ''
                    BEGIN
                        SET @ReturnCode = 10; SET @ErrorMessage = 'PaymentMethod is required.'; RETURN;
                    END

                    -- ── Load and validate the invoice ─────────────────────────────────────────
                    DECLARE
                        @InvoiceTenantId         UNIQUEIDENTIFIER,
                        @InvoiceCurrency         CHAR(3),
                        @InvoiceStatus           NVARCHAR(20),
                        @InvoiceNumber           NVARCHAR(50),
                        @InvoicePaidAmount       DECIMAL(18,4),
                        @InvoiceTaxRate          DECIMAL(5,2),
                        @InvoiceDiscount         DECIMAL(5,2),
                        @InvoiceLineItemsJson    NVARCHAR(MAX),
                        @InvoiceDueDate          DATETIME2(7);

                    SELECT
                        @InvoiceTenantId      = [TenantId],
                        @InvoiceCurrency      = [Currency],
                        @InvoiceStatus        = [Status],
                        @InvoiceNumber        = [InvoiceNumber],
                        @InvoicePaidAmount    = [PaidAmount],
                        @InvoiceTaxRate       = [TaxRatePercentage],
                        @InvoiceDiscount      = [DiscountPercentage],
                        @InvoiceLineItemsJson = [LineItems],
                        @InvoiceDueDate       = [DueDate]
                    FROM [dbo].[Invoices]
                    WHERE [Id] = @InvoiceId
                      AND [IsDeleted] = 0;

                    IF @InvoiceTenantId IS NULL
                    BEGIN
                        SET @ReturnCode = 1; SET @ErrorMessage = 'Invoice not found.'; RETURN;
                    END

                    IF @InvoiceTenantId != @TenantId
                    BEGIN
                        SET @ReturnCode = 2; SET @ErrorMessage = 'Payment tenant does not match invoice tenant.'; RETURN;
                    END

                    -- Payable statuses: Issued, PartiallyPaid, Overdue
                    -- Draft invoices haven't been sent; Paid and Voided are terminal.
                    IF @InvoiceStatus IN ('Draft', 'Paid', 'Voided')
                    BEGIN
                        SET @ReturnCode = 3;
                        SET @ErrorMessage = 'Invoice cannot accept payments in status ''' + @InvoiceStatus + '''. '
                            + CASE @InvoiceStatus
                                WHEN 'Draft'  THEN 'Issue the invoice first.'
                                WHEN 'Paid'   THEN 'This invoice is already paid in full.'
                                WHEN 'Voided' THEN 'This invoice has been voided.'
                              END;
                        RETURN;
                    END

                    IF @InvoiceCurrency != @Currency
                    BEGIN
                        SET @ReturnCode = 4;
                        SET @ErrorMessage = 'Payment currency (' + @Currency + ') does not match invoice currency ('
                                          + @InvoiceCurrency + ').';
                        RETURN;
                    END

                    -- ── Calculate TotalAmount from the line items JSON ────────────────────────
                    -- This exactly mirrors the domain model computation:
                    --   NetAmount per line = UnitPrice × Quantity × (1 − LineDiscount/100)
                    --   Subtotal           = SUM of all NetAmounts
                    --   DiscountedSubtotal = Subtotal × (1 − InvoiceDiscount/100)
                    --   TaxAmount          = DiscountedSubtotal × (TaxRate/100)
                    --   TotalAmount        = DiscountedSubtotal + TaxAmount

                    DECLARE @Subtotal            DECIMAL(18,4);
                    DECLARE @DiscountedSubtotal  DECIMAL(18,4);
                    DECLARE @TaxAmount           DECIMAL(18,4);
                    DECLARE @TotalAmount         DECIMAL(18,4);
                    DECLARE @OutstandingBefore   DECIMAL(18,4);

                    SELECT @Subtotal = SUM(
                        CAST(JSON_VALUE(li.[value], '$.unitPrice') AS DECIMAL(18,4))
                        * CAST(JSON_VALUE(li.[value], '$.quantity') AS DECIMAL(18,4))
                        * (1 - CAST(ISNULL(JSON_VALUE(li.[value], '$.discountPercentage'), '0') AS DECIMAL(5,2)) / 100.0)
                    )
                    FROM OPENJSON(@InvoiceLineItemsJson) AS li;

                    SET @Subtotal           = ISNULL(@Subtotal, 0);
                    SET @DiscountedSubtotal = @Subtotal * (1 - @InvoiceDiscount / 100.0);
                    SET @TaxAmount          = @DiscountedSubtotal * (@InvoiceTaxRate / 100.0);
                    SET @TotalAmount        = @DiscountedSubtotal + @TaxAmount;
                    SET @OutstandingBefore  = @TotalAmount - @InvoicePaidAmount;

                    -- ── Payment-type-specific validation ──────────────────────────────────────

                    IF @Type = 'Standard'
                    BEGIN
                        IF @Amount > @OutstandingBefore
                        BEGIN
                            SET @ReturnCode = 5;
                            SET @ErrorMessage = 'Payment of ' + CAST(@Amount AS NVARCHAR(20))
                                + ' ' + @Currency + ' exceeds the outstanding balance of '
                                + CAST(@OutstandingBefore AS NVARCHAR(20)) + ' ' + @Currency + '.';
                            RETURN;
                        END
                    END

                    IF @Type = 'Refund'
                    BEGIN
                        -- Validate the original payment
                        DECLARE
                            @OrigPayStatus NVARCHAR(20),
                            @OrigPayAmount DECIMAL(18,4);

                        SELECT
                            @OrigPayStatus = [Status],
                            @OrigPayAmount = [Amount]
                        FROM [dbo].[Payments]
                        WHERE [Id]        = @RefundedPaymentId
                          AND [InvoiceId] = @InvoiceId
                          AND [TenantId]  = @TenantId
                          AND [IsDeleted] = 0;

                        IF @OrigPayStatus IS NULL
                        BEGIN
                            SET @ReturnCode = 7;
                            SET @ErrorMessage = 'Original payment not found on this invoice, '
                                              + 'or it belongs to a different tenant.';
                            RETURN;
                        END

                        IF @OrigPayStatus != 'Completed'
                        BEGIN
                            SET @ReturnCode = 7;
                            SET @ErrorMessage = 'Only Completed payments can be refunded. '
                                + 'Original payment status: ' + @OrigPayStatus + '.';
                            RETURN;
                        END

                        -- Can't refund more than the original payment captured
                        IF @Amount > @OrigPayAmount
                        BEGIN
                            SET @ReturnCode = 8;
                            SET @ErrorMessage = 'Refund of ' + CAST(@Amount AS NVARCHAR(20))
                                + ' exceeds original payment of ' + CAST(@OrigPayAmount AS NVARCHAR(20)) + '.';
                            RETURN;
                        END

                        -- Can't refund more than has been paid on the invoice
                        IF @Amount > @InvoicePaidAmount
                        BEGIN
                            SET @ReturnCode = 8;
                            SET @ErrorMessage = 'Refund of ' + CAST(@Amount AS NVARCHAR(20))
                                + ' exceeds total paid amount of ' + CAST(@InvoicePaidAmount AS NVARCHAR(20))
                                + ' on this invoice.';
                            RETURN;
                        END
                    END

                    -- ── Open transaction and write ─────────────────────────────────────────────
                    BEGIN TRANSACTION;
                    BEGIN TRY

                        SET @PaymentId = NEWID();

                        -- Insert the Payment record
                        INSERT INTO [dbo].[Payments] (
                            [Id],
                            [TenantId],
                            [InvoiceId],
                            [RefundedPaymentId],
                            [Amount],
                            [Currency],
                            [Status],
                            [Type],
                            [PaymentMethod],
                            [ExternalReference],
                            [Notes],
                            [CompletedAt],
                            [InitiatedByUserId],
                            [CreatedAt],
                            [IsDeleted]
                        )
                        VALUES (
                            @PaymentId,
                            @TenantId,
                            @InvoiceId,
                            @RefundedPaymentId,
                            @Amount,
                            @Currency,
                            'Completed',            -- Inserted directly as Completed
                            @Type,                  -- 'Standard' or 'Refund'
                            @PaymentMethod,
                            @ExternalReference,
                            @Notes,
                            SYSUTCDATETIME(),       -- CompletedAt
                            @InitiatedByUserId,
                            SYSUTCDATETIME(),
                            0
                        );

                        -- Compute new paid amount on the invoice
                        DECLARE @NewPaidAmount DECIMAL(18,4);

                        SET @NewPaidAmount = CASE @Type
                            WHEN 'Standard' THEN @InvoicePaidAmount + @Amount
                            WHEN 'Refund'   THEN @InvoicePaidAmount - @Amount
                        END;

                        -- Derive the new invoice status from the updated amounts
                        DECLARE @IsPastDue BIT = CASE
                            WHEN @InvoiceDueDate IS NOT NULL AND @InvoiceDueDate < SYSUTCDATETIME() THEN 1
                            ELSE 0
                        END;

                        SET @NewInvoiceStatus = CASE
                            WHEN @NewPaidAmount >= @TotalAmount       THEN 'Paid'
                            WHEN @NewPaidAmount > 0 AND @IsPastDue = 1 THEN 'Overdue'
                            WHEN @NewPaidAmount > 0                   THEN 'PartiallyPaid'
                            WHEN @IsPastDue = 1                       THEN 'Overdue'
                            ELSE                                           'Issued'
                        END;

                        DECLARE @NewPaidAt DATETIME2(7) = CASE
                            WHEN @NewInvoiceStatus = 'Paid' THEN SYSUTCDATETIME()
                            ELSE NULL
                        END;

                        SET @OutstandingAmount = @TotalAmount - @NewPaidAmount;

                        -- Capture the invoice before-state for the audit log
                        DECLARE @AuditStateBefore NVARCHAR(MAX) = (
                            SELECT
                                @InvoiceStatus     AS status,
                                @InvoicePaidAmount AS paidAmount,
                                @OutstandingBefore AS outstandingAmount,
                                @TotalAmount       AS totalAmount,
                                @Currency          AS currency
                            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                        );

                        -- Update the Invoice
                        UPDATE [dbo].[Invoices]
                        SET
                            [PaidAmount] = @NewPaidAmount,
                            [Status]     = @NewInvoiceStatus,
                            [PaidAt]     = @NewPaidAt,
                            [UpdatedAt]  = SYSUTCDATETIME()
                        WHERE [Id] = @InvoiceId;

                        -- Capture the invoice after-state
                        DECLARE @AuditStateAfter NVARCHAR(MAX) = (
                            SELECT
                                @NewInvoiceStatus  AS status,
                                @NewPaidAmount     AS paidAmount,
                                @OutstandingAmount AS outstandingAmount,
                                @TotalAmount       AS totalAmount,
                                @Currency          AS currency
                            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                        );

                        -- Insert AuditLog entry
                        DECLARE @AuditAction      NVARCHAR(30)   = CASE @Type WHEN 'Refund' THEN 'PaymentRefunded' ELSE 'PaymentReceived' END;
                        DECLARE @AuditDescription NVARCHAR(1000) = CASE @Type
                            WHEN 'Refund'
                                THEN 'Refund of ' + CAST(@Amount AS NVARCHAR(20)) + ' ' + @Currency
                                   + ' applied to invoice ' + @InvoiceNumber
                                   + '. New status: ' + @NewInvoiceStatus + '.'
                            ELSE
                                'Payment of ' + CAST(@Amount AS NVARCHAR(20)) + ' ' + @Currency
                                   + ' received for invoice ' + @InvoiceNumber
                                   + '. New status: ' + @NewInvoiceStatus + '.'
                            END;

                        DECLARE @AuditMetadata NVARCHAR(MAX) = (
                            SELECT
                                @PaymentId        AS paymentId,
                                @Amount           AS amount,
                                @Currency         AS currency,
                                @PaymentMethod    AS paymentMethod,
                                @ExternalReference AS externalReference,
                                @Type             AS paymentType
                            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
                        );

                        INSERT INTO [dbo].[AuditLogs] (
                            [Id],
                            [TenantId],
                            [UserId],
                            [Action],
                            [EntityType],
                            [EntityId],
                            [Description],
                            [StateBefore],
                            [StateAfter],
                            [CorrelationId],
                            [Metadata],
                            [CreatedAt]
                        )
                        VALUES (
                            NEWID(),
                            @TenantId,
                            @InitiatedByUserId,
                            @AuditAction,
                            'Invoice',
                            @InvoiceId,
                            @AuditDescription,
                            @AuditStateBefore,
                            @AuditStateAfter,
                            @CorrelationId,
                            @AuditMetadata,
                            SYSUTCDATETIME()
                        );

                        COMMIT TRANSACTION;

                        SET @ReturnCode   = 0;
                        SET @ErrorMessage = NULL;

                    END TRY
                    BEGIN CATCH
                        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;

                        SET @ReturnCode   = 99;
                        SET @ErrorMessage = 'Unexpected error: ' + ERROR_MESSAGE()
                                          + ' (Line ' + CAST(ERROR_LINE() AS NVARCHAR(10)) + ')';
                        THROW;
                    END CATCH
                END;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER [dbo].[trg_Invoices_AfterUpdate]
                    ON  [dbo].[Invoices]
                    AFTER UPDATE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        SET XACT_ABORT ON;

                        -- Skip if no rows were actually affected (can happen with filtered updates)
                        IF @@ROWCOUNT = 0 RETURN;

                        -- One audit entry per modified row
                        INSERT INTO [dbo].[AuditLogs] (
                            [Id],
                            [TenantId],
                            [UserId],
                            [UserDisplayName],
                            [Action],
                            [EntityType],
                            [EntityId],
                            [Description],
                            [StateBefore],
                            [StateAfter],
                            [CreatedAt]
                        )
                        SELECT
                            NEWID(),
                            i.[TenantId],
                            NULL,                                   -- App-layer identity not available in trigger
                            'DB Trigger',
                            -- If only IsDeleted changed, use 'Deleted'; if Status changed, 'StatusChanged'; else 'Updated'
                            CASE
                                WHEN i.[IsDeleted] = 1 AND d.[IsDeleted] = 0 THEN 'Deleted'
                                WHEN i.[Status]    != d.[Status]             THEN 'StatusChanged'
                                ELSE 'Updated'
                            END,
                            'Invoice',
                            i.[Id],
                            -- Description adapts to what changed
                            CASE
                                WHEN i.[IsDeleted] = 1 AND d.[IsDeleted] = 0
                                    THEN 'Invoice ' + i.[InvoiceNumber] + ' soft-deleted.'
                                WHEN i.[Status] != d.[Status]
                                    THEN 'Invoice ' + i.[InvoiceNumber]
                                       + ' status changed from ''' + d.[Status]
                                       + ''' to ''' + i.[Status] + '''.'
                                ELSE 'Invoice ' + i.[InvoiceNumber] + ' updated.'
                            END,
                            -- Before state (from DELETED pseudo-table)
                            (SELECT
                                d.[InvoiceNumber]     AS invoiceNumber,
                                d.[Status]            AS status,
                                d.[PaidAmount]        AS paidAmount,
                                d.[DiscountPercentage] AS discountPercentage,
                                d.[TaxRatePercentage]  AS taxRatePercentage,
                                d.[IsDeleted]         AS isDeleted
                             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            -- After state (from INSERTED pseudo-table)
                            (SELECT
                                i.[InvoiceNumber]     AS invoiceNumber,
                                i.[Status]            AS status,
                                i.[PaidAmount]        AS paidAmount,
                                i.[DiscountPercentage] AS discountPercentage,
                                i.[TaxRatePercentage]  AS taxRatePercentage,
                                i.[IsDeleted]         AS isDeleted
                             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            SYSUTCDATETIME()
                        FROM INSERTED AS i
                        INNER JOIN DELETED AS d ON i.[Id] = d.[Id];

                    END;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER [dbo].[trg_Invoices_AfterDelete]
                    ON  [dbo].[Invoices]
                    AFTER DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        SET XACT_ABORT ON;

                        IF @@ROWCOUNT = 0 RETURN;

                        INSERT INTO [dbo].[AuditLogs] (
                            [Id], [TenantId], [UserId], [UserDisplayName],
                            [Action], [EntityType], [EntityId],
                            [Description], [StateBefore], [CreatedAt]
                        )
                        SELECT
                            NEWID(),
                            d.[TenantId],
                            NULL,
                            'DB Trigger — HARD DELETE',
                            'Deleted',
                            'Invoice',
                            d.[Id],
                            'HARD DELETE of Invoice ' + d.[InvoiceNumber]
                                + ' (TenantId: ' + CAST(d.[TenantId] AS NVARCHAR(36)) + '). '
                                + 'Hard deletes should not occur — investigate immediately.',
                            (SELECT
                                d.[Id]            AS id,
                                d.[InvoiceNumber] AS invoiceNumber,
                                d.[Status]        AS status,
                                d.[CustomerEmail] AS customerEmail,
                                d.[Currency]      AS currency,
                                d.[PaidAmount]    AS paidAmount,
                                d.[CreatedAt]     AS createdAt
                             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            SYSUTCDATETIME()
                        FROM DELETED AS d;

                        -- Raise a warning so any monitoring that watches for ERROR severity picks this up
                        RAISERROR('WARNING: Hard delete performed on Invoices table. See AuditLogs for details.', 16, 1);
                    END;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER [dbo].[trg_Payments_AfterUpdate]
                    ON  [dbo].[Payments]
                    AFTER UPDATE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        SET XACT_ABORT ON;

                        IF @@ROWCOUNT = 0 RETURN;

                        INSERT INTO [dbo].[AuditLogs] (
                            [Id], [TenantId], [UserId], [UserDisplayName],
                            [Action], [EntityType], [EntityId],
                            [Description], [StateBefore], [StateAfter], [CreatedAt]
                        )
                        SELECT
                            NEWID(),
                            i.[TenantId],
                            NULL,
                            'DB Trigger',
                            CASE
                                WHEN i.[Status] != d.[Status] THEN 'StatusChanged'
                                ELSE 'Updated'
                            END,
                            'Payment',
                            i.[Id],
                            CASE
                                WHEN i.[Status] != d.[Status]
                                    THEN 'Payment status changed from ''' + d.[Status] + ''' to ''' + i.[Status] + ''''
                                       + ' on invoice ' + CAST(i.[InvoiceId] AS NVARCHAR(36)) + '.'
                                ELSE 'Payment ' + CAST(i.[Id] AS NVARCHAR(36)) + ' updated.'
                            END,
                            (SELECT
                                d.[Status]    AS status,
                                d.[Amount]    AS amount,
                                d.[Currency]  AS currency,
                                d.[IsDeleted] AS isDeleted
                             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            (SELECT
                                i.[Status]    AS status,
                                i.[Amount]    AS amount,
                                i.[Currency]  AS currency,
                                i.[IsDeleted] AS isDeleted
                             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            SYSUTCDATETIME()
                        FROM INSERTED AS i
                        INNER JOIN DELETED AS d ON i.[Id] = d.[Id];

                    END;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER [dbo].[trg_Users_AfterUpdate]
                    ON  [dbo].[Users]
                    AFTER UPDATE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        SET XACT_ABORT ON;

                        IF @@ROWCOUNT = 0 RETURN;

                        INSERT INTO [dbo].[AuditLogs] (
                            [Id], [TenantId], [UserId], [UserDisplayName],
                            [Action], [EntityType], [EntityId],
                            [Description], [StateBefore], [StateAfter], [CreatedAt]
                        )
                        SELECT
                            NEWID(),
                            i.[TenantId],
                            i.[Id],
                            'DB Trigger',
                            CASE
                                WHEN i.[IsDeleted] = 1 AND d.[IsDeleted] = 0 THEN 'Deleted'
                                ELSE 'Updated'
                            END,
                            'User',
                            i.[Id],
                            CASE
                                WHEN i.[IsDeleted] = 1 AND d.[IsDeleted] = 0
                                    THEN 'User ' + i.[Email] + ' soft-deleted.'
                                WHEN i.[Role] <> d.[Role]
                                    THEN 'User ' + i.[Email] + ' role changed from '
                                        + d.[Role] + ' to ' + i.[Role] + '.'
                                WHEN i.[IsActive] <> d.[IsActive]
                                    THEN 'User ' + i.[Email]
                                        + CASE i.[IsActive] WHEN 1 THEN ' reactivated.' ELSE ' deactivated.' END
                                WHEN i.[LockedUntil] IS NOT NULL AND d.[LockedUntil] IS NULL
                                    THEN 'User ' + i.[Email] + ' account locked until '
                                        + CONVERT(NVARCHAR(30), i.[LockedUntil], 126) + '.'
                                ELSE 'User ' + i.[Email] + ' profile updated.'
                            END,
                            (SELECT
                                d.[Email]      AS email,
                                d.[Role]       AS role,
                                d.[IsActive]   AS isActive,
                                d.[LockedUntil] AS lockedUntil,
                                d.[IsDeleted]  AS isDeleted
                                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            (SELECT
                                i.[Email]      AS email,
                                i.[Role]       AS role,
                                i.[IsActive]   AS isActive,
                                i.[LockedUntil] AS lockedUntil,
                                i.[IsDeleted]  AS isDeleted
                                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            SYSUTCDATETIME()
                        FROM INSERTED AS i
                        INNER JOIN DELETED AS d ON i.[Id] = d.[Id]
                        WHERE i.[Role]        <> d.[Role]
                            OR i.[IsActive]    <> d.[IsActive]
                            OR i.[IsDeleted]   <> d.[IsDeleted]
                            OR i.[Email]       <> d.[Email]
                            -- FIXED NULL COMPARISON LOGIC BELOW
                            OR (i.[LockedUntil] IS NULL AND d.[LockedUntil] IS NOT NULL)
                            OR (i.[LockedUntil] IS NOT NULL AND d.[LockedUntil] IS NULL);
                    END;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER [dbo].[trg_Tenants_AfterUpdate]
                    ON  [dbo].[Tenants]
                    AFTER UPDATE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        SET XACT_ABORT ON;

                        IF @@ROWCOUNT = 0 RETURN;

                        INSERT INTO [dbo].[AuditLogs] (
                            [Id], [TenantId], [UserId], [UserDisplayName],
                            [Action], [EntityType], [EntityId],
                            [Description], [StateBefore], [StateAfter], [CreatedAt]
                        )
                        SELECT
                            NEWID(),
                            i.[Id],             -- For Tenant audits, TenantId = the tenant's own Id
                            NULL,
                            'DB Trigger',
                            CASE
                                WHEN i.[Status] != d.[Status] THEN 'StatusChanged'
                                ELSE 'Updated'
                            END,
                            'Tenant',
                            i.[Id],
                            CASE
                                WHEN i.[Status] != d.[Status]
                                    THEN 'Tenant ''' + i.[Name] + ''' status changed from '''
                                       + d.[Status] + ''' to ''' + i.[Status] + '''.'
                                ELSE 'Tenant ''' + i.[Name] + ''' profile updated.'
                            END,
                            (SELECT
                                d.[Name]           AS name,
                                d.[Status]         AS status,
                                d.[DefaultCurrency] AS defaultCurrency,
                                d.[BillingEmail]   AS billingEmail
                             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            (SELECT
                                i.[Name]           AS name,
                                i.[Status]         AS status,
                                i.[DefaultCurrency] AS defaultCurrency,
                                i.[BillingEmail]   AS billingEmail
                             FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                            SYSUTCDATETIME()
                        FROM INSERTED AS i
                        INNER JOIN DELETED AS d ON i.[Id] = d.[Id]
                        WHERE i.[Status]          != d.[Status]
                           OR i.[Name]            != d.[Name]
                           OR i.[BillingEmail]    != d.[BillingEmail]
                           OR i.[DefaultCurrency] != d.[DefaultCurrency]
                           OR i.[IsDeleted]       != d.[IsDeleted];

                    END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_Invoices_AfterUpdate");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_Invoices_AfterDelete");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_Payments_AfterUpdate");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_Users_AfterUpdate");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_Tenants_AfterUpdate");

            // 2. Drop the Stored Procedures
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS usp_GetNextInvoiceNumber");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS usp_CreateInvoice");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS usp_ProcessPayment");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "InvoiceSequences");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
