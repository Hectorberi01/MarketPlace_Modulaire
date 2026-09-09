using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Sellers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSellerSuspensionMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KybRejectionReason",
                schema: "sellers",
                table: "sellers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedOnUtc",
                schema: "sellers",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SuspensionCount",
                schema: "sellers",
                table: "sellers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                schema: "sellers",
                table: "sellers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KybRejectionReason",
                schema: "sellers",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SuspendedOnUtc",
                schema: "sellers",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SuspensionCount",
                schema: "sellers",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                schema: "sellers",
                table: "sellers");
        }
    }
}
