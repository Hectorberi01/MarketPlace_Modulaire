using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Tax.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialTax : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "tax");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "tax",
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
            name: "tax_rules",
            schema: "tax",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Jurisdiction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                Rate = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_tax_rules", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "tax", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_tax_rules_Jurisdiction_CategoryId",
            schema: "tax", table: "tax_rules", columns: new[] { "Jurisdiction", "CategoryId" });

        migrationBuilder.CreateIndex(
            name: "IX_tax_rules_IsActive",
            schema: "tax", table: "tax_rules", column: "IsActive");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "tax");
        migrationBuilder.DropTable(name: "tax_rules", schema: "tax");
    }
}
