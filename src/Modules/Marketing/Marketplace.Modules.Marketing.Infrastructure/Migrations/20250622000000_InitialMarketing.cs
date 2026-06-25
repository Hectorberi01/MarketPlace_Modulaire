using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Marketing.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialMarketing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "marketing");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "marketing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "jsonb", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_outbox_messages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "campaigns",
            schema: "marketing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                TargetSegment = table.Column<string>(type: "jsonb", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                promotion_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_campaigns", x => x.Id));

        migrationBuilder.CreateTable(
            name: "content_blocks",
            schema: "marketing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Slot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                Content = table.Column<string>(type: "jsonb", nullable: false),
                StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_content_blocks", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "marketing", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_campaigns_Status",
            schema: "marketing", table: "campaigns", column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_content_blocks_Slot_Status",
            schema: "marketing", table: "content_blocks", columns: new[] { "Slot", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "marketing");
        migrationBuilder.DropTable(name: "campaigns", schema: "marketing");
        migrationBuilder.DropTable(name: "content_blocks", schema: "marketing");
    }
}
