using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Messaging.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialMessaging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "messaging");

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "messaging",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Content = table.Column<string>(type: "jsonb", nullable: false),
                OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_outbox_messages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "conversations",
            schema: "messaging",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ContextType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ContextId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                LastMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_conversations", x => x.Id));

        migrationBuilder.CreateTable(
            name: "conversation_participants",
            schema: "messaging",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_conversation_participants", x => x.Id);
                table.ForeignKey(
                    name: "FK_conversation_participants_conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalSchema: "messaging",
                    principalTable: "conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "conversation_messages",
            schema: "messaging",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                attachments = table.Column<List<string>>(type: "text[]", nullable: false),
                ConversationId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_conversation_messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_conversation_messages_conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalSchema: "messaging",
                    principalTable: "conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_ProcessedOnUtc",
            schema: "messaging", table: "outbox_messages", column: "ProcessedOnUtc");

        migrationBuilder.CreateIndex(
            name: "IX_conversation_participants_ConversationId",
            schema: "messaging", table: "conversation_participants", column: "ConversationId");

        migrationBuilder.CreateIndex(
            name: "IX_conversation_participants_UserId",
            schema: "messaging", table: "conversation_participants", column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_conversation_messages_ConversationId",
            schema: "messaging", table: "conversation_messages", column: "ConversationId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "messaging");
        migrationBuilder.DropTable(name: "conversation_participants", schema: "messaging");
        migrationBuilder.DropTable(name: "conversation_messages", schema: "messaging");
        migrationBuilder.DropTable(name: "conversations", schema: "messaging");
    }
}
