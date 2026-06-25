using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Recommendations.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialRecommendations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "recommendations");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "recommendations",
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
            name: "recommendations",
            schema: "recommendations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ContextProductId = table.Column<Guid>(type: "uuid", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: true),
                Score = table.Column<double>(type: "double precision", nullable: false),
                GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                recommended_product_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_recommendations", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "recommendations", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_recommendations_Type_ContextProductId",
            schema: "recommendations", table: "recommendations", columns: new[] { "Type", "ContextProductId" });

        migrationBuilder.CreateIndex(
            name: "IX_recommendations_Type_UserId",
            schema: "recommendations", table: "recommendations", columns: new[] { "Type", "UserId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "recommendations");
        migrationBuilder.DropTable(name: "recommendations", schema: "recommendations");
    }
}
