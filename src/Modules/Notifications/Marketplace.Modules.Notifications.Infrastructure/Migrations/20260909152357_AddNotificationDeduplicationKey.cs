using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Notifications.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeduplicationKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeduplicationKey",
                schema: "notifications",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_DeduplicationKey",
                schema: "notifications",
                table: "notifications",
                column: "DeduplicationKey",
                unique: true,
                filter: "\"DeduplicationKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_DeduplicationKey",
                schema: "notifications",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "DeduplicationKey",
                schema: "notifications",
                table: "notifications");
        }
    }
}
