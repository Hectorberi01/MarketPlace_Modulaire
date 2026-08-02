using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CategoryUniqueByPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_categories_Path",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_slug",
                schema: "catalog",
                table: "categories");

            migrationBuilder.CreateIndex(
                name: "IX_categories_Path",
                schema: "catalog",
                table: "categories",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_categories_slug",
                schema: "catalog",
                table: "categories",
                column: "slug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_categories_Path",
                schema: "catalog",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_slug",
                schema: "catalog",
                table: "categories");

            migrationBuilder.CreateIndex(
                name: "IX_categories_Path",
                schema: "catalog",
                table: "categories",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_categories_slug",
                schema: "catalog",
                table: "categories",
                column: "slug",
                unique: true);
        }
    }
}
