using System;
using Marketplace.Modules.Offers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Offers.Infrastructure.Migrations;

/// <summary>Ajoute les colonnes de remise (promo vendeur) à la table des offres.</summary>
[DbContext(typeof(OffersDbContext))]
[Migration("20260722120000_AddOfferDiscount")]
public partial class AddOfferDiscount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "original_seller_price",
            schema: "offers",
            table: "offers",
            type: "numeric(18,2)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "compare_at_amount",
            schema: "offers",
            table: "offers",
            type: "numeric(18,2)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "compare_at_currency",
            schema: "offers",
            table: "offers",
            type: "character varying(3)",
            maxLength: 3,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "discount_ends_on_utc",
            schema: "offers",
            table: "offers",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "original_seller_price", schema: "offers", table: "offers");
        migrationBuilder.DropColumn(name: "compare_at_amount", schema: "offers", table: "offers");
        migrationBuilder.DropColumn(name: "compare_at_currency", schema: "offers", table: "offers");
        migrationBuilder.DropColumn(name: "discount_ends_on_utc", schema: "offers", table: "offers");
    }
}
