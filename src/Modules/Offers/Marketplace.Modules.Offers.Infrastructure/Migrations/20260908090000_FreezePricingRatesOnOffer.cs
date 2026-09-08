using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Marketplace.Modules.Offers.Infrastructure.Persistence;

#nullable disable

namespace Marketplace.Modules.Offers.Infrastructure.Migrations;

/// <summary>
/// Fige le barème (commission plateforme + frais provider) sur l'offre.
///
/// <para>
/// POURQUOI. Le prix acheteur est construit comme prix_vendeur x (1 + commission +
/// provider). Settlement refaisait le chemin a l'envers en relisant les taux dans la
/// CONFIGURATION, au moment de la confirmation de commande. Tant que le bareme ne
/// bougeait pas, les deux coincidaient. Le jour ou la commission passait de 10 % a
/// 12 %, toutes les offres deja tarifees a 10 % etaient reparties a 12 % : le vendeur
/// touchait moins que le net qu'il avait fixe, retroactivement, et sans aucune trace.
/// </para>
///
/// <para>
/// REPRISE DES DONNEES. Les lignes existantes recoivent le bareme historique 10 % / 5 %.
/// REMPLACER CES VALEURS si le bareme reellement applique en production a une autre
/// periode etait different : ces colonnes decrivent le passe, pas la configuration
/// actuelle.
/// </para>
/// </summary>
[DbContext(typeof(OffersDbContext))]
[Migration("20260908090000_FreezePricingRatesOnOffer")]
public partial class FreezePricingRatesOnOffer : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // defaultValue puis DROP DEFAULT : c'est ce qui permet d'ajouter une colonne
        // NOT NULL sur une table peuplee sans reecrire la table (PostgreSQL 11+).
        migrationBuilder.AddColumn<decimal>(
            name: "CommissionRate", schema: "offers", table: "offers",
            type: "numeric(6,4)", nullable: false, defaultValue: 0.10m);

        migrationBuilder.AddColumn<decimal>(
            name: "ProviderFeeRate", schema: "offers", table: "offers",
            type: "numeric(6,4)", nullable: false, defaultValue: 0.05m);

        // Les offres existantes portent deja les MONTANTS produits par le bareme : on en
        // deduit les taux reellement appliques, plutot que de leur imposer le bareme par
        // defaut. Une offre a 10 000 net avec 1 200 de commission a bien ete tarifee a
        // 12 %, et c'est cela qu'il faut conserver.
        migrationBuilder.Sql(@"
            UPDATE offers.offers
            SET ""CommissionRate""  = ROUND(""CommissionAmount""  / ""SellerPrice"", 4),
                ""ProviderFeeRate"" = ROUND(""ProviderFeeAmount"" / ""SellerPrice"", 4)
            WHERE ""SellerPrice"" > 0;");

        migrationBuilder.AlterColumn<decimal>(
            name: "CommissionRate", schema: "offers", table: "offers",
            type: "numeric(6,4)", nullable: false,
            oldClrType: typeof(decimal), oldType: "numeric(6,4)", oldDefaultValue: 0.10m);

        migrationBuilder.AlterColumn<decimal>(
            name: "ProviderFeeRate", schema: "offers", table: "offers",
            type: "numeric(6,4)", nullable: false,
            oldClrType: typeof(decimal), oldType: "numeric(6,4)", oldDefaultValue: 0.05m);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CommissionRate", schema: "offers", table: "offers");
        migrationBuilder.DropColumn(name: "ProviderFeeRate", schema: "offers", table: "offers");
    }
}
