using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Cart.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCart : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "cart");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "cart",
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
            name: "carts",
            schema: "cart",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_carts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "cart_items",
            schema: "cart",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ShipFromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                UnitBaseAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                CartId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cart_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_cart_items_carts_CartId",
                    column: x => x.CartId,
                    principalSchema: "cart",
                    principalTable: "carts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "cart",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_carts_BuyerId_Status",
            schema: "cart",
            table: "carts",
            columns: new[] { "BuyerId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_cart_items_CartId_OfferId",
            schema: "cart",
            table: "cart_items",
            columns: new[] { "CartId", "OfferId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "cart");
        migrationBuilder.DropTable(name: "cart_items", schema: "cart");
        migrationBuilder.DropTable(name: "carts", schema: "cart");
    }
}
