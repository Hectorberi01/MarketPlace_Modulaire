using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Billing.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialBilling : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "billing");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "billing",
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
            name: "commission_rules",
            schema: "billing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                Rate = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                FixedFee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                MinFee = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                MaxFee = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_commission_rules", x => x.Id));

        migrationBuilder.CreateTable(
            name: "invoices",
            schema: "billing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_invoices", x => x.Id));

        migrationBuilder.CreateTable(
            name: "invoice_lines",
            schema: "billing",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoice_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_invoice_lines_invoices_InvoiceId",
                    column: x => x.InvoiceId,
                    principalSchema: "billing",
                    principalTable: "invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "billing", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_commission_rules_Scope_TargetId",
            schema: "billing", table: "commission_rules", columns: new[] { "Scope", "TargetId" });

        migrationBuilder.CreateIndex(
            name: "IX_commission_rules_IsActive",
            schema: "billing", table: "commission_rules", column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_invoices_SellerId",
            schema: "billing", table: "invoices", column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_invoice_lines_InvoiceId",
            schema: "billing", table: "invoice_lines", column: "InvoiceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "billing");
        migrationBuilder.DropTable(name: "commission_rules", schema: "billing");
        migrationBuilder.DropTable(name: "invoice_lines", schema: "billing");
        migrationBuilder.DropTable(name: "invoices", schema: "billing");
    }
}
