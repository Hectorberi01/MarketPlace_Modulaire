using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverEarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DriverEarning",
                schema: "deliveries",
                table: "deliveries",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DriverShareRate",
                schema: "deliveries",
                table: "deliveries",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriverEarning",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "DriverShareRate",
                schema: "deliveries",
                table: "deliveries");
        }
    }
}
