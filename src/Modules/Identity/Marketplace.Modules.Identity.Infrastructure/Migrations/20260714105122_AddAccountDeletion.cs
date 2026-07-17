using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_phone_number",
                schema: "identity",
                table: "users");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedOnUtc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true,
                filter: "\"Status\" <> 'Deleted'");

            migrationBuilder.CreateIndex(
                name: "IX_users_phone_number",
                schema: "identity",
                table: "users",
                column: "phone_number",
                unique: true,
                filter: "\"Status\" <> 'Deleted'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_phone_number",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "DeletedOnUtc",
                schema: "identity",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_phone_number",
                schema: "identity",
                table: "users",
                column: "phone_number",
                unique: true);
        }
    }
}
