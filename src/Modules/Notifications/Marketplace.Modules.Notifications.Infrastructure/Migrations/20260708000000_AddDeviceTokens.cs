using System;
using Marketplace.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Notifications.Infrastructure.Migrations;

/// <summary>Ajoute la table des jetons d'appareil (push FCM).</summary>
[DbContext(typeof(NotificationsDbContext))]
[Migration("20260708000000_AddDeviceTokens")]
public partial class AddDeviceTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "device_tokens",
            schema: "notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_tokens", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_device_tokens_Token",
            schema: "notifications",
            table: "device_tokens",
            column: "Token",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_device_tokens_UserId",
            schema: "notifications",
            table: "device_tokens",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "device_tokens", schema: "notifications");
    }
}
