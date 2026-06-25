using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Fraud.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialFraud : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "fraud");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "fraud",
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
            name: "risk_assessments",
            schema: "fraud",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Score = table.Column<double>(type: "double precision", nullable: false),
                Signals = table.Column<string>(type: "jsonb", nullable: false),
                Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_risk_assessments", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "fraud", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_risk_assessments_SubjectType_SubjectId_EvaluatedAtUtc",
            schema: "fraud", table: "risk_assessments", columns: new[] { "SubjectType", "SubjectId", "EvaluatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "fraud");
        migrationBuilder.DropTable(name: "risk_assessments", schema: "fraud");
    }
}
