using Microsoft.EntityFrameworkCore.Migrations;

namespace CleanArch.Infrastructure.Persistence.Migrations;

/// <summary>
/// EF Core migration that creates:
///   1. The InvoiceSequences table used by the stored procedure
///   2. The usp_GetNextInvoiceNumber stored procedure itself
///
/// Running this migration is idempotent — it drops and recreates the procedure,
/// and uses IF NOT EXISTS for the table so re-running is safe.
///
/// To apply:
///   dotnet ef database update --project src/CleanArch.Infrastructure
/// </summary>
public partial class CreateInvoiceSequencesAndStoredProcedure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── InvoiceSequences table ────────────────────────────────────────────
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.objects
                WHERE object_id = OBJECT_ID(N'[dbo].[InvoiceSequences]')
                AND   type      = N'U'
            )
            BEGIN
                CREATE TABLE [dbo].[InvoiceSequences] (
                    [TenantId]     UNIQUEIDENTIFIER NOT NULL,
                    [Year]         INT              NOT NULL,
                    [LastSequence] INT              NOT NULL CONSTRAINT DF_InvoiceSequences_LastSequence DEFAULT 0,
                    [UpdatedAt]    DATETIME2(7)     NOT NULL CONSTRAINT DF_InvoiceSequences_UpdatedAt DEFAULT GETUTCDATE(),

                    CONSTRAINT [PK_InvoiceSequences] PRIMARY KEY CLUSTERED ([TenantId] ASC, [Year] ASC)
                );
            END
        ");

        // ── Stored procedure ──────────────────────────────────────────────────
        // We use CREATE OR ALTER (SQL Server 2016+) for clean idempotent deploys.
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

                IF @@TRANCOUNT = 0
                BEGIN
                    BEGIN TRANSACTION;
                    SET @LocalTransaction = 1;
                END

                BEGIN TRY
                    UPDATE [dbo].[InvoiceSequences]
                    WITH (UPDLOCK, HOLDLOCK)
                    SET   [LastSequence] = [LastSequence] + 1,
                          [UpdatedAt]   = GETUTCDATE()
                    WHERE [TenantId] = @TenantId
                      AND [Year]     = @Year;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO [dbo].[InvoiceSequences] ([TenantId], [Year], [LastSequence], [UpdatedAt])
                        VALUES (@TenantId, @Year, 1, GETUTCDATE());

                        SET @NextSequence = 1;
                    END
                    ELSE
                    BEGIN
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
                    THROW;
                END CATCH
            END;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS [dbo].[usp_GetNextInvoiceNumber];");
        migrationBuilder.Sql("DROP TABLE IF EXISTS [dbo].[InvoiceSequences];");
    }
}
