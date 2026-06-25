using System.Text;
using Marketplace.Api.Endpoints;
using Marketplace.Api.Modules;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. Modules : chaque installer enregistre son DbContext, ses handlers, son outbox.
foreach (var module in ModuleRegistry.Installers)
{
    module.Install(builder.Services, builder.Configuration);
}

// 2. MediatR : scan des assemblies Application de tous les modules.
builder.Services.AddMediatR(cfg =>
{
    foreach (var assembly in ModuleRegistry.Installers
                 .Select(installer => installer.ApplicationAssembly)
                 .Distinct())
    {
        cfg.RegisterServicesFromAssembly(assembly);
    }
});

// 3. Pipeline transverse + infrastructure partagée.
builder.Services.AddBuildingBlocksPipeline();
builder.Services.AddBuildingBlocksInfrastructure();

// 3bis. Cache distribué : Redis si configuré, sinon le cache mémoire par défaut
// (déjà enregistré par AddBuildingBlocksInfrastructure). Le panier et les read
// models chauds s'appuient dessus via ICacheService.
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? "marketplace:";
    });
}

// 4. Authentification JWT (jetons émis par le module Identity).
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"]
    ?? throw new InvalidOperationException("Clé de signature JWT absente (Jwt:SigningKey).");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? "marketplace",
            ValidAudience = jwtSection["Audience"] ?? "marketplace",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// 5. API.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Migrations + seed au démarrage : pratique en dev, désactivé par défaut.
if (app.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();

    var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await catalogDb.Database.MigrateAsync();

    var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await identityDb.Database.MigrateAsync();
    await IdentityDataSeeder.SeedDefaultRolesAsync(identityDb);

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
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("System");

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

await app.RunAsync();

// Exposé pour les tests d'intégration (WebApplicationFactory<Program>).
public partial class Program
{
}
