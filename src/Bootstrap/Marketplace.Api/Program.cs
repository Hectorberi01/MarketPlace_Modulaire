using System.Text;
using Marketplace.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Marketplace.Api.Endpoints;
using Marketplace.Api.Observability;
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

// 0. Observabilité (logs structurés + traces/métriques OTel + health checks).
//    Toutes les métriques sont enregistrées sur tous les hôtes — voir le commentaire
//    d'AddObservability : ce n'est pas de la générosité, c'est une nécessité.
builder.AddObservability("marketplace-api");

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

// ⚠️ REFUSE DE DÉMARRER en production avec la clé de dev PUBLIQUE (celle qui est
// committée dans le dépôt). Avec elle, n'importe qui forge un jeton « Admin ».
// Le compose impose déjà l'override ; ceci est la seconde barrière — pour tous les
// autres chemins de déploiement. Voir JwtKeyGuard.
JwtKeyGuard.EnsureProductionKey(signingKey, builder.Environment);

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

// ─────────────────────────────────────────────────────────────────────────────
// LIMITATION DE DÉBIT — ELLE ÉTAIT TOTALEMENT ABSENTE DE CET HÔTE.
//
// L'API expose /api/identity/auth/login en ANONYME, et n'appelait ni AddRateLimiter
// ni UseRateLimiter. Le brute-force n'y rencontrait donc aucune résistance
// applicative : seul le plafond d'edge de Traefik s'appliquait (100 req/s), soit
// 6 000 tentatives de mot de passe par minute.
//
// AddProxyForwardedHeaders est indissociable : sans lui, le limiteur partitionnerait
// sur l'IP de Traefik — une seule partition pour la planète entière.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddAuthRateLimiter();
builder.Services.AddProxyForwardedHeaders();

// ─────────────────────────────────────────────────────────────────────────────
// AUTORISATION — POLITIQUE DE REPLI (FallbackPolicy). NE PAS RETIRER.
//
// Avant : `AddAuthorization()` nu. Conséquence : toute route qui OUBLIAIT d'appeler
// `RequireAuthorization()` était ANONYME. Et beaucoup l'oubliaient. Un anonyme
// pouvait, depuis Internet :
//   • créer, modifier et SUPPRIMER n'importe quel produit du catalogue ;
//   • changer le PRIX de l'offre d'un vendeur, suspendre celle d'un concurrent ;
//   • fixer le STOCK à une valeur arbitraire et casser la saga de commande ;
//   • lister et créer des UTILISATEURS et des RÔLES (/api/identity/users) ;
//   • valider des dossiers KYB (/api/sellers/admin) ;
//   • toucher au RÈGLEMENT des vendeurs (/api/settlement).
//
// La FallbackPolicy inverse la logique par défaut : une route SANS métadonnée
// d'autorisation exige désormais un utilisateur authentifié. L'oubli ne crée plus
// une faille — il crée un 401, qu'on remarque tout de suite.
//
// Les routes réellement publiques restent publiques : `.AllowAnonymous()` est une
// métadonnée explicite, et elle prime sur la politique de repli. C'est le cas des
// webhooks PSP, du retour de paiement, de la connexion, et des sondes de santé.
//
// ⚠️ La politique de repli protège contre l'ANONYME. Elle ne protège PAS contre un
// acheteur authentifié qui appellerait une route d'administration : les 5 hôtes
// partagent la même clé JWT, donc un jeton acheteur est valide ici. C'est pourquoi
// chaque groupe sensible exige EN PLUS un rôle (voir ApiAuthorization).
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// 5. API.
// Réponses d'erreur normalisées (ProblemDetails) — voir UseExceptionHandler plus bas.
// Un conflit de verrou optimiste devient un 409 « réessayez », et non un 500 « nous
// sommes cassés ». Enregistré AVANT AddProblemDetails : le premier handler qui traite
// l'exception gagne.
builder.Services.AddConcurrencyExceptionHandler();

builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Authentification Bearer (JWT) dans la doc : ajoute le bouton « Authorize ».
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Coller le token JWT (obtenu via /api/identity/auth/login), sans le préfixe « Bearer »."
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ─────────────────────────────────────────────────────────────────────────────
// ⚠️ EN TOUT PREMIER DANS LE PIPELINE. NE PAS DÉPLACER.
//
// Restaure la vraie IP du client (Traefik la transmet en X-Forwarded-For). Tout ce qui
// suit — journalisation, authentification, limitation de débit — lit RemoteIpAddress.
// Placer ce middleware plus bas reviendrait à leur donner l'IP du proxy : la correction
// serait sans effet, tout en paraissant appliquée.
// ─────────────────────────────────────────────────────────────────────────────
app.UseProxyForwardedHeaders();


// Webhooks PSP : n'accepter un webhook non signé (secret manquant) qu'en
// développement. En production, un secret absent fait rejeter le webhook.
Marketplace.Modules.Payments.Infrastructure.Gateways.GatewayWebhook.AllowUnsignedWhenSecretMissing
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

    // ── AMORÇAGE DU PREMIER ADMINISTRATEUR ─────────────────────────────────
    // Sur une base neuve, aucun compte n'existe : créer un admin exige déjà
    // d'être admin (poule / œuf). Ce hook casse la boucle.
    //
    // Piloté par la configuration (Identity:Bootstrap:AdminEmail / AdminPassword,
    // fournis en prod via ADMIN_EMAIL / ADMIN_PASSWORD — voir deploy/ENV-PROD.md).
    // IDEMPOTENT : si le compte existe déjà, rien n'est fait — un redéploiement ne
    // réinitialise donc JAMAIS le mot de passe. Sans ces deux variables, aucun
    // compte n'est créé (comportement d'origine).
    //
    // Aucun autre compte de démonstration n'est semé (ni vendeur, ni « Démo »).
    var bootstrapAdminEmail = app.Configuration["Identity:Bootstrap:AdminEmail"];
    var bootstrapAdminPassword = app.Configuration["Identity:Bootstrap:AdminPassword"];
    if (!string.IsNullOrWhiteSpace(bootstrapAdminEmail) && !string.IsNullOrWhiteSpace(bootstrapAdminPassword))
    {
        var passwordHasher = scope.ServiceProvider
            .GetRequiredService<Marketplace.Modules.Identity.Application.Abstractions.IPasswordHasher>();

        await IdentityDataSeeder.SeedAdminUserAsync(
            identityDb,
            passwordHasher,
            bootstrapAdminEmail,
            bootstrapAdminPassword,
            app.Configuration["Identity:Bootstrap:AdminFirstName"] is { Length: > 0 } fn ? fn : "Admin",
            app.Configuration["Identity:Bootstrap:AdminLastName"] is { Length: > 0 } ln ? ln : "Plateforme",
            app.Configuration["Identity:Bootstrap:AdminPhone"] is { Length: > 0 } ph ? ph : "+22900000000");

        app.Logger.LogInformation(
            "Amorçage administrateur : compte « {Email} » vérifié/créé (idempotent).",
            bootstrapAdminEmail);
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


app.UseAuthentication();
app.UseAuthorization();

// ⚠️ APRÈS UseAuthentication/UseAuthorization — ET C'EST INDISPENSABLE.
//
// Le limiteur partitionne par IDENTIFIANT DE COMPTE quand l'utilisateur est
// authentifié (mitigation du CGNAT). Or cette identité n'existe qu'APRÈS
// UseAuthentication : placé avant, HttpContext.User est encore anonyme, la clé
// retombe toujours sur l'IP, et le partitionnement par compte ne s'applique
// jamais. C'était le cas — une régression silencieuse.
app.UseRateLimiter();

app.MapObservability();

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
