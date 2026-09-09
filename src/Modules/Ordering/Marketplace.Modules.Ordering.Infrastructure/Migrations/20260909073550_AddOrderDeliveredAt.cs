using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Ajoute `orders.DeliveredAtUtc` — point de départ du délai de retour.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION A ÉTÉ GÉNÉRÉE AVEC UNE COLONNE DE TROP, RETIRÉE À LA MAIN.
    ///
    /// EF y avait ajouté `WalletApplied`, qui existe pourtant en base depuis
    /// `20260908160000_AddOrderWalletApplied`. La raison : cette migration-là a été
    /// écrite à la main, SANS son fichier `.Designer.cs`. Or c'est le Designer qui met à
    /// jour l'instantané du modèle (`OrderingDbContextModelSnapshot`) — celui que le
    /// générateur compare au modèle courant pour calculer la différence.
    ///
    /// L'instantané ignorait donc `WalletApplied`, et la génération suivante a
    /// « redécouvert » la colonne. En base elle existait déjà : 42701, colonne en double.
    ///
    /// LA DETTE EST SOLDÉE ICI : l'instantané régénéré avec cette migration contient
    /// désormais les deux colonnes. Les prochaines générations partiront d'un état juste.
    ///
    /// À RETENIR : une migration écrite à la main sans son Designer laisse l'instantané
    /// en arrière, et la facture est présentée à la migration suivante — pas à celui qui
    /// l'a écrite.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class AddOrderDeliveredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAtUtc",
                schema: "ordering",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                schema: "ordering",
                table: "orders");
        }
    }
}
