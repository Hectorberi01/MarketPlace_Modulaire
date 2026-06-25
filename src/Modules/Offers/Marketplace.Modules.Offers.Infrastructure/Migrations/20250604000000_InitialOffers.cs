using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Offers.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialOffers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "offers");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "offers",
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
            name: "offers",
            schema: "offers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                base_price_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                base_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                FulfillmentType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ShipFromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                HandlingTime = table.Column<int>(type: "integer", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_offers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "offers",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_offers_ProductId",
            schema: "offers",
            table: "offers",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_offers_SellerId",
            schema: "offers",
            table: "offers",
            column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_offers_SellerId_sku",
            schema: "offers",
            table: "offers",
            columns: new[] { "SellerId", "sku" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "offers", schema: "offers");
        migrationBuilder.DropTable(name: "outbox_messages", schema: "offers");
    }
}
