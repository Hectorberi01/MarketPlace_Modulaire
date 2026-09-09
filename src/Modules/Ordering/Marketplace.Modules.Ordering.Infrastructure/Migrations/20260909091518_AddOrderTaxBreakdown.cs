using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTaxBreakdown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                schema: "ordering",
                table: "orders",
                type: "numeric(9,5)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "TaxRuleId",
                schema: "ordering",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxTotal",
                schema: "ordering",
                table: "orders",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxRate",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "TaxRuleId",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "TaxTotal",
                schema: "ordering",
                table: "orders");
        }
    }
}
