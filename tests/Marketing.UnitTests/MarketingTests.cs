using FluentAssertions;
using Marketplace.Modules.Marketing.Domain.Campaigns;
using Marketplace.Modules.Marketing.Domain.Content;
using Xunit;

namespace Marketing.UnitTests;

public class MarketingTests
{
    private static readonly DateTime Start = new(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Campaign_lifecycle_and_promotions()
    {
        var campaign = Campaign.Create("Black Friday", CampaignType.FlashSale, Start, Start.AddDays(1), null).Value;
        campaign.Status.Should().Be(CampaignStatus.Draft);

        var promo = Guid.NewGuid();
        campaign.AddPromotion(promo);
        campaign.AddPromotion(promo); // dédoublonné
        campaign.PromotionIds.Should().ContainSingle().Which.Should().Be(promo);

        campaign.Schedule().IsSuccess.Should().BeTrue();
        campaign.Activate().IsSuccess.Should().BeTrue();
        campaign.Status.Should().Be(CampaignStatus.Active);
    }

    [Fact]
    public void Campaign_with_invalid_period_fails()
        => Campaign.Create("x", CampaignType.Event, Start, Start, null).IsFailure.Should().BeTrue();

    [Fact]
    public void ContentBlock_publish_and_archive()
    {
        var block = ContentBlock.Create(ContentBlockType.Banner, "home_hero", 1, "{\"img\":\"a.jpg\"}", null, null).Value;
        block.Status.Should().Be(ContentStatus.Draft);

        block.Publish();
        block.Status.Should().Be(ContentStatus.Published);

        block.Archive();
        block.Status.Should().Be(ContentStatus.Archived);
    }

    [Fact]
    public void ContentBlock_requires_content()
        => ContentBlock.Create(ContentBlockType.Page, "slot", 0, "  ", null, null).IsFailure.Should().BeTrue();
}
