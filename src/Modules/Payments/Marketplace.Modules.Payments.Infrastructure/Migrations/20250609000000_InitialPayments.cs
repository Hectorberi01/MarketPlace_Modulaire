using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Payments.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialPayments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "payments");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "payments",
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
            name: "payments",
            schema: "payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ProviderReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payments", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "payments",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_payments_OrderId",
            schema: "payments",
            table: "payments",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_payments_Status",
            schema: "payments",
            table: "payments",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "payments");
        migrationBuilder.DropTable(name: "payments", schema: "payments");
    }
}
