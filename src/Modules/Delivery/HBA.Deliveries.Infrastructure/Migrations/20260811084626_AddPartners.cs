using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PartnerId",
                schema: "deliveries",
                table: "deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_partner",
                schema: "deliveries",
                table: "deliveries",
                columns: new[] { "PartnerId", "CreatedAtUtc" },
                filter: "\"PartnerId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_deliveries_partner",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                schema: "deliveries",
                table: "deliveries");
        }
    }
}
