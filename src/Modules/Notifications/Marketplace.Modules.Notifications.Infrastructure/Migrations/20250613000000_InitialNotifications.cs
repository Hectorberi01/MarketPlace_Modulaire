using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Notifications.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "notifications");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "jsonb", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "notifications",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                RelatedEntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notifications", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "notifications",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_notifications_RecipientUserId_Status",
            schema: "notifications",
            table: "notifications",
            columns: new[] { "RecipientUserId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "notifications");
        migrationBuilder.DropTable(name: "notifications", schema: "notifications");
    }
}
