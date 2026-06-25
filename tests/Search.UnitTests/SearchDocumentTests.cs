using FluentAssertions;
using Marketplace.Modules.Search.Domain.Documents;
using Xunit;

namespace Search.UnitTests;

public class SearchDocumentTests
{
    private static SearchDocument NewDoc()
        => SearchDocument.FromProduct(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Téléphone", "telephone");

    [Fact]
    public void ApplyOfferPrice_keeps_the_lowest_price()
    {
        var doc = NewDoc();

        doc.ApplyOfferPrice(7000m, "XOF");
        doc.ApplyOfferPrice(6500m, "XOF");
        doc.ApplyOfferPrice(8000m, "XOF");

        doc.MinPrice.Should().Be(6500m);
        doc.Currency.Should().Be("XOF");
    }

    [Fact]
    public void ApplyRating_sets_average_and_count()
    {
        var doc = NewDoc();

        doc.ApplyRating(4.5d, 12);

        doc.AverageRating.Should().Be(4.5d);
        doc.ReviewCount.Should().Be(12);
    }

    [Fact]
    public void UpdateProduct_refreshes_denormalized_fields()
    {
        var doc = NewDoc();
        var newCategory = Guid.NewGuid();

        doc.UpdateProduct(doc.SellerId, newCategory, "Smartphone", "smartphone");

        doc.Name.Should().Be("Smartphone");
        doc.CategoryId.Should().Be(newCategory);
    }
}
