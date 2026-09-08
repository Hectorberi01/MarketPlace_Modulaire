using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Marketplace.Modules.Ordering.Infrastructure.Persistence;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations;

/// <summary>
/// Separe la VALEUR de la commande du MONTANT A ENCAISSER.
///
/// <para>
/// POURQUOI. GrandTotal disait jusqu'ici les deux choses a la fois, et cela tenait tant
/// qu'un seul moyen reglait la commande. La cagnotte introduit une part payee autrement :
/// le vendeur doit toujours toucher son prix, la commission se calcule toujours sur le
/// prix, mais le prestataire de paiement, lui, n'encaisse que la difference. Un seul
/// chiffre ne peut pas porter les deux sens sans que l'un des deux devienne faux.
/// </para>
///
/// <para>
/// REPRISE DES DONNEES. Les commandes existantes recoivent zero, et c'est exact : elles
/// ont toutes ete reglees integralement au prestataire. Ce n'est pas une valeur par
/// defaut faute de mieux, c'est la valeur juste.
/// </para>
/// </summary>
[DbContext(typeof(OrderingDbContext))]
[Migration("20260908160000_AddOrderWalletApplied")]
public partial class AddOrderWalletApplied : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // La valeur par defaut est CONSERVEE sur la colonne (pas de DROP DEFAULT comme
        // pour le bareme) : une commande sans cagnotte est le cas courant, et laisser la
        // base repondre zero evite qu'un futur chemin d'ecriture oublie le champ.
        migrationBuilder.AddColumn<decimal>(
            name: "WalletApplied", schema: "ordering", table: "orders",
            type: "numeric(18,2)", nullable: false, defaultValue: 0m);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "WalletApplied", schema: "ordering", table: "orders");
    }
}
