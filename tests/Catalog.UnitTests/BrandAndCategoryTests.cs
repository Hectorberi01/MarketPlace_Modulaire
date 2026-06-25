using FluentAssertions;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Categories;
using Xunit;

namespace Catalog.UnitTests;

public class BrandAndCategoryTests
{
    [Fact]
    public void Brand_Create_demarre_en_Pending()
    {
        var result = Brand.Create("Adidas", "https://cdn/logo.png");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(BrandStatus.Pending);
        result.Value.Slug.Value.Should().Be("adidas");
    }

    [Fact]
    public void Brand_Approve_passe_en_Active()
    {
        var brand = Brand.Create("Nike").Value;

        brand.Approve().IsSuccess.Should().BeTrue();
        brand.Status.Should().Be(BrandStatus.Active);
    }

    [Fact]
    public void Category_racine_a_un_path_simple()
    {
        var result = Category.Create("Électronique");

        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/electronique");
        result.Value.ParentId.Should().BeNull();
    }

    [Fact]
    public void Category_enfant_concatene_le_path_du_parent()
    {
        var parentId = Guid.NewGuid();

        var result = Category.Create("Téléphones", parentId, "/electronique");

        result.IsSuccess.Should().BeTrue();
        result.Value.Path.Should().Be("/electronique/telephones");
        result.Value.ParentId.Should().Be(parentId);
    }
}
