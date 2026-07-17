using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundReversalIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_wallet_transactions_refund_reversal",
                schema: "settlement",
                table: "wallet_transactions",
                columns: new[] { "ReferenceType", "ReferenceId", "OwnerId", "Account" },
                unique: true,
                filter: "\"ReferenceType\" = 'refund'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_wallet_transactions_refund_reversal",
                schema: "settlement",
                table: "wallet_transactions");
        }
    }
}
