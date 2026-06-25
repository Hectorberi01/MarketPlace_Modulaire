using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Payments.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPaymentGatewayFlow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Flow",
            schema: "payments",
            table: "payments",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "HostedCheckout");

        migrationBuilder.CreateIndex(
            name: "IX_payments_ProviderReference",
            schema: "payments",
            table: "payments",
            column: "ProviderReference");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_payments_ProviderReference",
            schema: "payments",
            table: "payments");

        migrationBuilder.DropColumn(
            name: "Flow",
            schema: "payments",
            table: "payments");
    }
}
