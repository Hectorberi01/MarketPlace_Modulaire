using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPasswordResetToken : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PasswordResetTokenHash",
            schema: "identity",
            table: "users",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PasswordResetExpiresOnUtc",
            schema: "identity",
            table: "users",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PasswordResetTokenHash",
            schema: "identity",
            table: "users");

        migrationBuilder.DropColumn(
            name: "PasswordResetExpiresOnUtc",
            schema: "identity",
            table: "users");
    }
}
