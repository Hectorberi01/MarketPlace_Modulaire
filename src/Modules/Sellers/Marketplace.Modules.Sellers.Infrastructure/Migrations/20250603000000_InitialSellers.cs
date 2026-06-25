using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Sellers.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialSellers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "sellers");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "sellers",
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
            name: "sellers",
            schema: "sellers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                LogoUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                KybStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CommissionRate = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                Rating = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                SalesCount = table.Column<int>(type: "integer", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                payout_account = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sellers", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "kyb_documents",
            schema: "sellers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                FileUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                UploadedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_kyb_documents", x => x.Id);
                table.ForeignKey(
                    name: "FK_kyb_documents_sellers_SellerId",
                    column: x => x.SellerId,
                    principalSchema: "sellers",
                    principalTable: "sellers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "sellers",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_kyb_documents_SellerId",
            schema: "sellers",
            table: "kyb_documents",
            column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_sellers_ShopName",
            schema: "sellers",
            table: "sellers",
            column: "ShopName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sellers_UserId",
            schema: "sellers",
            table: "sellers",
            column: "UserId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "kyb_documents", schema: "sellers");
        migrationBuilder.DropTable(name: "outbox_messages", schema: "sellers");
        migrationBuilder.DropTable(name: "sellers", schema: "sellers");
    }
}
