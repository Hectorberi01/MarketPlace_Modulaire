using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Wishlist.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialWishlist : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "wishlist");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "wishlist",
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
            name: "wishlists",
            schema: "wishlist",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wishlists", x => x.Id));

        migrationBuilder.CreateTable(
            name: "wishlist_items",
            schema: "wishlist",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                PriceAlert = table.Column<bool>(type: "boolean", nullable: false),
                StockAlert = table.Column<bool>(type: "boolean", nullable: false),
                AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                WishlistId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_wishlist_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_wishlist_items_wishlists_WishlistId",
                    column: x => x.WishlistId,
                    principalSchema: "wishlist",
                    principalTable: "wishlists",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "wishlist", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_wishlists_UserId",
            schema: "wishlist", table: "wishlists", column: "UserId", unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_wishlist_items_ProductId",
            schema: "wishlist", table: "wishlist_items", column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_wishlist_items_WishlistId",
            schema: "wishlist", table: "wishlist_items", column: "WishlistId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "wishlist");
        migrationBuilder.DropTable(name: "wishlist_items", schema: "wishlist");
        migrationBuilder.DropTable(name: "wishlists", schema: "wishlist");
    }
}
