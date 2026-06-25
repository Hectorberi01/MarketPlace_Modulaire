using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Payments.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPaymentEscrow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "EscrowReleasedAt",
            schema: "payments",
            table: "payments",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "EscrowReleasedAt",
            schema: "payments",
            table: "payments");
    }
}
