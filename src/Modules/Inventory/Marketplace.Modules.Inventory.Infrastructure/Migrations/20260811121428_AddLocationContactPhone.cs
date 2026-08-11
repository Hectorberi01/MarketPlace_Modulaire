using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationContactPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_contact_phone",
                schema: "inventory",
                table: "fulfillment_locations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address_contact_phone",
                schema: "inventory",
                table: "fulfillment_locations");
        }
    }
}
