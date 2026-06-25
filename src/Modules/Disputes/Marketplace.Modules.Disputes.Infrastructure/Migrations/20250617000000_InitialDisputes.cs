using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Disputes.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialDisputes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "disputes");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "disputes",
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
            name: "disputes",
            schema: "disputes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                RaisedBy = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_disputes", x => x.Id));

        migrationBuilder.CreateTable(
            name: "dispute_messages",
            schema: "disputes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                PhotoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DisputeId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dispute_messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_dispute_messages_disputes_DisputeId",
                    column: x => x.DisputeId,
                    principalSchema: "disputes",
                    principalTable: "disputes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "disputes", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_disputes_OrderId",
            schema: "disputes", table: "disputes", column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_disputes_RaisedBy",
            schema: "disputes", table: "disputes", column: "RaisedBy");

        migrationBuilder.CreateIndex(
            name: "IX_dispute_messages_DisputeId",
            schema: "disputes", table: "dispute_messages", column: "DisputeId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "disputes");
        migrationBuilder.DropTable(name: "dispute_messages", schema: "disputes");
        migrationBuilder.DropTable(name: "disputes", schema: "disputes");
    }
}
