-- =============================================================================
-- usp_GetNextInvoiceNumber
-- =============================================================================
-- Purpose: Returns the next invoice sequence number for a given tenant,
--          scoped to the current calendar year.
--
-- Why a stored procedure (not application-side logic):
--   If we read the current MAX sequence in C# and increment in memory,
--   two concurrent requests can read the same MAX and generate the same number.
--   The procedure uses UPDLOCK + HOLDLOCK (serializable read) to make the
--   read-then-increment operation atomic, eliminating that race condition.
--
-- Why a dedicated InvoiceSequences table (not MAX(InvoiceNumber)):
--   Parsing and incrementing a number from a string column is fragile and slow.
--   A dedicated sequence table with a simple integer is clean, fast, and
--   never affected by soft-deleted or rolled-back invoices creating gaps
--   in the MAX() value.
--
-- Gap behaviour:
--   If an invoice transaction rolls back after calling this procedure,
--   the sequence number is consumed (a gap will appear). This is acceptable —
--   gaps in invoice numbers are normal in financial systems and not a compliance
--   issue in any major jurisdiction. What matters is uniqueness, not continuity.
--
-- Usage:
--   DECLARE @NextSeq INT
--   EXEC usp_GetNextInvoiceNumber @TenantId = '...', @Year = 2024, @NextSequence = @NextSeq OUTPUT
-- =============================================================================

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_GetNextInvoiceNumber]') AND type = 'P')
    DROP PROCEDURE [dbo].[usp_GetNextInvoiceNumber];
GO

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
GO

-- =============================================================================
-- InvoiceSequences table
-- =============================================================================
-- Must exist before the stored procedure is called.
-- One row per (TenantId, Year) pair. Created by migration, shown here for reference.
-- =============================================================================

/*
CREATE TABLE [dbo].[InvoiceSequences] (
    [TenantId]      UNIQUEIDENTIFIER NOT NULL,
    [Year]          INT              NOT NULL,
    [LastSequence]  INT              NOT NULL DEFAULT 0,
    [UpdatedAt]     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_InvoiceSequences] PRIMARY KEY CLUSTERED ([TenantId], [Year])
);
*/
