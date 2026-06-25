using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialOrdering : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "ordering");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "ordering",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "jsonb", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "orders",
            schema: "ordering",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                CartId = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                TotalSellerDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                TotalPlatformDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                GrandTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "order_lines",
            schema: "ordering",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ShipFromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                UnitBasePrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                SellerDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                PlatformDiscount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                FinalUnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_order_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_order_lines_orders_OrderId",
                    column: x => x.OrderId,
                    principalSchema: "ordering",
                    principalTable: "orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "ordering",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_orders_BuyerId_Status",
            schema: "ordering",
            table: "orders",
            columns: new[] { "BuyerId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_order_lines_OrderId",
            schema: "ordering",
            table: "order_lines",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_order_lines_SellerId",
            schema: "ordering",
            table: "order_lines",
            column: "SellerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "ordering");
        migrationBuilder.DropTable(name: "order_lines", schema: "ordering");
        migrationBuilder.DropTable(name: "orders", schema: "ordering");
    }
}
