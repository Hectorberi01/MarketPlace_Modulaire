using System;
using Marketplace.Modules.Reviews.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Reviews.Infrastructure.Migrations;

/// <summary>Ajoute la réponse publique du vendeur à un avis (SellerReply + date).</summary>
[DbContext(typeof(ReviewsDbContext))]
[Migration("20250629000000_AddReviewSellerReply")]
public partial class AddReviewSellerReply : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SellerReply",
            schema: "reviews",
            table: "reviews",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "SellerRepliedAtUtc",
            schema: "reviews",
            table: "reviews",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SellerReply", schema: "reviews", table: "reviews");
        migrationBuilder.DropColumn(name: "SellerRepliedAtUtc", schema: "reviews", table: "reviews");
    }
}
