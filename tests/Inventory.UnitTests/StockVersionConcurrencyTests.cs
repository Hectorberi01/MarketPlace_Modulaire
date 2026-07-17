using FluentAssertions;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock;
using Xunit;

namespace Inventory.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// CES TESTS GARDENT LE VERROU OPTIMISTE. ILS NE TESTENT PAS UN COMPTEUR.
///
/// `InventoryItem` porte `UseXminAsConcurrencyToken()`. Mais un jeton de concurrence
/// n'est évalué QUE dans la clause WHERE d'un UPDATE. Or `Reserve()` et
/// `ReleaseReservation()` ne modifient aucune colonne de la table parente : ils
/// insèrent ou suppriment des lignes enfants. Sans une colonne du parent qui change,
/// EF n'émet aucun UPDATE, le `xmin` n'est jamais confronté, et deux réservations
/// concurrentes du dernier article passent toutes les deux. SURVENTE.
///
/// `StockVersion` est cette colonne. Chaque mutation l'incrémente (`Touch()`).
///
/// La conséquence pour qui lira ces tests un jour : si vous ajoutez une mutation à
/// `InventoryItem` SANS appeler `Touch()`, rien ne cassera visiblement — les tests
/// existants passeront, l'API répondra 200, et le verrou sera mort sur ce chemin-là.
/// C'est exactement le genre de panne qu'on ne découvre qu'en lisant un rapport de
/// stock négatif. D'où ces tests : ils rendent l'oubli bruyant.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class StockVersionConcurrencyTests
{
    private static InventoryItem NewItem(int onHand = 10, int threshold = 2)
        => InventoryItem.Create(Sku.Create("SKU-CONC").Value, Guid.NewGuid(), onHand, threshold).Value;

    [Fact]
    public void Reserve_incremente_StockVersion__sinon_aucun_UPDATE_et_le_verrou_est_mort()
    {
        var item = NewItem(onHand: 1);
        var before = item.StockVersion;

        item.Reserve(Guid.NewGuid(), 1, DateTime.UtcNow.AddMinutes(15)).IsSuccess.Should().BeTrue();

        item.StockVersion.Should().Be(before + 1,
            "sans cet incrément EF n'émet pas d'UPDATE sur inventory_items, "
            + "la clause `AND xmin = …` n'est jamais évaluée, et le dernier article se vend deux fois");
    }

    [Fact]
    public void ReleaseReservation_incremente_StockVersion()
    {
        var item = NewItem(onHand: 5);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 2, DateTime.UtcNow.AddMinutes(15));
        var before = item.StockVersion;

        item.ReleaseReservation(orderId).IsSuccess.Should().BeTrue();

        item.StockVersion.Should().Be(before + 1);
    }

    [Fact]
    public void ConfirmReservation_incremente_StockVersion()
    {
        var item = NewItem(onHand: 5);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 2, DateTime.UtcNow.AddMinutes(15));
        var before = item.StockVersion;

        item.ConfirmReservation(orderId).IsSuccess.Should().BeTrue();

        item.StockVersion.Should().Be(before + 1);
    }

    [Fact]
    public void Receive_incremente_StockVersion()
    {
        var item = NewItem(onHand: 5);
        var before = item.StockVersion;

        item.Receive(3).IsSuccess.Should().BeTrue();

        item.StockVersion.Should().Be(before + 1);
    }

    [Fact]
    public void AdjustOnHand_incremente_StockVersion()
    {
        var item = NewItem(onHand: 5);
        var before = item.StockVersion;

        item.AdjustOnHand(-1).IsSuccess.Should().BeTrue();

        item.StockVersion.Should().Be(before + 1);
    }

    [Fact]
    public void SetReorderThreshold_incremente_StockVersion()
    {
        var item = NewItem();
        var before = item.StockVersion;

        item.SetReorderThreshold(7).IsSuccess.Should().BeTrue();

        item.StockVersion.Should().Be(before + 1);
    }

    [Fact]
    public void Une_mutation_REFUSEE_ne_touche_pas_StockVersion()
    {
        // Une opération rejetée ne doit pas salir la ligne : sinon on générerait des
        // conflits 409 pour des requêtes qui n'ont, par définition, rien écrit.
        var item = NewItem(onHand: 1);
        var before = item.StockVersion;

        item.Reserve(Guid.NewGuid(), 5, DateTime.UtcNow.AddMinutes(15)).IsFailure.Should().BeTrue();
        item.Receive(0).IsFailure.Should().BeTrue();
        item.SetReorderThreshold(-1).IsFailure.Should().BeTrue();

        item.StockVersion.Should().Be(before, "un échec n'écrit rien, donc ne doit rien verrouiller");
    }

    [Fact]
    public void Chaque_mutation_successive_avance_le_compteur__jamais_de_stagnation()
    {
        // Deux réservations d'affilée doivent produire DEUX versions distinctes.
        // Si le compteur stagnait, deux transactions concurrentes pourraient écrire la
        // même valeur et l'UPDATE resterait « propre » aux yeux d'EF.
        var item = NewItem(onHand: 10);

        item.Reserve(Guid.NewGuid(), 1, DateTime.UtcNow.AddMinutes(15));
        item.Reserve(Guid.NewGuid(), 1, DateTime.UtcNow.AddMinutes(15));
        item.Receive(1);

        item.StockVersion.Should().Be(3);
    }
}
