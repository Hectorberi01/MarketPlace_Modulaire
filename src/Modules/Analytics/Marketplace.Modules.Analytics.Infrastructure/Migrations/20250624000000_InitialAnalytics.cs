using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Analytics.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialAnalytics : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "analytics");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "analytics",
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
            name: "metric_snapshots",
            schema: "analytics",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                PeriodKey = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Granularity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                OrdersCount = table.Column<int>(type: "integer", nullable: false),
                ItemsSold = table.Column<int>(type: "integer", nullable: false),
                GmvAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_metric_snapshots", x => x.Id));

        migrationBuilder.CreateTable(
            name: "processed_orders",
            schema: "analytics",
            columns: table => new
            {
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_processed_orders", x => x.OrderId));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "analytics", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_metric_snapshots_Scope_OwnerId_PeriodKey_Granularity",
            schema: "analytics", table: "metric_snapshots",
            columns: new[] { "Scope", "OwnerId", "PeriodKey", "Granularity" }, unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "analytics");
        migrationBuilder.DropTable(name: "metric_snapshots", schema: "analytics");
        migrationBuilder.DropTable(name: "processed_orders", schema: "analytics");
    }
}
