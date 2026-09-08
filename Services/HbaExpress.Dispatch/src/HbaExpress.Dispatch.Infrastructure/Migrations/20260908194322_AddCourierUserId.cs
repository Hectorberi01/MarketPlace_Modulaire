using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HbaExpress.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCourierUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                schema: "dispatch",
                table: "couriers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_couriers_UserId",
                schema: "dispatch",
                table: "couriers",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_couriers_UserId",
                schema: "dispatch",
                table: "couriers");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "dispatch",
                table: "couriers");
        }
    }
}
