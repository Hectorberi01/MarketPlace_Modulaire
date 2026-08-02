using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Returns.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReturnRefundableAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundableAmount",
                schema: "returns",
                table: "return_requests",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundableAmount",
                schema: "returns",
                table: "return_requests");
        }
    }
}
