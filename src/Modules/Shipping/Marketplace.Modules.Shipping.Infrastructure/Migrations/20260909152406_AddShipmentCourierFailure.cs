using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Shipping.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentCourierFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CourierFailedAtUtc",
                schema: "shipping",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourierFailureReason",
                schema: "shipping",
                table: "shipments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FailedDeliveryJobId",
                schema: "shipping",
                table: "shipments",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CourierFailedAtUtc",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "CourierFailureReason",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "FailedDeliveryJobId",
                schema: "shipping",
                table: "shipments");
        }
    }
}
