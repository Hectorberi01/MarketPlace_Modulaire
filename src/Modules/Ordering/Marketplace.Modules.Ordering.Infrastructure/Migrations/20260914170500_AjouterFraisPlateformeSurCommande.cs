using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Marketplace.Modules.Ordering.Infrastructure.Persistence;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations;

/// <summary>
/// Ajoute le frais de service de la plateforme sur la commande : son MONTANT et le
/// TAUX qui l'a produit.
///
/// <para>
/// LES COMMANDES EXISTANTES RESTENT A ZERO, ET C'EST LA SEULE REPRISE HONNETE. Elles
/// ont ete payees sous l'ancien modele, ou la remuneration de la plateforme etait deja
/// contenue dans le prix de chaque article. Leur poser 2 % apres coup inventerait un
/// frais que l'acheteur n'a jamais paye, et rendrait faux le total deja encaisse comme
/// toute facture reimprimee.
/// </para>
///
/// <para>
/// DEUX COLONNES ET NON UNE. Le taux seul ne suffit pas — un recalcul futur sur des
/// lignes remisees ne redonnerait pas forcement le meme centime. Le montant seul ne
/// suffit pas non plus : sans le taux, plus personne ne sait sous quel bareme la
/// commande a ete passee. C'est la meme paire que TaxTotal / TaxRate, pour la meme
/// raison.
/// </para>
///
/// <para>
/// `defaultValue` puis on garde le defaut : contrairement aux taux d'offre, ce defaut
/// a un sens durable — une commande sans frais est une commande a zero pour cent, pas
/// une anomalie. Cela permet aussi d'ajouter la colonne NOT NULL sur une table peuplee
/// sans reecrire la table.
/// </para>
/// </summary>
[DbContext(typeof(OrderingDbContext))]
[Migration("20260914170500_AjouterFraisPlateformeSurCommande")]
public partial class AjouterFraisPlateformeSurCommande : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PlatformFee", schema: "ordering", table: "orders",
            type: "numeric(18,2)", nullable: false, defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "PlatformFeeRate", schema: "ordering", table: "orders",
            type: "numeric(9,5)", nullable: false, defaultValue: 0m);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PlatformFee", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "PlatformFeeRate", schema: "ordering", table: "orders");
    }
}
