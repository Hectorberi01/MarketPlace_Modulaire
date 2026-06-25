using FluentAssertions;
using Marketplace.Modules.Catalog.Domain.Products;
using Xunit;

namespace Catalog.UnitTests;

public class SlugTests
{
    [Theory]
    [InlineData("Chaussures Nike Air", "chaussures-nike-air")]
    [InlineData("  Téléphone  Samsung  ", "telephone-samsung")]
    [InlineData("Café & Thé (Bénin)", "cafe-the-benin")]
    public void Create_normalise_le_slug(string input, string expected)
    {
        var result = Slug.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejette_une_entree_vide(string input)
    {
        var result = Slug.Create(input);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.slug.empty");
    }
}
