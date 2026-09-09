using FluentAssertions;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock;
using Xunit;

namespace Inventory.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// L'EXPIRATION GOUVERNE CE QU'ON PEUT PROMETTRE, PAS CE QU'ON DOIT.
///
/// `StockReservation.ExpiresAtUtc` était écrite à chaque réservation et lue par
/// personne : aucune requête, aucun balayeur, aucun calcul. `Reserved` sommait toutes
/// les lignes sans distinction, si bien qu'une réservation n'était libérée que par
/// l'annulation de sa commande. Quand le balayeur des commandes abandonnées ne tournait
/// pas, le stock restait immobilisé indéfiniment — sur une référence à faible stock,
/// l'article apparaissait épuisé à tout le monde.
///
/// LA CORRECTION NE SE RÉDUIT PAS À FILTRER. Filtrer sans précaution aurait libéré le
/// stock d'une commande en cours de paiement, l'aurait laissé vendre à quelqu'un
/// d'autre, et la confirmation du payeur lent aurait fait passer le stock physique sous
/// zéro. Une survente, créée par un correctif.
///
/// D'où les deux garanties tenues ensemble ici : la disponibilité ignore les
/// réservations expirées, la CONFIRMATION les honore quand même.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class ExpirationDesReservationsTests
{
    private static InventoryItem NewItem(int onHand = 10)
        => InventoryItem.Create(Sku.Create("SKU-1").Value, Guid.NewGuid(), onHand, 2).Value;

    private static DateTime Passe => DateTime.UtcNow.AddMinutes(-1);
    private static DateTime Futur => DateTime.UtcNow.AddHours(6);

    [Fact]
    public void Une_reservation_expiree_n_immobilise_plus_de_stock()
    {
        var item = NewItem(onHand: 10);
        item.Reserve(Guid.NewGuid(), 4, Passe);

        item.Reserved.Should().Be(0);
        item.Available.Should().Be(10, "le stock d'une réservation périmée doit être revendable");
    }

    [Fact]
    public void Seules_les_reservations_vivantes_sont_comptees()
    {
        var item = NewItem(onHand: 10);
        item.Reserve(Guid.NewGuid(), 4, Passe);
        item.Reserve(Guid.NewGuid(), 3, Futur);

        item.Reserved.Should().Be(3);
        item.Available.Should().Be(7);
    }

    [Fact]
    public void Le_stock_libere_par_expiration_est_reservable_par_un_autre()
    {
        var item = NewItem(onHand: 5);
        item.Reserve(Guid.NewGuid(), 5, Passe);

        // Sans le filtre, celle-ci échouait sur « stock insuffisant » alors que la
        // première réservation n'avait plus aucune valeur.
        item.Reserve(Guid.NewGuid(), 5, Futur).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// LA GARANTIE QUI EMPÊCHE LE CORRECTIF DE CRÉER UNE SURVENTE. NE PAS FILTRER
    /// L'EXPIRATION DANS `ConfirmReservation`.
    ///
    /// Un paiement Mobile Money peut aboutir après la fenêtre. La commande est payée :
    /// le stock physique doit partir. Si la confirmation échouait,
    /// `ConfirmOrderPaymentCommandHandler` — qui ignore le résultat — la laisserait
    /// échouer EN SILENCE : client débité, stock jamais décrémenté.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Une_commande_payee_en_retard_decremente_quand_meme_le_stock()
    {
        var item = NewItem(onHand: 10);
        var commande = Guid.NewGuid();
        item.Reserve(commande, 4, Passe);

        item.ConfirmReservation(commande).IsSuccess.Should().BeTrue();
        item.OnHand.Should().Be(6);
    }

    [Fact]
    public void Une_reservation_expiree_reste_liberable_par_annulation()
    {
        var item = NewItem(onHand: 10);
        var commande = Guid.NewGuid();
        item.Reserve(commande, 4, Passe);

        // Les lignes périmées ne sont pas purgées à la volée : c'est l'annulation ou la
        // confirmation qui les solde. La libération doit donc rester idempotente.
        item.ReleaseReservation(commande).IsSuccess.Should().BeTrue();
        item.Reservations.Should().BeEmpty();
    }
}
