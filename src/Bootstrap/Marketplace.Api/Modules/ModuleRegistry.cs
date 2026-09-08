using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.Modules.Billing.Infrastructure;
using Marketplace.Modules.Catalog.Infrastructure;
using Marketplace.Modules.Disputes.Infrastructure;
using Marketplace.Modules.Identity.Infrastructure;
using Marketplace.Modules.Cart.Infrastructure;
using Marketplace.Modules.Inventory.Infrastructure;
using Marketplace.Modules.Loyalty.Infrastructure;
using Marketplace.Modules.Marketing.Infrastructure;
using Marketplace.Modules.Messaging.Infrastructure;
using Marketplace.Modules.Notifications.Infrastructure;
using Marketplace.Modules.Offers.Infrastructure;
using Marketplace.Modules.Ordering.Infrastructure;
using Marketplace.Modules.Analytics.Infrastructure;
using Marketplace.Modules.Fraud.Infrastructure;
using Marketplace.Modules.Recommendations.Infrastructure;
using Marketplace.Modules.Payments.Infrastructure;
using Marketplace.Modules.Pricing.Infrastructure;
using Marketplace.Modules.Returns.Infrastructure;
using Marketplace.Modules.Reviews.Infrastructure;
using Marketplace.Modules.Search.Infrastructure;
using Marketplace.Modules.Sellers.Infrastructure;
using Marketplace.Modules.Settlement.Infrastructure;
using Marketplace.Modules.Delivery.Infrastructure;
using Marketplace.Modules.Shipping.Infrastructure;
using Marketplace.Modules.Tax.Infrastructure;
using Marketplace.Modules.Wishlist.Infrastructure;

namespace Marketplace.Api.Modules;

/// <summary>
/// Liste des modules composés dans ce déploiement. C'est le SEUL endroit qui
/// connaît l'ensemble des modules. Ajouter un module = ajouter son installer ici
/// (et référencer son projet Infrastructure dans le .csproj).
/// </summary>
public static class ModuleRegistry
{
    public static IReadOnlyList<IModuleInstaller> Installers { get; } = new IModuleInstaller[]
    {
        new IdentityModuleInstaller(),
        new CatalogModuleInstaller(),
        new SellersModuleInstaller(),
        new OffersModuleInstaller(),
        new InventoryModuleInstaller(),
        new PricingModuleInstaller(),
        new CartModuleInstaller(),
        new OrderingModuleInstaller(),
        new PaymentsModuleInstaller(),
        new ShippingModuleInstaller(),
        // Identité du coursier côté marketplace (paiement). Le dispatch et la géo
        // vivent dans le service autonome — voir docs/CDC_Service_Livraison_Coursier.md.
        new DeliveryModuleInstaller(),
        new ReviewsModuleInstaller(),
        new SearchModuleInstaller(),
        new NotificationsModuleInstaller(),
        new BillingModuleInstaller(),
        new SettlementModuleInstaller(),
        new ReturnsModuleInstaller(),
        new DisputesModuleInstaller(),
        new MessagingModuleInstaller(),
        new WishlistModuleInstaller(),
        new LoyaltyModuleInstaller(),
        new TaxModuleInstaller(),
        new MarketingModuleInstaller(),
        new RecommendationsModuleInstaller(),
        new AnalyticsModuleInstaller(),
        new FraudModuleInstaller()
        // ...
    };
}
