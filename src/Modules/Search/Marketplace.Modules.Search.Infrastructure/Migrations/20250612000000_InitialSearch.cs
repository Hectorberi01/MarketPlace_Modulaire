using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Search.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialSearch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "search");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "search",
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
            name: "documents",
            schema: "search",
            columns: table => new
            {
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Slug = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                MinPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                AverageRating = table.Column<double>(type: "double precision", nullable: false),
                ReviewCount = table.Column<int>(type: "integer", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_documents", x => x.ProductId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "search",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_documents_CategoryId",
            schema: "search",
            table: "documents",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_documents_Name",
            schema: "search",
            table: "documents",
            column: "Name");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "search");
        migrationBuilder.DropTable(name: "documents", schema: "search");
    }
}
