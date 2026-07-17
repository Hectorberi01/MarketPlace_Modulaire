using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Returns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundExecutionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RefundApprovedAtUtc",
                schema: "returns",
                table: "return_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundExecutedAtUtc",
                schema: "returns",
                table: "return_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                schema: "returns",
                table: "return_requests",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_Status",
                schema: "returns",
                table: "return_requests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_return_requests_Status",
                schema: "returns",
                table: "return_requests");

            migrationBuilder.DropColumn(
                name: "RefundApprovedAtUtc",
                schema: "returns",
                table: "return_requests");

            migrationBuilder.DropColumn(
                name: "RefundExecutedAtUtc",
                schema: "returns",
                table: "return_requests");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                schema: "returns",
                table: "return_requests");
        }
    }
}
