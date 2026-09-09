using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Marketplace.Api.Extensions;
using Marketplace.Api.Modules;
using Marketplace.Api.Observability;
using Marketplace.Bff.Driver.Endpoints;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability("bff-driver");

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

// 3. Pipeline transverse + infrastructure partagée.
builder.Services.AddBuildingBlocksPipeline();
builder.Services.AddBuildingBlocksInfrastructure();

var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? "marketplace:";
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// AUCUN SignalR ICI, CONTRAIREMENT AUX BFF MOBILE ET VENDEUR.
//
// Le seul temps réel dont un coursier a besoin, ce sont les offres de course — et
// elles vivent dans le service de dispatch, avec son propre hub et son propre backplane.
// L'application s'y connecte directement. Poser un second hub ici n'aurait servi qu'à
// relayer, en ajoutant un point de rupture sur le chemin le plus sensible du produit :
// vingt-cinq secondes pour répondre à une offre.
// ─────────────────────────────────────────────────────────────────────────────

// 3ter. CORS — la console web du coursier est une application navigateur.
const string DriverCorsPolicy = "driver-console";
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? builder.Configuration["Cors:Origins"]?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(DriverCorsPolicy, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            // REPLI FERMÉ. Un repli ouvert combiné à AllowCredentials laisserait
            // n'importe quel site lire le portefeuille d'un coursier connecté.
            policy.SetIsOriginAllowed(_ => false);
        }
    });
});

// 4. Auth JWT — MÊME clé, MÊME émetteur, MÊME audience que les autres hôtes.
//
// C'est ce qui permet à l'application coursier de se connecter ici et d'appeler ensuite
// le service de dispatch avec le même jeton. Le dispatch le VÉRIFIE sans en émettre.
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"]
    ?? throw new InvalidOperationException("Clé de signature JWT absente (Jwt:SigningKey).");

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
    // FILET DE SÉCURITÉ. Sans lui, une route qui oublie RequireAuthorization est anonyme
    // en silence — ici, cela ouvrirait le portefeuille des coursiers. Les routes
    // réellement publiques déclarent AllowAnonymous, qui prime.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddAuthRateLimiter();
builder.Services.AddProxyForwardedHeaders();
builder.Services.AddConcurrencyExceptionHandler();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", ".").Replace("`", "_"));

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Coller le token JWT (obtenu via /driver/auth/login), sans le préfixe « Bearer »."
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

var dbContextTypes = builder.Services
    .Where(d => d.ServiceType is { IsClass: true } t && t != typeof(DbContext) && typeof(DbContext).IsAssignableFrom(t))
    .Select(d => d.ServiceType)
    .Distinct()
    .ToList();

var app = builder.Build();

// EN TOUT PREMIER : restaure la vraie IP du client (Traefik la transmet en
// X-Forwarded-For). Tout ce qui suit — journalisation, authentification, limitation de
// débit — lit RemoteIpAddress.
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// EN TÊTE DE PIPELINE. Sans lui, une exception non gérée remonte à Kestrel, qui rend un
// 500 nu SANS exécuter les rappels OnStarting — donc sans en-tête CORS. La console web
// ne verrait pas le 500, elle verrait « blocked by CORS policy », et on chercherait un
// problème de CORS qui n'existe pas.
app.UseExceptionHandler();

app.UseCors(DriverCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

// APRÈS UseAuthentication : le limiteur partitionne par identifiant de compte quand
// l'utilisateur est authentifié. Placé avant, l'identité n'existe pas encore et la clé
// retombe toujours sur l'IP.
app.UseRateLimiter();

app.MapObservability();

// Surface /driver.
app.MapDriverAuthEndpoints();
app.MapDriverAccountEndpoints();
app.MapDriverWalletEndpoints();

await app.RunAsync();
