using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations;

/// <summary>
/// Position de livraison figée sur la commande.
///
/// <para>
/// Figée comme le reste de l'instantané : si l'acheteur déplace ensuite le point de
/// son adresse, la commande doit continuer de dire où le colis a été envoyé.
/// </para>
///
/// <para>
/// Nulle sur toutes les commandes antérieures, et cela le restera : la position
/// n'existait pas au moment où elles ont été passées. Le vendeur y verra simplement
/// l'absence de lien « ouvrir dans Maps », et se rabattra sur le point de repère —
/// ce qu'il faisait déjà.
/// </para>
/// </summary>
public partial class OrderShipToCoordinates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "ShipToLatitude", schema: "ordering", table: "orders",
            type: "double precision", nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "ShipToLongitude", schema: "ordering", table: "orders",
            type: "double precision", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShipToLatitude", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToLongitude", schema: "ordering", table: "orders");
    }
}
