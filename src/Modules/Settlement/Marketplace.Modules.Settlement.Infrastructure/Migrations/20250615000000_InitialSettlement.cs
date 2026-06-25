using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialSettlement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "settlement");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "jsonb", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_outbox_messages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "seller_earnings",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                GrossAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                CommissionAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                NetAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SettlementBatchId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_seller_earnings", x => x.Id));

        migrationBuilder.CreateTable(
            name: "settlement_batches",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                TotalNet = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_settlement_batches", x => x.Id));

        migrationBuilder.CreateTable(
            name: "payouts",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                GrossAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                CommissionAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                NetAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ProviderRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SettlementBatchId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payouts", x => x.Id);
                table.ForeignKey(
                    name: "FK_payouts_settlement_batches_SettlementBatchId",
                    column: x => x.SettlementBatchId,
                    principalSchema: "settlement",
                    principalTable: "settlement_batches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "settlement", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_seller_earnings_OrderId",
            schema: "settlement", table: "seller_earnings", column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_seller_earnings_SellerId",
            schema: "settlement", table: "seller_earnings", column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_seller_earnings_Status_CreatedAtUtc",
            schema: "settlement", table: "seller_earnings", columns: new[] { "Status", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_payouts_SellerId",
            schema: "settlement", table: "payouts", column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_payouts_SettlementBatchId",
            schema: "settlement", table: "payouts", column: "SettlementBatchId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "settlement");
        migrationBuilder.DropTable(name: "seller_earnings", schema: "settlement");
        migrationBuilder.DropTable(name: "payouts", schema: "settlement");
        migrationBuilder.DropTable(name: "settlement_batches", schema: "settlement");
    }
}
