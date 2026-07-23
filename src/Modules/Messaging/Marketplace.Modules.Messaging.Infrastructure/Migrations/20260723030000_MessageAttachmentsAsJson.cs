using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Messaging.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MessageAttachmentsAsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Les pièces jointes passent de `text[]` (jamais persisté correctement à cause
            // du comportement « collection primitive » d'EF Core 8) à `text` contenant du
            // JSON. La colonne n'a jamais stocké de donnée exploitable : on la recrée.
            migrationBuilder.DropColumn(
                name: "attachments",
                schema: "messaging",
                table: "conversation_messages");

            migrationBuilder.AddColumn<string>(
                name: "attachments",
                schema: "messaging",
                table: "conversation_messages",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachments",
                schema: "messaging",
                table: "conversation_messages");

            migrationBuilder.AddColumn<List<string>>(
                name: "attachments",
                schema: "messaging",
                table: "conversation_messages",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }
    }
}
