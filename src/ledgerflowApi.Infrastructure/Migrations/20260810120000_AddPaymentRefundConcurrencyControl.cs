using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ledgerflowApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefundConcurrencyControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Running total of refunds already applied against this payment. Backfilled to
            // zero for every existing row — for pre-existing Standard payments this correctly
            // starts the count at zero (any refunds already recorded against them under the
            // old model are separate Payment rows and are not retroactively summed here; the
            // guard only applies to refunds processed after this migration).
            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "Payments",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RefundedAmountCurrency",
                table: "Payments",
                type: "nchar(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            // Backfill RefundedAmountCurrency from each row's own Currency, since a single
            // fixed default (e.g. "USD") would be wrong for non-USD payments. Must run before
            // the NOT NULL/default-value constraint above is relied upon for new inserts.
            migrationBuilder.Sql(@"
                UPDATE Payments
                SET RefundedAmountCurrency = Currency
                WHERE RefundedAmountCurrency = '';
            ");

            // SQL Server ROWVERSION — an 8-byte value the database itself increments on every
            // UPDATE to the row. Used as an EF Core concurrency token so two refunds racing
            // against the same original payment can't both succeed (see Payment.ApplyRefund
            // and Payment.RowVersion for the full explanation).
            //
            // No defaultValue here deliberately: SQL Server auto-populates every existing row
            // with a valid rowversion when the column is added, and rejects an explicit DEFAULT
            // constraint on a rowversion/timestamp column.
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Payments",
                type: "rowversion",
                rowVersion: true,
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundedAmountCurrency",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Payments");
        }
    }
}
