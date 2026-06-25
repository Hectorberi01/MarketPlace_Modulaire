using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "catalog");

        migrationBuilder.CreateTable(
            name: "brands",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                LogoUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_brands", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "categories",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                attribute_schema = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_categories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "catalog",
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
            name: "products",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                BrandId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Gtin = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                Ean = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                ProductGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                attributes = table.Column<string>(type: "jsonb", nullable: false),
                tags = table.Column<List<string>>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_products", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "product_media",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AltText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Position = table.Column<int>(type: "integer", nullable: false),
                IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_media", x => x.Id);
                table.ForeignKey(
                    name: "FK_product_media_products_ProductId",
                    column: x => x.ProductId,
                    principalSchema: "catalog",
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "product_variants",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                variant_attributes = table.Column<string>(type: "jsonb", nullable: false),
                Barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                WeightGrams = table.Column<int>(type: "integer", nullable: false),
                dimensions = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_variants", x => x.Id);
                table.ForeignKey(
                    name: "FK_product_variants_products_ProductId",
                    column: x => x.ProductId,
                    principalSchema: "catalog",
                    principalTable: "products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_brands_slug",
            schema: "catalog",
            table: "brands",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_categories_ParentId",
            schema: "catalog",
            table: "categories",
            column: "ParentId");

        migrationBuilder.CreateIndex(
            name: "IX_categories_Path",
            schema: "catalog",
            table: "categories",
            column: "Path");

        migrationBuilder.CreateIndex(
            name: "IX_categories_slug",
            schema: "catalog",
            table: "categories",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "catalog",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_product_media_ProductId",
            schema: "catalog",
            table: "product_media",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_product_variants_ProductId",
            schema: "catalog",
            table: "product_variants",
            column: "ProductId");

        migrationBuilder.CreateIndex(
            name: "IX_product_variants_sku",
            schema: "catalog",
            table: "product_variants",
            column: "sku",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_products_BrandId",
            schema: "catalog",
            table: "products",
            column: "BrandId");

        migrationBuilder.CreateIndex(
            name: "IX_products_CategoryId",
            schema: "catalog",
            table: "products",
            column: "CategoryId");

        migrationBuilder.CreateIndex(
            name: "IX_products_ProductGroupId",
            schema: "catalog",
            table: "products",
            column: "ProductGroupId");

        migrationBuilder.CreateIndex(
            name: "IX_products_SellerId",
            schema: "catalog",
            table: "products",
            column: "SellerId");

        migrationBuilder.CreateIndex(
            name: "IX_products_slug",
            schema: "catalog",
            table: "products",
            column: "slug",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "brands", schema: "catalog");
        migrationBuilder.DropTable(name: "categories", schema: "catalog");
        migrationBuilder.DropTable(name: "outbox_messages", schema: "catalog");
        migrationBuilder.DropTable(name: "product_media", schema: "catalog");
        migrationBuilder.DropTable(name: "product_variants", schema: "catalog");
        migrationBuilder.DropTable(name: "products", schema: "catalog");
    }
}
