using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "identity");

        migrationBuilder.CreateTable(
            name: "roles",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                permissions = table.Column<List<string>>(type: "text[]", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_roles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                MfaEnabled = table.Column<bool>(type: "boolean", nullable: false),
                MfaSecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                SecurityStamp = table.Column<Guid>(type: "uuid", nullable: false),
                EmailVerificationTokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                EmailVerificationExpiresOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "jsonb", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "user_roles",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_roles", x => x.Id);
                table.ForeignKey(
                    name: "FK_user_roles_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ExpiresOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RevokedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_refresh_tokens_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_roles_Name",
            schema: "identity",
            table: "roles",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_users_email",
            schema: "identity",
            table: "users",
            column: "email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_users_phone_number",
            schema: "identity",
            table: "users",
            column: "phone_number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "identity",
            table: "outbox_messages",
            column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_user_roles_UserId_RoleId",
            schema: "identity",
            table: "user_roles",
            columns: new[] { "UserId", "RoleId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_TokenHash",
            schema: "identity",
            table: "refresh_tokens",
            column: "TokenHash");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_UserId",
            schema: "identity",
            table: "refresh_tokens",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "identity");
        migrationBuilder.DropTable(name: "refresh_tokens", schema: "identity");
        migrationBuilder.DropTable(name: "roles", schema: "identity");
        migrationBuilder.DropTable(name: "user_roles", schema: "identity");
        migrationBuilder.DropTable(name: "users", schema: "identity");
    }
}
