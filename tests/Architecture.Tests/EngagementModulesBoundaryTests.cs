using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;
using WishlistAggregate = Marketplace.Modules.Wishlist.Domain.Wishlists.Wishlist;

namespace Architecture.Tests;

/// <summary>Frontières des modules d'engagement : Messaging, Wishlist, Loyalty.</summary>
public class EngagementModulesBoundaryTests
{
    private static readonly Assembly MessagingDomain =
        typeof(Marketplace.Modules.Messaging.Domain.Conversations.Conversation).Assembly;

    private static readonly Assembly MessagingApplication =
        typeof(Marketplace.Modules.Messaging.Application.Conversations.StartConversationCommand).Assembly;

    private static readonly Assembly WishlistDomain = typeof(WishlistAggregate).Assembly;

    private static readonly Assembly WishlistApplication =
        typeof(Marketplace.Modules.Wishlist.Application.Wishlists.AddToWishlistCommand).Assembly;

    private static readonly Assembly LoyaltyDomain =
        typeof(Marketplace.Modules.Loyalty.Domain.Accounts.LoyaltyAccount).Assembly;

    private static readonly Assembly LoyaltyApplication =
        typeof(Marketplace.Modules.Loyalty.Application.Accounts.RedeemPointsCommand).Assembly;

    [Fact]
    public void Domains_ne_dependent_pas_de_EFCore()
    {
        AssertNoEf(MessagingDomain);
        AssertNoEf(WishlistDomain);
        AssertNoEf(LoyaltyDomain);
    }

    [Fact]
    public void Applications_ne_dependent_ni_de_EFCore_ni_de_leur_Infrastructure()
    {
        AssertNoEfNorInfra(MessagingApplication, "Marketplace.Modules.Messaging.Infrastructure");
        AssertNoEfNorInfra(WishlistApplication, "Marketplace.Modules.Wishlist.Infrastructure");
        AssertNoEfNorInfra(LoyaltyApplication, "Marketplace.Modules.Loyalty.Infrastructure");
    }

    private static void AssertNoEf(Assembly assembly)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();

    private static void AssertNoEfNorInfra(Assembly assembly, string infrastructureNamespace)
        => Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", infrastructureNamespace)
            .GetResult().IsSuccessful.Should().BeTrue();
}
