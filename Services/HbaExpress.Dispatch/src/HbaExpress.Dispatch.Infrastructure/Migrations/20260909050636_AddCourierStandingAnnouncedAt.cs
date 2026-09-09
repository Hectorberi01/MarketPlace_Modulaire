using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HbaExpress.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourierStandingAnnouncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StandingAnnouncedAtUtc",
                schema: "dispatch",
                table: "couriers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StandingAnnouncedAtUtc",
                schema: "dispatch",
                table: "couriers");
        }
    }
}
