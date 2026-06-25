using FluentAssertions;
using Marketplace.Modules.Reviews.Domain.Reviews;
using Xunit;

namespace Reviews.UnitTests;

public class ReviewAggregateTests
{
    private static Review NewReview(int rating = 5)
        => Review.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Rating.Create(rating).Value, "Super", "Très bon produit", isVerifiedPurchase: true).Value;

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_out_of_range_is_invalid(int value)
        => Rating.Create(value).IsFailure.Should().BeTrue();

    [Fact]
    public void Created_review_is_published_and_verified()
    {
        var review = NewReview();

        review.Status.Should().Be(ReviewStatus.Published);
        review.IsVerifiedPurchase.Should().BeTrue();
        review.Rating.Value.Should().Be(5);
    }

    [Fact]
    public void Reject_then_restore_round_trips_status()
    {
        var review = NewReview();

        review.Reject().IsSuccess.Should().BeTrue();
        review.Status.Should().Be(ReviewStatus.Rejected);

        review.Restore().IsSuccess.Should().BeTrue();
        review.Status.Should().Be(ReviewStatus.Published);
    }

    [Fact]
    public void Create_without_body_fails()
    {
        var result = Review.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Rating.Create(4).Value, "Titre", "  ", isVerifiedPurchase: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reviews.body_required");
    }
}
