using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Marketplace.Api.Extensions;
using Marketplace.Api.Modules;
using Marketplace.Api.Observability;
using Marketplace.Bff.Web.Endpoints;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Observabilité (logs structurés + traces/métriques OTel + health checks).
builder.AddObservability("bff-web");

// 1. Modules composés in-process (même registre que Marketplace.Api).
foreach (var module in ModuleRegistry.Installers)
{
    module.Install(builder.Services, builder.Configuration);
}

// 2. MediatR sur toutes les assemblies Application des modules.
builder.Services.AddMediatR(cfg =>
{
    foreach (var assembly in ModuleRegistry.Installers.Select(i => i.ApplicationAssembly).Distinct())
    {
        cfg.RegisterServicesFromAssembly(assembly);
    }
});

// 3. Pipeline transverse + infrastructure partagée (+ cache mémoire par défaut).
builder.Services.AddBuildingBlocksPipeline();
builder.Services.AddBuildingBlocksInfrastructure();

// 3bis. Redis si configuré (sinon cache mémoire).
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? "marketplace:";
    });
}

// 3ter. CORS — le BFF Web est appelé depuis un navigateur (SPA/SSR). On autorise
//       les origines déclarées (Cors:Origins) ; à défaut, tout en développement.
const string WebCorsPolicy = "web-spa";
// Tableau indexé (Cors__Origins__0…) OU liste séparée par des virgules
// (Cors__Origins="https://a, http://b") : la seconde forme se pilote depuis
// une variable unique, sans avoir à connaître le nombre d'origines d'avance.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? builder.Configuration["Cors:Origins"]?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(WebCorsPolicy, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

// 4. Auth JWT — le BFF valide le jeton et propage l'identité ; l'autorisation fine
//    vit dans les modules.
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

builder.Services.AddAuthorization(options =>
{
    // FILET DE SÉCURITÉ — inversion de la logique par défaut.
    //
    // Sans lui, un futur MapGroup/MapGet qui OUBLIE son RequireAuthorization est ANONYME
    // en silence. Avec lui, l'oubli produit un 401 qu'on remarque tout de suite. Les
    // routes réellement publiques (auth, webhooks, santé) déclarent AllowAnonymous, qui
    // prime. L'API l'avait déjà ; les BFF, non — c'est là que la surface bouge le plus.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
// Rate limiting sur les endpoints d'auth (brute-force / énumération).
builder.Services.AddAuthRateLimiter();

// Sans ceci, le limiteur ci-dessus partitionne sur l'IP de TRAEFIK — donc une seule
// partition pour toute la planète. Voir ForwardedHeadersSetup.
builder.Services.AddProxyForwardedHeaders();
// Réponses d'erreur normalisées (ProblemDetails) — voir UseExceptionHandler plus bas.
// Un conflit de verrou optimiste devient un 409 « réessayez », et non un 500 « nous
// sommes cassés ». Enregistré AVANT AddProblemDetails : le premier handler qui traite
// l'exception gagne.
builder.Services.AddConcurrencyExceptionHandler();

builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // schemaIds qualifiés par le nom complet : évite toute collision si deux
    // groupes exposent des DTO imbriqués de même nom court.
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", ".").Replace("`", "_"));

    // Authentification Bearer (JWT) dans la doc : ajoute le bouton « Authorize ».
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Coller le token JWT (obtenu via /web/auth/login), sans le préfixe « Bearer »."
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

// Types de DbContext (un par module) pour la migration optionnelle au démarrage.
var dbContextTypes = builder.Services
    .Where(d => d.ServiceType is { IsClass: true } t && t != typeof(DbContext) && typeof(DbContext).IsAssignableFrom(t))
    .Select(d => d.ServiceType)
    .Distinct()
    .ToList();

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


if (app.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();
    foreach (var contextType in dbContextTypes)
    {
        var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await dbContext.Database.MigrateAsync();
    }
}

// Swagger réservé au développement (pas d'exposition d'API en production).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ─────────────────────────────────────────────────────────────────────────────
// GESTIONNAIRE D'EXCEPTIONS — EN PREMIER, ET CE N'EST PAS UN DÉTAIL D'ORDRE.
//
// Sans lui, une exception non gérée remontait jusqu'à Kestrel, qui produisait un
// 500 nu — et, ce faisant, N'EXÉCUTAIT PAS les rappels `OnStarting`. Or c'est
// exactement par là que le middleware CORS pose son en-tête
// `Access-Control-Allow-Origin` sur les requêtes non-preflight.
//
// Conséquence : la console d'administration ne voyait pas le 500. Elle voyait une
// réponse sans en-tête CORS, et le navigateur lui répondait « blocked by CORS
// policy ». On passait des heures à chercher un problème de configuration CORS
// qui n'existait pas, pendant que la vraie erreur — celle du serveur — n'était
// jamais affichée nulle part.
//
// Placé ici, en tête de pipeline, il intercepte l'exception, écrit une réponse
// ProblemDetails propre, et la réponse REDÉMARRE : les rappels OnStarting jouent,
// l'en-tête CORS revient, et le client reçoit enfin un message lisible.
// ─────────────────────────────────────────────────────────────────────────────
app.UseExceptionHandler();

app.UseCors(WebCorsPolicy);
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

// Surface /web (référentiel BFF Web enrichi).
app.MapWebAuthEndpoints();
app.MapWebDiscoveryEndpoints();
app.MapWebCatalogEndpoints();
app.MapWebCartEndpoints();
app.MapWebCheckoutEndpoints();
app.MapWebOrderEndpoints();
app.MapWebAccountEndpoints();
app.MapWebMessagingEndpoints();
app.MapWebStorefrontEndpoints();

await app.RunAsync();
