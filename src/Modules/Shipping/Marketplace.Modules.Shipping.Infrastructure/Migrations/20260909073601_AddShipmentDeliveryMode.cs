using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Shipping.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentDeliveryMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryJobId",
                schema: "shipping",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryMode",
                schema: "shipping",
                table: "shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryJobId",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                schema: "shipping",
                table: "shipments");
        }
    }
}
