using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_users_CreatedOnUtc",
                schema: "identity",
                table: "users",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_users_Status",
                schema: "identity",
                table: "users",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_CreatedOnUtc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_Status",
                schema: "identity",
                table: "users");
        }
    }
}
