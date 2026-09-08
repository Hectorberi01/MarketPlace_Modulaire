using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HbaExpress.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dispatch");

            migrationBuilder.CreateTable(
                name: "couriers",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Vehicle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Standing = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Availability = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastLatitude = table.Column<double>(type: "double precision", nullable: true),
                    LastLongitude = table.Column<double>(type: "double precision", nullable: true),
                    LastPositionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommuneCodes = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_couriers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_jobs",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShippingFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssignedCourierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CurrentRound = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_job_dropoffs",
                schema: "dispatch",
                columns: table => new
                {
                    DeliveryJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CommuneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quartier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_job_dropoffs", x => x.DeliveryJobId);
                    table.ForeignKey(
                        name: "FK_delivery_job_dropoffs_delivery_jobs_DeliveryJobId",
                        column: x => x.DeliveryJobId,
                        principalSchema: "dispatch",
                        principalTable: "delivery_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_job_pickups",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SellerPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CommuneCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quartier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    IsPickedUp = table.Column<bool>(type: "boolean", nullable: false),
                    PickedUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Incident = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    DeliveryJobId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_job_pickups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_job_pickups_delivery_jobs_DeliveryJobId",
                        column: x => x.DeliveryJobId,
                        principalSchema: "dispatch",
                        principalTable: "delivery_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dispatch_offers",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveryJobId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispatch_offers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dispatch_offers_delivery_jobs_DeliveryJobId",
                        column: x => x.DeliveryJobId,
                        principalSchema: "dispatch",
                        principalTable: "delivery_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_job_parcels",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    PickupPointId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_job_parcels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_job_parcels_delivery_job_pickups_PickupPointId",
                        column: x => x.PickupPointId,
                        principalSchema: "dispatch",
                        principalTable: "delivery_job_pickups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_couriers_Standing_Availability",
                schema: "dispatch",
                table: "couriers",
                columns: new[] { "Standing", "Availability" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_job_parcels_PickupPointId",
                schema: "dispatch",
                table: "delivery_job_parcels",
                column: "PickupPointId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_job_pickups_DeliveryJobId_Sequence",
                schema: "dispatch",
                table: "delivery_job_pickups",
                columns: new[] { "DeliveryJobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_jobs_OrderId",
                schema: "dispatch",
                table: "delivery_jobs",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_jobs_Status_CreatedAtUtc",
                schema: "dispatch",
                table: "delivery_jobs",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_dispatch_offers_CourierId_Status",
                schema: "dispatch",
                table: "dispatch_offers",
                columns: new[] { "CourierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_dispatch_offers_DeliveryJobId_CourierId_Round",
                schema: "dispatch",
                table: "dispatch_offers",
                columns: new[] { "DeliveryJobId", "CourierId", "Round" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "couriers",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "delivery_job_dropoffs",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "delivery_job_parcels",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "dispatch_offers",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "delivery_job_pickups",
                schema: "dispatch");

            migrationBuilder.DropTable(
                name: "delivery_jobs",
                schema: "dispatch");
        }
    }
}
