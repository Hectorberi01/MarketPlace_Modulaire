using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "deliveries");

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    pickup_contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    pickup_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pickup_commune_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    pickup_quartier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    pickup_landmark = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    pickup_instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pickup_latitude = table.Column<double>(type: "double precision", nullable: true),
                    pickup_longitude = table.Column<double>(type: "double precision", nullable: true),
                    dropoff_contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    dropoff_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dropoff_commune_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    dropoff_quartier = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    dropoff_landmark = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    dropoff_instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dropoff_latitude = table.Column<double>(type: "double precision", nullable: true),
                    dropoff_longitude = table.Column<double>(type: "double precision", nullable: true),
                    package_description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    package_weight_kg = table.Column<decimal>(type: "numeric(9,3)", precision: 9, scale: 3, nullable: true),
                    package_is_fragile = table.Column<bool>(type: "boolean", nullable: false),
                    package_is_perishable = table.Column<bool>(type: "boolean", nullable: false),
                    RequiredProof = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AssignedDriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PickedUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProofValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "drivers",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Vehicle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccountStatus = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Availability = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_latitude = table.Column<double>(type: "double precision", nullable: true),
                    last_longitude = table.Column<double>(type: "double precision", nullable: true),
                    LastPositionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedDeliveries = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_assignments",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OfferedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_assignments_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalSchema: "deliveries",
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_awaiting_driver",
                schema: "deliveries",
                table: "deliveries",
                columns: new[] { "Status", "CreatedAtUtc" },
                filter: "\"Status\" IN ('SearchingDriver', 'NoDriverAvailable')");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_driver",
                schema: "deliveries",
                table: "deliveries",
                column: "AssignedDriverId");

            migrationBuilder.CreateIndex(
                name: "ux_deliveries_reference_source",
                schema: "deliveries",
                table: "deliveries",
                columns: new[] { "Reference", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_assignments_DriverId",
                schema: "deliveries",
                table: "delivery_assignments",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_assignments_delivery_id",
                schema: "deliveries",
                table: "delivery_assignments",
                column: "delivery_id");

            migrationBuilder.CreateIndex(
                name: "ix_drivers_dispatchable",
                schema: "deliveries",
                table: "drivers",
                columns: new[] { "AccountStatus", "Availability" });

            migrationBuilder.CreateIndex(
                name: "ux_drivers_user",
                schema: "deliveries",
                table: "drivers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "deliveries",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "deliveries",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_assignments",
                schema: "deliveries");

            migrationBuilder.DropTable(
                name: "drivers",
                schema: "deliveries");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "deliveries");

            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "deliveries");
        }
    }
}
