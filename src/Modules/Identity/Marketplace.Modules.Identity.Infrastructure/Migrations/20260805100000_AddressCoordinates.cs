using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations;

/// <summary>
/// Position GPS facultative sur l'adresse de livraison.
///
/// <para>
/// Deux colonnes nullables, aucun rattrapage : personne n'avait de position, et il
/// n'existe aucun moyen d'en déduire une depuis un point de repère. Les adresses
/// existantes restent parfaitement livrables — la position COMPLÈTE le repère, elle
/// ne le remplace pas.
/// </para>
///
/// <para>
/// Pas d'index : rien ne requête par coordonnées aujourd'hui. Un index spatial
/// supposerait PostGIS, que cette base n'a pas et dont le seul usage prévu — ouvrir
/// un point dans une application de cartographie — n'a aucun besoin.
/// </para>
/// </summary>
public partial class AddressCoordinates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "Latitude", schema: "identity", table: "addresses",
            type: "double precision", nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Longitude", schema: "identity", table: "addresses",
            type: "double precision", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Latitude", schema: "identity", table: "addresses");
        migrationBuilder.DropColumn(name: "Longitude", schema: "identity", table: "addresses");
    }
}
