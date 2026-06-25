using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Loyalty.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialLoyalty : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "loyalty");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "loyalty",
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
            name: "loyalty_accounts",
            schema: "loyalty",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PointsBalance = table.Column<int>(type: "integer", nullable: false),
                LifetimePoints = table.Column<int>(type: "integer", nullable: false),
                Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_loyalty_accounts", x => x.Id));

        migrationBuilder.CreateTable(
            name: "point_transactions",
            schema: "loyalty",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<int>(type: "integer", nullable: false),
                Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                RelatedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LoyaltyAccountId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_point_transactions", x => x.Id);
                table.ForeignKey(
                    name: "FK_point_transactions_loyalty_accounts_LoyaltyAccountId",
                    column: x => x.LoyaltyAccountId,
                    principalSchema: "loyalty",
                    principalTable: "loyalty_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "loyalty", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_loyalty_accounts_UserId",
            schema: "loyalty", table: "loyalty_accounts", column: "UserId", unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_point_transactions_LoyaltyAccountId",
            schema: "loyalty", table: "point_transactions", column: "LoyaltyAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_point_transactions_RelatedOrderId",
            schema: "loyalty", table: "point_transactions", column: "RelatedOrderId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "loyalty");
        migrationBuilder.DropTable(name: "point_transactions", schema: "loyalty");
        migrationBuilder.DropTable(name: "loyalty_accounts", schema: "loyalty");
    }
}
