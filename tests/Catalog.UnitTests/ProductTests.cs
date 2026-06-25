using FluentAssertions;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Domain.Products.Events;
using Xunit;

namespace Catalog.UnitTests;

public class ProductTests
{
    [Fact]
    public void Create_reussit_et_leve_un_domain_event()
    {
        var sellerId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var result = Product.Create(sellerId, categoryId, "Sac à main cuir", "Beau sac");

        result.IsSuccess.Should().BeTrue();
        var product = result.Value;
        product.Status.Should().Be(ProductStatus.Draft);
        product.Slug.Value.Should().Be("sac-a-main-cuir");
        product.DomainEvents.Should().ContainSingle(e => e is ProductCreatedDomainEvent);
    }

    [Fact]
    public void Create_echoue_sans_vendeur()
    {
        var result = Product.Create(Guid.Empty, Guid.NewGuid(), "Article", "Desc");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.product.seller_required");
    }

    [Fact]
    public void Activate_passe_de_Draft_a_Active()
    {
        var product = Product.Create(Guid.NewGuid(), Guid.NewGuid(), "Article", "Desc").Value;

        var result = product.Activate();

        result.IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Active);
    }
}
