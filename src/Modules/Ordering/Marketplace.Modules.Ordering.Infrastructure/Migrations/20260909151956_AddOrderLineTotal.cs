using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderLineTotal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FinalUnitPrice",
                schema: "ordering",
                table: "order_lines",
                newName: "LineTotal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LineTotal",
                schema: "ordering",
                table: "order_lines",
                newName: "FinalUnitPrice");
        }
    }
}
