using Marketplace.Host.Extensions;
using Marketplace.Host.Middleware;
using Marketplace.Host.Configuration;
using Marketplace.Host.Endpoints;
using Marketplace.Host.RealTime;
using Marketplace.Host.Client.Endpoints;
using Marketplace.Host.Seller.Endpoints;
using Marketplace.Host.Admin.Endpoints;
using Marketplace.Host.Observability;
using Marketplace.Modules.Catalog.Infrastructure.Persistence;
using Marketplace.Modules.Identity.Infrastructure.Persistence;
using Marketplace.Modules.Sellers.Infrastructure.Persistence;
using Marketplace.Modules.Offers.Infrastructure.Persistence;
using Marketplace.Modules.Inventory.Infrastructure.Persistence;
using Marketplace.Modules.Pricing.Infrastructure.Persistence;
using Marketplace.Modules.Cart.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Infrastructure.Persistence;
using Marketplace.Modules.Payments.Infrastructure.Persistence;
using Marketplace.Modules.Shipping.Infrastructure.Persistence;
using Marketplace.Modules.Reviews.Infrastructure.Persistence;
using Marketplace.Modules.Search.Infrastructure.Persistence;
using Marketplace.Modules.Notifications.Infrastructure.Persistence;
using Marketplace.Modules.Billing.Infrastructure.Persistence;
using Marketplace.Modules.Settlement.Infrastructure.Persistence;
using Marketplace.Modules.Returns.Infrastructure.Persistence;
using Marketplace.Modules.Disputes.Infrastructure.Persistence;
using Marketplace.Modules.Messaging.Infrastructure.Persistence;
using Marketplace.Modules.Wishlist.Infrastructure.Persistence;
using Marketplace.Modules.Loyalty.Infrastructure.Persistence;
using Marketplace.Modules.Tax.Infrastructure.Persistence;
using Marketplace.Modules.Marketing.Infrastructure.Persistence;
using Marketplace.Modules.Recommendations.Infrastructure.Persistence;
using Marketplace.Modules.Analytics.Infrastructure.Persistence;
using Marketplace.Modules.Fraud.Infrastructure.Persistence;
using Marketplace.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddMarketplaceApi();

var app = builder.Build();

// EN TOUT PREMIER DANS LE PIPELINE. Restaure la vraie IP du client (X-Forwarded-For) ;
// journalisation, authentification et limitation de débit lisent RemoteIpAddress.
// Plus bas, elles liraient l'IP du proxy — correction sans effet, mais d'apparence
// appliquée.
app.UseProxyForwardedHeaders();


// Webhooks PSP : n'accepter un webhook non signé (secret manquant) qu'en
// développement. En production, un secret absent fait rejeter le webhook.
Marketplace.Modules.Payments.Infrastructure.Gateways.GatewayWebhook.AllowUnsignedWhenSecretMissing
    = app.Environment.IsDevelopment();

// Service coursier : même règle, secret distinct. La route qu'il protège fait SORTIR de
// l'argent du portefeuille de la plateforme — hors développement, pas de secret, pas
// d'appel.
Marketplace.BuildingBlocks.Infrastructure.Security.PartnerServiceSignature.AllowUnsignedWhenSecretMissing
    = app.Environment.IsDevelopment();

// Migrations + seed au démarrage : pratique en dev, désactivé par défaut.
if (app.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();

    var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await catalogDb.Database.MigrateAsync();

    var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await identityDb.Database.MigrateAsync();
    // Rôles RBAC par défaut (configuration système, pas des données de démo) :
    // nécessaires pour pouvoir attribuer un rôle au premier administrateur.
    await IdentityDataSeeder.SeedDefaultRolesAsync(identityDb);

    // Amorçage du premier administrateur : sur une base neuve, créer un admin exige
    // d'être admin. Piloté par Identity:Bootstrap:AdminEmail / AdminPassword
    // (ADMIN_EMAIL / ADMIN_PASSWORD en production). IDEMPOTENT : un redéploiement ne
    // réinitialise jamais le mot de passe. Sans ces variables, aucun compte n'est créé.
    var bootstrapAdminEmail = app.Configuration["Identity:Bootstrap:AdminEmail"];
    var bootstrapAdminPassword = app.Configuration["Identity:Bootstrap:AdminPassword"];
    if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail) && !string.IsNullOrWhiteSpace(bootstrapAdminPassword))
    {
        var passwordHasher = scope.ServiceProvider
            .GetRequiredService<Marketplace.Modules.Identity.Application.Abstractions.IPasswordHasher>();

        var outcome = await IdentityDataSeeder.SeedAdminUserAsync(
            identityDb,
            passwordHasher,
            bootstrapAdminEmail,
            bootstrapAdminPassword,
            app.Configuration["Identity:Bootstrap:AdminFirstName"] is { Length: > 0 } fn ? fn : "Admin",
            app.Configuration["Identity:Bootstrap:AdminLastName"] is { Length: > 0 } ln ? ln : "Plateforme",
            app.Configuration["Identity:Bootstrap:AdminPhone"] is { Length: > 0 } ph ? ph : "+22900000000");

        switch (outcome)
        {
            case IdentityDataSeeder.AdminSeedOutcome.Created:
                app.Logger.LogInformation(
                    "Amorçage administrateur : compte « {Email} » CRÉÉ (actif, rôle Admin).",
                    bootstrapAdminEmail);
                break;

            case IdentityDataSeeder.AdminSeedOutcome.AlreadyPresent:
                app.Logger.LogInformation(
                    "Amorçage administrateur : « {Email} » existe déjà — inchangé (le mot de passe n'est jamais réinitialisé).",
                    bootstrapAdminEmail);
                break;

            default:
                // On ne lève PAS : refuser de démarrer priverait aussi les comptes
                // déjà existants d'un service par ailleurs sain. Mais le message doit
                // être impossible à manquer — c'est le seul compte par lequel une
                // plateforme neuve peut être ouverte.
                app.Logger.LogError(
                    "AMORÇAGE ADMINISTRATEUR ÉCHOUÉ ({Raison}) : aucun compte « {Email} » n'a été créé. "
                    + "Sur une base neuve, PERSONNE ne pourra se connecter à la console d'administration.",
                    outcome, bootstrapAdminEmail);
                break;
        }

        // On relit la base plutôt que de se fier à l'issue de l'amorçage : `AlreadyPresent`
        // ne dit rien de l'ÉTAT du compte. Un admin suspendu ou sans rôle franchit
        // l'amorçage sans erreur et ne peut pourtant pas se connecter.
        var signInBlocker = await IdentityDataSeeder.VerifyAdminCanSignInAsync(identityDb, bootstrapAdminEmail);
        if (signInBlocker is null)
        {
            app.Logger.LogInformation(
                "Première connexion possible : « {Email} » est actif et porte le rôle Admin.",
                bootstrapAdminEmail);
        }
        else
        {
            app.Logger.LogError(
                "CONNEXION ADMINISTRATEUR IMPOSSIBLE pour « {Email} » : {Raison}.",
                bootstrapAdminEmail, signInBlocker);
        }
    }
    else
    {
        // Ni e-mail ni mot de passe : aucun compte n'est semé. C'est légitime sur une
        // base déjà peuplée, et bloquant sur une base neuve — d'où l'avertissement.
        app.Logger.LogWarning(
            "Aucun administrateur d'amorçage configuré (Identity:Bootstrap:AdminEmail / AdminPassword). "
            + "Sur une base neuve, la console d'administration restera inaccessible.");
    }

    var sellersDb = scope.ServiceProvider.GetRequiredService<SellersDbContext>();
    await sellersDb.Database.MigrateAsync();

    var offersDb = scope.ServiceProvider.GetRequiredService<OffersDbContext>();
    await offersDb.Database.MigrateAsync();

    var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await inventoryDb.Database.MigrateAsync();

    var pricingDb = scope.ServiceProvider.GetRequiredService<PricingDbContext>();
    await pricingDb.Database.MigrateAsync();

    var cartDb = scope.ServiceProvider.GetRequiredService<CartDbContext>();
    await cartDb.Database.MigrateAsync();

    var orderingDb = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    await orderingDb.Database.MigrateAsync();

    var paymentsDb = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await paymentsDb.Database.MigrateAsync();

    var shippingDb = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
    await shippingDb.Database.MigrateAsync();


    var reviewsDb = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
    await reviewsDb.Database.MigrateAsync();

    var searchDb = scope.ServiceProvider.GetRequiredService<SearchDbContext>();
    await searchDb.Database.MigrateAsync();

    var notificationsDb = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    await notificationsDb.Database.MigrateAsync();

    var billingDb = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    await billingDb.Database.MigrateAsync();

    var settlementDb = scope.ServiceProvider.GetRequiredService<SettlementDbContext>();
    await settlementDb.Database.MigrateAsync();

    var returnsDb = scope.ServiceProvider.GetRequiredService<ReturnsDbContext>();
    await returnsDb.Database.MigrateAsync();

    var disputesDb = scope.ServiceProvider.GetRequiredService<DisputesDbContext>();
    await disputesDb.Database.MigrateAsync();

    var messagingDb = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
    await messagingDb.Database.MigrateAsync();

    var wishlistDb = scope.ServiceProvider.GetRequiredService<WishlistDbContext>();
    await wishlistDb.Database.MigrateAsync();

    var loyaltyDb = scope.ServiceProvider.GetRequiredService<LoyaltyDbContext>();
    await loyaltyDb.Database.MigrateAsync();

    var taxDb = scope.ServiceProvider.GetRequiredService<TaxDbContext>();
    await taxDb.Database.MigrateAsync();

    var marketingDb = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();
    await marketingDb.Database.MigrateAsync();

    var recommendationsDb = scope.ServiceProvider.GetRequiredService<RecommendationsDbContext>();
    await recommendationsDb.Database.MigrateAsync();

    var analyticsDb = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
    await analyticsDb.Database.MigrateAsync();

    var fraudDb = scope.ServiceProvider.GetRequiredService<FraudDbContext>();
    await fraudDb.Database.MigrateAsync();

    var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    await platformDb.Database.MigrateAsync();
}

// Swagger réservé au développement (pas d'exposition d'API en production).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Sans gestionnaire d'exceptions, une erreur non gérée remonte à Kestrel, qui rend
// un 500 nu SANS exécuter les rappels OnStarting — donc sans les en-têtes CORS, et
// sans corps. Le client ne reçoit ni message, ni cause : il voit « CORS » ou rien.
// Ici, l'exception devient un ProblemDetails lisible, et la réponse conserve ses
// en-têtes.
app.UseExceptionHandler();

// CORS par surface, et non une politique globale : les trois consoles partagent
// désormais une seule origine serveur. Une liste fusionnée autoriserait la boutique à
// appeler /api/admin depuis le navigateur. Le branchement se fait sur le chemin — les
// preflight OPTIONS entrent dans la même branche que la requête réelle.
app.UseWhen(c => c.Request.Path.StartsWithSegments("/api/admin"), b => b.UseCors(CorsPolicies.Admin));
app.UseWhen(c => c.Request.Path.StartsWithSegments("/api/sellers"), b => b.UseCors(CorsPolicies.Seller));
app.UseWhen(c => c.Request.Path.StartsWithSegments("/api/client"), b => b.UseCors(CorsPolicies.Client));

app.UseAuthentication();
app.UseAuthorization();

// APRÈS UseAuthentication/UseAuthorization : le limiteur partitionne par identifiant
// de compte quand l'utilisateur est authentifié (mitigation du CGNAT), et cette
// identité n'existe qu'après. Placé avant, la clé retombe toujours sur l'IP.
app.UseRateLimiter();

app.MapObservability();

// Surface /api/<module> : API brute des modules. Elle porte les routes qu'aucune
// console n'appelle mais dont un tiers a besoin — webhooks PSP, retours de paiement.
app.MapIdentityEndpoints();
app.MapCatalogEndpoints();
app.MapSellerEndpoints();
app.MapOfferEndpoints();
app.MapInventoryEndpoints();
app.MapPricingEndpoints();
app.MapCartEndpoints();
app.MapOrderingEndpoints();
app.MapPaymentsEndpoints();
app.MapShippingEndpoints();
app.MapReviewsEndpoints();
app.MapSearchEndpoints();
app.MapNotificationsEndpoints();
app.MapBillingEndpoints();
app.MapSettlementEndpoints();
app.MapReturnsEndpoints();
app.MapDisputesEndpoints();
app.MapMessagingEndpoints();
app.MapWishlistEndpoints();
app.MapLoyaltyEndpoints();
app.MapTaxEndpoints();
app.MapMarketingEndpoints();
app.MapRecommendationsEndpoints();
app.MapAnalyticsEndpoints();
app.MapFraudEndpoints();

// Surface /api/client (boutique web + application acheteur).
app.MapClientGeographyEndpoints();
app.MapClientAuthEndpoints();
app.MapClientDiscoveryEndpoints();
app.MapClientCatalogEndpoints();
app.MapClientCartEndpoints();
app.MapClientCheckoutEndpoints();
app.MapClientOrderEndpoints();
app.MapClientAccountEndpoints();
app.MapClientWalletEndpoints();
app.MapClientMessagingEndpoints();
app.MapClientContentEndpoints();
app.MapClientEventEndpoints();
app.MapClientEngagementEndpoints();
app.MapClientAppEndpoints();
app.MapClientStorefrontEndpoints();
app.MapClientDeepLinkEndpoints();

// Surface /api/sellers (console vendeur + application vendeur).
app.MapSellerGeographyEndpoints();
app.MapSellerAuthEndpoints();
app.MapSellerRegistrationEndpoints();
app.MapSellerDashboardEndpoints();
app.MapSellerAccountEndpoints();
app.MapSellerShopEndpoints();
app.MapSellerCatalogEndpoints();
app.MapSellerOfferEndpoints();
app.MapSellerOrderEndpoints();
app.MapSellerFulfillmentEndpoints();
app.MapSellerReturnEndpoints();
app.MapSellerInventoryEndpoints();
app.MapSellerFinanceEndpoints();
app.MapSellerWalletEndpoints();
app.MapSellerReviewEndpoints();
app.MapSellerDisputeEndpoints();
app.MapSellerMessagingEndpoints();
app.MapSellerNotificationEndpoints();
app.MapSellerAppEndpoints();
app.MapSellerLegalEndpoints();

// Surface /api/admin (console plateforme).
app.MapAdminGeographyEndpoints();
app.MapAdminAuthEndpoints();
app.MapAdminAccountEndpoints();
app.MapAdminSellerEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminModerationEndpoints();
app.MapAdminOrderEndpoints();
app.MapAdminFinanceEndpoints();
app.MapAdminWalletEndpoints();
app.MapAdminCatalogEndpoints();
app.MapAdminProductEndpoints();
app.MapAdminPlatformEndpoints();
app.MapAdminMediaEndpoints();
app.MapAdminLegalEndpoints();
app.MapAdminShippingEndpoints();
app.MapAdminMonitoringEndpoints();
app.MapAdminRefundEndpoints();
app.MapAdminOutboxEndpoints();
app.MapAdminSearchEndpoints();

// Hubs temps réel (messagerie), un par surface navigateur.
//
// EXEMPTÉS du limiteur de débit : une connexion WebSocket est longue — elle négocie, se
// reconnecte et pousse des messages. Le limiteur la compterait comme une avalanche de
// requêtes et couperait la messagerie. Le hub exige déjà une authentification.
app.MapHub<ChatHub>("/api/client/hubs/chat").RequireCors(CorsPolicies.Client).DisableRateLimiting();
app.MapHub<ChatHub>("/api/sellers/hubs/chat").RequireCors(CorsPolicies.Seller).DisableRateLimiting();

await app.RunAsync();

// Exposé pour les tests d'intégration (WebApplicationFactory<Program>).
public partial class Program
{
}
