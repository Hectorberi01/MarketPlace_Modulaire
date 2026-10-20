using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Platform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provider_change_log",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_change_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "providers",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    SettingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SecretsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_providers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "platform",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "platform",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_provider_change_log_AtUtc",
                schema: "platform",
                table: "provider_change_log",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_provider_change_log_ProviderId",
                schema: "platform",
                table: "provider_change_log",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_providers_Channel_Code",
                schema: "platform",
                table: "providers",
                columns: new[] { "Channel", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_providers_Channel_IsEnabled_Priority",
                schema: "platform",
                table: "providers",
                columns: new[] { "Channel", "IsEnabled", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "provider_change_log",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "providers",
                schema: "platform");
        }
    }
}
