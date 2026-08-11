using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingAndZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "deliveries",
                table: "deliveries",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                schema: "deliveries",
                table: "deliveries",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                schema: "deliveries",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "delivery_quotes",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: false),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_latitude = table.Column<double>(type: "double precision", nullable: false),
                    dropoff_longitude = table.Column<double>(type: "double precision", nullable: false),
                    weight_kg = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    Size = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeliveryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    EstimatedDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BasePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DistancePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ZoneSurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    VehicleSurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    WeightSurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    UrgencySurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PricingRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricingVersion = table.Column<int>(type: "integer", nullable: false),
                    PickupZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    DropoffZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedByDeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_quotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_zones",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    boundary = table.Column<string>(type: "jsonb", nullable: false),
                    BaseFee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ExtraFee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    RoadFactor = table.Column<double>(type: "double precision", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_rules",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    BasePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PricePerKm = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    MinimumPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    MaximumDistanceKm = table.Column<double>(type: "double precision", nullable: true),
                    DeliveryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    VehicleType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UrgencySurcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_weight_tiers",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FromKg = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: false),
                    ToKg = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    Surcharge = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    pricing_rule_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_weight_tiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_weight_tiers_pricing_rules_pricing_rule_id",
                        column: x => x.pricing_rule_id,
                        principalSchema: "deliveries",
                        principalTable: "pricing_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_quotes_expiry",
                schema: "deliveries",
                table: "delivery_quotes",
                column: "ExpiresAtUtc",
                filter: "\"ConsumedByDeliveryId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_zones_active",
                schema: "deliveries",
                table: "delivery_zones",
                columns: new[] { "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "ix_pricing_rules_active",
                schema: "deliveries",
                table: "pricing_rules",
                columns: new[] { "IsActive", "ValidFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_weight_tiers_pricing_rule_id",
                schema: "deliveries",
                table: "pricing_weight_tiers",
                column: "pricing_rule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_quotes",
                schema: "deliveries");

            migrationBuilder.DropTable(
                name: "delivery_zones",
                schema: "deliveries");

            migrationBuilder.DropTable(
                name: "pricing_weight_tiers",
                schema: "deliveries");

            migrationBuilder.DropTable(
                name: "pricing_rules",
                schema: "deliveries");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "Price",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                schema: "deliveries",
                table: "deliveries");
        }
    }
}
