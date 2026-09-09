using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRefundChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "settlement",
                table: "customer_refunds",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Cash");

            migrationBuilder.CreateIndex(
                name: "IX_customer_refunds_OrderId_Channel",
                schema: "settlement",
                table: "customer_refunds",
                columns: new[] { "OrderId", "Channel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customer_refunds_OrderId_Channel",
                schema: "settlement",
                table: "customer_refunds");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "settlement",
                table: "customer_refunds");
        }
    }
}
