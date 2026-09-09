using FluentAssertions;
using Marketplace.Modules.Catalog.Domain.Products;
using Xunit;

namespace Catalog.UnitTests;

/// <summary>
/// NON-RÉGRESSION §7.5 — un produit rejeté doit pouvoir être approuvé après correction.
///
/// Le rejet écrivait `Archived`, et `Activate` refuse un produit archivé : le couple
/// « rejeter -> corriger -> approuver », geste naturel du modérateur, renvoyait 409.
/// La modération renvoie désormais en brouillon ; l'archivage reste possible, mais il
/// est le RETRAIT du catalogue, et sa sortie est explicite.
/// </summary>
public class ModerationProduitTests
{
    private static Product NewProduct()
        => Product.Create(Guid.NewGuid(), Guid.NewGuid(), "Sac en raphia", "Fait main").Value;

    [Fact]
    public void Rejeter_puis_corriger_puis_approuver_fonctionne()
    {
        var product = NewProduct();
        product.Activate();

        // Le rejet de modération : retour en brouillon.
        product.Unarchive().IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Draft);

        // Le vendeur corrige, le modérateur approuve.
        product.Update("Sac en raphia tressé", "Fait main à Cotonou", null, null, null, null, null, null)
            .IsSuccess.Should().BeTrue();
        product.Activate().IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Active);
    }

    [Fact]
    public void Un_produit_archive_ne_se_publie_pas_directement_mais_le_message_dit_la_sortie()
    {
        var product = NewProduct();
        product.Archive();

        var refus = product.Activate();

        refus.IsFailure.Should().BeTrue();
        refus.Error.Code.Should().Be("catalog.product.archived");
        refus.Error.Message.Should().Contain("brouillon");

        // Et la sortie existe bel et bien.
        product.Unarchive().IsSuccess.Should().BeTrue();
        product.Activate().IsSuccess.Should().BeTrue();
    }
}
