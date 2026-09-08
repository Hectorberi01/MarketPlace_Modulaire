using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Marketplace.Modules.Ordering.Infrastructure.Persistence;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations;

/// <summary>
/// Fige le barème (commission plateforme + frais provider) sur la ligne de commande.
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
[DbContext(typeof(OrderingDbContext))]
[Migration("20260908090000_FreezePricingRatesOnOrderLine")]
public partial class FreezePricingRatesOnOrderLine : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // defaultValue puis DROP DEFAULT : c'est ce qui permet d'ajouter une colonne
        // NOT NULL sur une table peuplee sans reecrire la table (PostgreSQL 11+).
        migrationBuilder.AddColumn<decimal>(
            name: "CommissionRate", schema: "ordering", table: "order_lines",
            type: "numeric(6,4)", nullable: false, defaultValue: 0.10m);

        migrationBuilder.AddColumn<decimal>(
            name: "ProviderFeeRate", schema: "ordering", table: "order_lines",
            type: "numeric(6,4)", nullable: false, defaultValue: 0.05m);

        migrationBuilder.AlterColumn<decimal>(
            name: "CommissionRate", schema: "ordering", table: "order_lines",
            type: "numeric(6,4)", nullable: false,
            oldClrType: typeof(decimal), oldType: "numeric(6,4)", oldDefaultValue: 0.10m);

        migrationBuilder.AlterColumn<decimal>(
            name: "ProviderFeeRate", schema: "ordering", table: "order_lines",
            type: "numeric(6,4)", nullable: false,
            oldClrType: typeof(decimal), oldType: "numeric(6,4)", oldDefaultValue: 0.05m);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CommissionRate", schema: "ordering", table: "order_lines");
        migrationBuilder.DropColumn(name: "ProviderFeeRate", schema: "ordering", table: "order_lines");
    }
}
