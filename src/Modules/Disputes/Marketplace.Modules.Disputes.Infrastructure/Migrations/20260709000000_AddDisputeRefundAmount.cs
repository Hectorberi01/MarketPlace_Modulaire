using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Marketplace.Modules.Disputes.Infrastructure.Persistence;

#nullable disable

namespace Marketplace.Modules.Disputes.Infrastructure.Migrations;

/// <summary>
/// Ajoute la colonne « RefundAmount » (montant du remboursement partiel) à la
/// table disputes.disputes. Migration écrite à la main (pattern du dépôt) :
/// attributs [DbContext]/[Migration] pour la découverte au runtime, appliquée par
/// MigrateAsync au démarrage.
/// </summary>
[DbContext(typeof(DisputesDbContext))]
[Migration("20260709000000_AddDisputeRefundAmount")]
public partial class AddDisputeRefundAmount : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "RefundAmount",
            schema: "disputes",
            table: "disputes",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RefundAmount",
            schema: "disputes",
            table: "disputes");
    }
}
