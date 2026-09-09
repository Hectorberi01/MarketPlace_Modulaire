using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Shipping.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShippingZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shipping_zones",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsFallback = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommuneCodes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Rates = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_zones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipping_zones_Code",
                schema: "shipping",
                table: "shipping_zones",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shipping_zones_single_active_fallback",
                schema: "shipping",
                table: "shipping_zones",
                column: "IsFallback",
                unique: true,
                filter: "\"IsFallback\" = true AND \"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipping_zones",
                schema: "shipping");
        }
    }
}
