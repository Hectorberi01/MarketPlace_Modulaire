using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HbaExpress.Dispatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchOutbound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbound_messages",
                schema: "dispatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_messages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_dead_lettered",
                schema: "dispatch",
                table: "outbound_messages",
                column: "DeadLetteredAtUtc",
                filter: "\"DeadLetteredAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbound_messages_pending",
                schema: "dispatch",
                table: "outbound_messages",
                column: "NextAttemptAtUtc",
                filter: "\"SentAtUtc\" IS NULL AND \"DeadLetteredAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbound_messages",
                schema: "dispatch");
        }
    }
}
