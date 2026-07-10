using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProviderFeeAmount",
                schema: "settlement",
                table: "seller_earnings",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProviderFeeBalance",
                schema: "settlement",
                table: "platform_wallet",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<Guid>(
                name: "SettlementBatchId",
                schema: "settlement",
                table: "payouts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderFeeAmount",
                schema: "settlement",
                table: "seller_earnings");

            migrationBuilder.DropColumn(
                name: "ProviderFeeBalance",
                schema: "settlement",
                table: "platform_wallet");

            migrationBuilder.AlterColumn<Guid>(
                name: "SettlementBatchId",
                schema: "settlement",
                table: "payouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
