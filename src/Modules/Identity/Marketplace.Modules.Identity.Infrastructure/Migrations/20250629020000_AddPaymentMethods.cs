using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPaymentMethods : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "payment_methods",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                AccountRef = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ExpiryMonth = table.Column<int>(type: "integer", nullable: true),
                ExpiryYear = table.Column<int>(type: "integer", nullable: true),
                HolderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payment_methods", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_payment_methods_UserId",
            schema: "identity",
            table: "payment_methods",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "payment_methods",
            schema: "identity");
    }
}
