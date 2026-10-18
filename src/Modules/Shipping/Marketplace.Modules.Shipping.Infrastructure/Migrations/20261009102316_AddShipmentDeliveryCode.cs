using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Shipping.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentDeliveryCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryCode",
                schema: "shipping",
                table: "shipments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryCodeAttempts",
                schema: "shipping",
                table: "shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryCode",
                schema: "shipping",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "DeliveryCodeAttempts",
                schema: "shipping",
                table: "shipments");
        }
    }
}
