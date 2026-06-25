using FluentAssertions;
using Marketplace.Modules.Catalog.Domain.Products;
using Xunit;

namespace Catalog.UnitTests;

public class ProductVariantAndMediaTests
{
    private static Product NewProduct()
        => Product.Create(Guid.NewGuid(), Guid.NewGuid(), "T-shirt", "Coton bio").Value;

    [Fact]
    public void AddVariant_ajoute_une_variante_avec_dimensions()
    {
        var product = NewProduct();

        var result = product.AddVariant(
            "TSHIRT-RED-M",
            new Dictionary<string, string> { ["color"] = "red", ["size"] = "M" },
            barcode: "123456789012",
            weightGrams: 180,
            lengthMm: 300, widthMm: 200, heightMm: 20);

        result.IsSuccess.Should().BeTrue();
        product.Variants.Should().ContainSingle();
        result.Value.Sku.Value.Should().Be("TSHIRT-RED-M");
        result.Value.Dimensions!.LengthMm.Should().Be(300);
    }

    [Fact]
    public void AddVariant_refuse_un_sku_duplique()
    {
        var product = NewProduct();
        product.AddVariant("DUP-1", null, null, 100);

        var result = product.AddVariant("dup-1", null, null, 100);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.variant.sku_duplicate");
    }

    [Fact]
    public void AddMedia_premier_media_devient_principal()
    {
        var product = NewProduct();

        var first = product.AddMedia("https://cdn/1.jpg", ProductMediaType.Image, "vue 1", isPrimary: false);
        var second = product.AddMedia("https://cdn/2.jpg", ProductMediaType.Image, "vue 2", isPrimary: false);

        first.Value.IsPrimary.Should().BeTrue();
        second.Value.IsPrimary.Should().BeFalse();
        product.Media.Should().HaveCount(2);
    }

    [Fact]
    public void AddMedia_nouveau_principal_remplace_lancien()
    {
        var product = NewProduct();
        var first = product.AddMedia("https://cdn/1.jpg", ProductMediaType.Image, "vue 1", isPrimary: true);

        var second = product.AddMedia("https://cdn/2.jpg", ProductMediaType.Image, "vue 2", isPrimary: true);

        first.Value.IsPrimary.Should().BeFalse();
        second.Value.IsPrimary.Should().BeTrue();
    }
}
