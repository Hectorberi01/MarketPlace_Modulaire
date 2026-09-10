using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Sellers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_sellers_CreatedOnUtc",
                schema: "sellers",
                table: "sellers",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_sellers_KybStatus",
                schema: "sellers",
                table: "sellers",
                column: "KybStatus");

            migrationBuilder.CreateIndex(
                name: "IX_sellers_Status",
                schema: "sellers",
                table: "sellers",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sellers_CreatedOnUtc",
                schema: "sellers",
                table: "sellers");

            migrationBuilder.DropIndex(
                name: "IX_sellers_KybStatus",
                schema: "sellers",
                table: "sellers");

            migrationBuilder.DropIndex(
                name: "IX_sellers_Status",
                schema: "sellers",
                table: "sellers");
        }
    }
}
