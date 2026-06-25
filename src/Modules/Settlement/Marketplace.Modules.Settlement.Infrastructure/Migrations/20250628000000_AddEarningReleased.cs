using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddEarningReleased : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ReleasedAtUtc",
            schema: "settlement",
            table: "seller_earnings",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReleasedAtUtc",
            schema: "settlement",
            table: "seller_earnings");
    }
}
