using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Returns.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialReturns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "returns");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "returns",
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
            name: "return_requests",
            schema: "returns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                RefundAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                RefundId = table.Column<Guid>(type: "uuid", nullable: true),
                Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_return_requests", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "returns", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_return_requests_BuyerId",
            schema: "returns", table: "return_requests", column: "BuyerId");

        migrationBuilder.CreateIndex(
            name: "IX_return_requests_SellerId",
            schema: "returns", table: "return_requests", column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_return_requests_OrderId_OfferId",
            schema: "returns", table: "return_requests", columns: new[] { "OrderId", "OfferId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "returns");
        migrationBuilder.DropTable(name: "return_requests", schema: "returns");
    }
}
