using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Messaging.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MessageAttachmentsChildTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Les pièces jointes deviennent une TABLE ENFANT (comme les réactions), au lieu
            // d'une colonne tableau/JSON qu'EF Core 8 relisait vide. La colonne `attachments`
            // n'ayant jamais rien restitué correctement, on la supprime.
            migrationBuilder.DropColumn(
                name: "attachments",
                schema: "messaging",
                table: "conversation_messages");

            migrationBuilder.CreateTable(
                name: "message_attachments",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_message_attachments_conversation_messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "messaging",
                        principalTable: "conversation_messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_MessageId",
                schema: "messaging",
                table: "message_attachments",
                column: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_attachments",
                schema: "messaging");

            migrationBuilder.AddColumn<string>(
                name: "attachments",
                schema: "messaging",
                table: "conversation_messages",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }
    }
}
