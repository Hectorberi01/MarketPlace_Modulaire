using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_withdrawals_Status_OwnerType",
                schema: "settlement",
                table: "withdrawals",
                columns: new[] { "Status", "OwnerType" });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_transactions_OwnerId_OwnerType_Account",
                schema: "settlement",
                table: "wallet_transactions",
                columns: new[] { "OwnerId", "OwnerType", "Account" })
                .Annotation("Npgsql:IndexInclude", new[] { "Direction", "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_withdrawals_Status_OwnerType",
                schema: "settlement",
                table: "withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_wallet_transactions_OwnerId_OwnerType_Account",
                schema: "settlement",
                table: "wallet_transactions");
        }
    }
}
