using FluentAssertions;
using Marketplace.Modules.Reviews.Domain.Reviews;
using Marketplace.Modules.Reviews.Domain.Reviews.Events;
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

    /// <summary>
    /// Le signalement est un SIGNAL, pas une sanction : il n'émet aucun événement de
    /// retrait. C'est `ReviewRejectedDomainEvent` qui fait recalculer la note du vendeur
    /// (`RefreshSellerRatingOnReviewRejectedHandler`) ; si `Flag()` l'émettait — ou si le
    /// statut `Flagged` sortait des lectures publiques — n'importe quel compte
    /// authentifié ferait tomber la note d'un produit, avis par avis.
    /// </summary>
    [Fact]
    public void Flag_marks_for_review_without_raising_a_rejection()
    {
        var review = NewReview();
        review.ClearDomainEvents();

        review.Flag().IsSuccess.Should().BeTrue();

        review.Status.Should().Be(ReviewStatus.Flagged);
        review.DomainEvents.Should().NotContain(e => e is ReviewRejectedDomainEvent);
    }

    /// <summary>Signaler deux fois ne fait rien de plus : le geste est sans effet cumulatif.</summary>
    [Fact]
    public void Flag_twice_is_idempotent()
    {
        var review = NewReview();

        review.Flag().IsSuccess.Should().BeTrue();
        review.Flag().IsSuccess.Should().BeTrue();

        review.Status.Should().Be(ReviewStatus.Flagged);
    }

    /// <summary>Un modérateur lève le signalement : l'avis redevient simplement publié.</summary>
    [Fact]
    public void Flag_then_restore_round_trips_status()
    {
        var review = NewReview();

        review.Flag().IsSuccess.Should().BeTrue();

        review.Restore().IsSuccess.Should().BeTrue();
        review.Status.Should().Be(ReviewStatus.Published);
    }

    /// <summary>Un avis déjà rejeté par la modération ne se signale plus.</summary>
    [Fact]
    public void Flag_on_rejected_review_fails()
    {
        var review = NewReview();
        review.Reject().IsSuccess.Should().BeTrue();

        var result = review.Flag();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reviews.rejected");
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
