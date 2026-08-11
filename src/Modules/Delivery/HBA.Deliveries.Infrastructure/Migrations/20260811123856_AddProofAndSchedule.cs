using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProofAndSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProofValue",
                schema: "deliveries",
                table: "deliveries",
                newName: "proof_value");

            migrationBuilder.AddColumn<string>(
                name: "IssuedPin",
                schema: "deliveries",
                table: "deliveries",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledForUtc",
                schema: "deliveries",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "proof_captured_at_utc",
                schema: "deliveries",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proof_kind",
                schema: "deliveries",
                table: "deliveries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_scheduled_for",
                schema: "deliveries",
                table: "deliveries",
                column: "ScheduledForUtc",
                filter: "\"ScheduledForUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deliveries_scheduled_for",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "IssuedPin",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ScheduledForUtc",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "proof_captured_at_utc",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "proof_kind",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.RenameColumn(
                name: "proof_value",
                schema: "deliveries",
                table: "deliveries",
                newName: "ProofValue");
        }
    }
}
