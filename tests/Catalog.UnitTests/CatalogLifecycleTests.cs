using FluentAssertions;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;
using Xunit;

namespace Catalog.UnitTests;

public class CatalogLifecycleTests
{
    [Fact]
    public void Category_demarre_en_Draft_puis_se_publie()
    {
        var category = Category.Create("Maison").Value;
        category.Status.Should().Be(CategoryStatus.Draft);

        category.Publish().IsSuccess.Should().BeTrue();
        category.Status.Should().Be(CategoryStatus.Published);
    }

    [Fact]
    public void Category_archivee_ne_peut_pas_etre_publiee()
    {
        var category = Category.Create("Jardin").Value;
        category.Archive();

        var result = category.Publish();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.category.archived");
    }

    [Fact]
    public void Product_Archive_change_le_statut()
    {
        var product = Product.Create(Guid.NewGuid(), Guid.NewGuid(), "Lampe", "LED").Value;

        product.Archive().IsSuccess.Should().BeTrue();
        product.Status.Should().Be(ProductStatus.Archived);
    }

    [Fact]
    public void Product_Update_recalcule_le_slug()
    {
        var product = Product.Create(Guid.NewGuid(), Guid.NewGuid(), "Ancien nom", "x").Value;

        var result = product.Update("Nouveau Nom", "desc", null, null, null, null, null, null);

        result.IsSuccess.Should().BeTrue();
        product.Slug.Value.Should().Be("nouveau-nom");
    }

    [Fact]
    public void Brand_Update_change_nom_et_slug()
    {
        var brand = Brand.Create("Old").Value;

        brand.Update("New Brand", null, null).IsSuccess.Should().BeTrue();
        brand.Slug.Value.Should().Be("new-brand");
    }
}
