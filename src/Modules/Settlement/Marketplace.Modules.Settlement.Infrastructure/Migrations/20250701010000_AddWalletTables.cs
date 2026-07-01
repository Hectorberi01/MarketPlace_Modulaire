using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddWalletTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "seller_wallets",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                PendingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                AvailableBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_seller_wallets", x => x.Id));

        migrationBuilder.CreateTable(
            name: "platform_wallet",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                CommissionBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                ShippingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_platform_wallet", x => x.Id));

        migrationBuilder.CreateTable(
            name: "withdrawals",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ProviderRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_withdrawals", x => x.Id));

        migrationBuilder.CreateTable(
            name: "wallet_transactions",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Account = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ReferenceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wallet_transactions", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_seller_wallets_SellerId",
            schema: "settlement", table: "seller_wallets", column: "SellerId", unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_withdrawals_SellerId",
            schema: "settlement", table: "withdrawals", column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_wallet_transactions_OwnerId_CreatedAtUtc",
            schema: "settlement", table: "wallet_transactions", columns: new[] { "OwnerId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "seller_wallets", schema: "settlement");
        migrationBuilder.DropTable(name: "platform_wallet", schema: "settlement");
        migrationBuilder.DropTable(name: "withdrawals", schema: "settlement");
        migrationBuilder.DropTable(name: "wallet_transactions", schema: "settlement");
    }
}
