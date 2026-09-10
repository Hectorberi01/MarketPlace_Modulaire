using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_products_CreatedOnUtc",
                schema: "catalog",
                table: "products",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_products_Status",
                schema: "catalog",
                table: "products",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_products_CreatedOnUtc",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_Status",
                schema: "catalog",
                table: "products");
        }
    }
}
