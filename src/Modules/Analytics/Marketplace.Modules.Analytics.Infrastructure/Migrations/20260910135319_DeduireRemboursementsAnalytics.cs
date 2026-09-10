using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Analytics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeduireRemboursementsAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                schema: "analytics",
                table: "metric_snapshots",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RefundsCount",
                schema: "analytics",
                table: "metric_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "processed_refunds",
                schema: "analytics",
                columns: table => new
                {
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_refunds", x => new { x.Source, x.SourceId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_processed_refunds_OrderId",
                schema: "analytics",
                table: "processed_refunds",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_refunds",
                schema: "analytics");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                schema: "analytics",
                table: "metric_snapshots");

            migrationBuilder.DropColumn(
                name: "RefundsCount",
                schema: "analytics",
                table: "metric_snapshots");
        }
    }
}
