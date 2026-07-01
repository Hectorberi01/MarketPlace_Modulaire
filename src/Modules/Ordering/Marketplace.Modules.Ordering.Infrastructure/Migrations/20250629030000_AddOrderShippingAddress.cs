using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddOrderShippingAddress : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ShipToLabel", schema: "ordering", table: "orders",
            type: "character varying(60)", maxLength: 60, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToRecipient", schema: "ordering", table: "orders",
            type: "character varying(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToLine1", schema: "ordering", table: "orders",
            type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToLine2", schema: "ordering", table: "orders",
            type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToCity", schema: "ordering", table: "orders",
            type: "character varying(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToCountry", schema: "ordering", table: "orders",
            type: "character varying(80)", maxLength: 80, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToPhone", schema: "ordering", table: "orders",
            type: "character varying(30)", maxLength: 30, nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShipToLabel", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToRecipient", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToLine1", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToLine2", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToCity", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToCountry", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToPhone", schema: "ordering", table: "orders");
    }
}
