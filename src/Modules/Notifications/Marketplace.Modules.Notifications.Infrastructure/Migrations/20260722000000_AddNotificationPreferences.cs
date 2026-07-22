using System;
using System.Collections.Generic;
using Marketplace.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Notifications.Infrastructure.Migrations;

/// <summary>Ajoute la table des préférences de notification (catégories de push coupées).</summary>
[DbContext(typeof(NotificationsDbContext))]
[Migration("20260722000000_AddNotificationPreferences")]
public partial class AddNotificationPreferences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_preferences",
            schema: "notifications",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                muted_categories = table.Column<List<string>>(type: "text[]", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_preferences", x => x.UserId);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "notification_preferences", schema: "notifications");
    }
}
