using System.Text;
using HbaExpress.Dispatch.Api.BackgroundJobs;
using HbaExpress.Dispatch.Api.Endpoints;
using HbaExpress.Dispatch.Api.Extensions;
using HbaExpress.Dispatch.Api.Outbound;
using HbaExpress.Dispatch.Api.Security;
using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Application;
using HbaExpress.Dispatch.Infrastructure;
using HbaExpress.Dispatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────────────────────
// 1. LE SERVICE LUI-MÊME.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddDispatchApplication();
builder.Services.AddDispatchInfrastructure(builder.Configuration);

// Ce que vaut une course. Lu au démarrage, et le démarrage ÉCHOUE si la part n'est pas
// renseignée : un service qui ne sait pas ce qu'il doit payer ne doit pas tourner.
builder.Services.AddSingleton(CourierPayGuard.Resolve(builder.Configuration));

// Canal SORTANT vers la marketplace — c'est lui qui fait payer les courses livrées.
var marketplaceBaseUrl = builder.Configuration[MarketplaceClient.BaseUrlKey];
builder.Services.AddHttpClient<IMarketplaceClient, MarketplaceClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(marketplaceBaseUrl))
    {
        client.BaseAddress = new Uri(marketplaceBaseUrl, UriKind.Absolute);
    }

    // Court : le drain traite les messages en série, une longue attente sur un service
    // injoignable bloquerait la file derrière elle.
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ─────────────────────────────────────────────────────────────────────────────
// 2. AUTHENTIFICATION DE L'APPLICATION COURSIER.
//
// LA MÊME CLÉ QUE LA MARKETPLACE, DÉLIBÉRÉMENT. Le coursier se connecte au module
// Identity du monolithe — il n'y a pas de second compte, pas de second mot de passe, et
// surtout pas de second cycle de vie à tenir synchronisé. Ce service se contente de
// VÉRIFIER le jeton ; il n'en émet aucun.
//
// Conséquence à ne pas perdre de vue : un jeton d'ACHETEUR est cryptographiquement
// valide ici aussi. C'est pourquoi l'identifiant du jeton est confronté à la table des
// coursiers de ce service — un acheteur y est inconnu, et reçoit un 404.
// ─────────────────────────────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"]
    ?? throw new InvalidOperationException(
        "Clé de signature JWT absente (Jwt:SigningKey). Elle doit être IDENTIQUE à celle de la marketplace, "
        + "sinon aucun jeton de coursier ne sera accepté.");

JwtSigningKeyGuard.Ensure(signingKey, builder.Environment);

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
// AUTORISATION — POLITIQUE DE REPLI. MÊME RAISON QUE CÔTÉ MARKETPLACE.
//
// Sans elle, une route qui OUBLIE d'appeler RequireAuthorization est anonyme. Ici, cela
// voudrait dire : n'importe qui met un coursier en ligne, déplace sa position, ou prend
// sa course. L'oubli ne doit pas créer une faille — il doit créer un 401, qu'on remarque
// tout de suite.
//
// Les routes /internal restent explicitement anonymes : leur appelant est un service,
// authentifié par SIGNATURE. `.AllowAnonymous()` est une métadonnée explicite, et prime
// sur cette politique.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ─────────────────────────────────────────────────────────────────────────────
// 3. API.
// ─────────────────────────────────────────────────────────────────────────────
// AVANT AddProblemDetails : les IExceptionHandler sont essayés dans l'ordre
// d'enregistrement, et le premier qui rend « true » gagne. Enregistré après, le
// gestionnaire par défaut transformerait le conflit en 500 avant qu'on ait la main.
builder.Services.AddExceptionHandler<ConcurrencyExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Jeton JWT obtenu auprès de la marketplace (/api/identity/auth/login)."
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

// Sonde de santé : la base est la SEULE dépendance dure de ce service. Sans elle il ne
// peut ni créer, ni offrir, ni accepter une course — répondre « en bonne santé » serait
// mensonger.
builder.Services.AddHealthChecks()
    .AddNpgSql(
        DispatchInfrastructureServices.ResolveConnectionString(builder.Configuration),
        name: "dispatch-db")
    // Les abandons de la file sortante : des courses livrées que personne n'a payées.
    // Dégradé plutôt qu'en panne — voir OutboundHealthCheck.
    .AddCheck<OutboundHealthCheck>("dispatch-outbound");

// ─────────────────────────────────────────────────────────────────────────────
// 4. TÂCHES DE FOND.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddHostedService<RedispatchBackgroundService>();
builder.Services.AddHostedService<OutboundDrainService>();

var app = builder.Build();

// Hors développement, un appel entrant NON SIGNÉ est rejeté. Ces routes créent des
// courses et changent le droit de travailler d'un coursier : les ouvrir faute de secret
// configuré serait pire que de ne pas les exposer.
InboundSignature.AllowUnsignedWhenSecretMissing = app.Environment.IsDevelopment();

if (app.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Sans gestionnaire d'exceptions, une erreur non gérée remonte à Kestrel, qui rend un
// 500 nu, sans corps ni en-têtes. Ici elle devient un ProblemDetails lisible — et un
// conflit de concurrence devient un 409, intercepté juste avant.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapInternalEndpoints();
app.MapCourierEndpoints();

await app.RunAsync();

// Exposé pour d'éventuels tests d'intégration (WebApplicationFactory<Program>).
public partial class Program
{
}
