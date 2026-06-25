using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Inventory.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialInventory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "inventory");

        migrationBuilder.CreateTable(
            name: "fulfillment_locations",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                address_city = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                address_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                address_latitude = table.Column<double>(type: "double precision", nullable: true),
                address_longitude = table.Column<double>(type: "double precision", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fulfillment_locations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "inventory_items",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                OnHand = table.Column<int>(type: "integer", nullable: false),
                ReorderThreshold = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inventory_items", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "inventory",
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
            name: "stock_reservations",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_reservations", x => x.Id);
                table.ForeignKey(
                    name: "FK_stock_reservations_inventory_items_InventoryItemId",
                    column: x => x.InventoryItemId,
                    principalSchema: "inventory",
                    principalTable: "inventory_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_fulfillment_locations_OwnerId",
            schema: "inventory",
            table: "fulfillment_locations",
            column: "OwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_items_sku",
            schema: "inventory",
            table: "inventory_items",
            column: "sku");

        migrationBuilder.CreateIndex(
            name: "IX_inventory_items_sku_LocationId",
            schema: "inventory",
            table: "inventory_items",
            columns: new[] { "sku", "LocationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "inventory",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_stock_reservations_InventoryItemId",
            schema: "inventory",
            table: "stock_reservations",
            column: "InventoryItemId");

        migrationBuilder.CreateIndex(
            name: "IX_stock_reservations_OrderId",
            schema: "inventory",
            table: "stock_reservations",
            column: "OrderId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "fulfillment_locations", schema: "inventory");
        migrationBuilder.DropTable(name: "outbox_messages", schema: "inventory");
        migrationBuilder.DropTable(name: "stock_reservations", schema: "inventory");
        migrationBuilder.DropTable(name: "inventory_items", schema: "inventory");
    }
}
