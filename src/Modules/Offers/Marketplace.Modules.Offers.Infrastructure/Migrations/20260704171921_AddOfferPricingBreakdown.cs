using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Offers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferPricingBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                schema: "offers",
                table: "offers",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProviderFeeAmount",
                schema: "offers",
                table: "offers",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerPrice",
                schema: "offers",
                table: "offers",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionAmount",
                schema: "offers",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "ProviderFeeAmount",
                schema: "offers",
                table: "offers");

            migrationBuilder.DropColumn(
                name: "SellerPrice",
                schema: "offers",
                table: "offers");
        }
    }
}
