using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Pricing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionRedemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PerUserLimit",
                schema: "pricing",
                table: "promotions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "pricing",
                table: "promotions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "promotion_redemptions",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_redemptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_promotion_redemptions_buyer",
                schema: "pricing",
                table: "promotion_redemptions",
                columns: new[] { "PromotionId", "BuyerId" });

            migrationBuilder.CreateIndex(
                name: "ux_promotion_redemptions_order",
                schema: "pricing",
                table: "promotion_redemptions",
                columns: new[] { "PromotionId", "OrderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "promotion_redemptions",
                schema: "pricing");

            migrationBuilder.DropColumn(
                name: "PerUserLimit",
                schema: "pricing",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "pricing",
                table: "promotions");
        }
    }
}
