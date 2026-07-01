using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddAddresses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "addresses",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                Recipient = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_addresses", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_addresses_UserId",
            schema: "identity",
            table: "addresses",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "addresses",
            schema: "identity");
    }
}
