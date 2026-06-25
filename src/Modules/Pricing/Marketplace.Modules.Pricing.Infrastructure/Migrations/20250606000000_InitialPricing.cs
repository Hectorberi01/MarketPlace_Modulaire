using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Pricing.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialPricing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "pricing");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "pricing",
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
            name: "promotions",
            schema: "pricing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                FundedBy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Value = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                ScopeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                UsageLimit = table.Column<int>(type: "integer", nullable: false),
                UsedCount = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                targets = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                condition_min_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                condition_min_quantity = table.Column<int>(type: "integer", nullable: true),
                condition_first_order_only = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_promotions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "pricing",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_promotions_Code",
            schema: "pricing",
            table: "promotions",
            column: "Code");

        migrationBuilder.CreateIndex(
            name: "IX_promotions_Status",
            schema: "pricing",
            table: "promotions",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "pricing");
        migrationBuilder.DropTable(name: "promotions", schema: "pricing");
    }
}
