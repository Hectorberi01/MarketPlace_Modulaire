using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Shipping.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialShipping : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "shipping");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "shipping",
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
            name: "shipments",
            schema: "shipping",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                ShipFromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ShippedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_shipments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "shipment_items",
            schema: "shipping",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                ShipmentId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_shipment_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_shipment_items_shipments_ShipmentId",
                    column: x => x.ShipmentId,
                    principalSchema: "shipping",
                    principalTable: "shipments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "shipping",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_shipments_OrderId",
            schema: "shipping",
            table: "shipments",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_shipments_SellerId",
            schema: "shipping",
            table: "shipments",
            column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_shipment_items_ShipmentId",
            schema: "shipping",
            table: "shipment_items",
            column: "ShipmentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "shipping");
        migrationBuilder.DropTable(name: "shipment_items", schema: "shipping");
        migrationBuilder.DropTable(name: "shipments", schema: "shipping");
    }
}
