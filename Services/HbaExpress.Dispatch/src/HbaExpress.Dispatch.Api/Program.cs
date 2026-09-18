using System.Text;
using HbaExpress.Dispatch.Api.BackgroundJobs;
using HbaExpress.Dispatch.Api.Endpoints;
using HbaExpress.Dispatch.Api.Extensions;
using HbaExpress.Dispatch.Api.Notifications;
using HbaExpress.Dispatch.Api.Observability;
using HbaExpress.Dispatch.Api.Outbound;
using HbaExpress.Dispatch.Api.Realtime;
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
// 0. OBSERVABILITÉ — journaux structurés, traces, métriques, sondes.
//
// EN PREMIER, et pas par élégance : ce service a deux pannes silencieuses par nature
// — une course que personne n'accepte, une course livrée dont le paiement ne part pas.
// Aucune des deux ne produit d'erreur HTTP. Sans mesure, elles ne se découvrent que par
// la réclamation d'un coursier ou d'un acheteur.
// ─────────────────────────────────────────────────────────────────────────────
builder.AddDispatchObservability("hbaexpress-dispatch");

// ─────────────────────────────────────────────────────────────────────────────
// 1. LE SERVICE LUI-MÊME.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddDispatchApplication();

// ─────────────────────────────────────────────────────────────────────────────
// TEMPS RÉEL — le hub qui porte les offres jusqu'au téléphone du coursier.
//
// BACKPLANE REDIS, ET IL EST DEVENU NÉCESSAIRE. Depuis que les balayages réservent
// leurs lignes par « FOR UPDATE SKIP LOCKED », ce service peut tourner en plusieurs
// instances. Sans backplane, un coursier connecté à l'instance A ne recevrait jamais
// une offre dispatchée par l'instance B : il la verrait apparaître au rattrapage, vingt
// secondes plus tard, sur une fenêtre qui en dure vingt-cinq. La panne est
// intermittente, invisible à une instance, et impossible à reproduire en développement.
//
// Sans Redis configuré, SignalR reste en mémoire — correct sur une seule instance.
// ─────────────────────────────────────────────────────────────────────────────
var signalr = builder.Services.AddSignalR();
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalr.AddStackExchangeRedis(redisConnection);
}

// ─────────────────────────────────────────────────────────────────────────────
// COMMENT UNE OFFRE ATTEINT UN COURSIER : DEUX CANAUX, UN SEUL PORT.
//
// Le hub couvre l'application ouverte, le push couvre le téléphone rangé. Une offre vit
// vingt-cinq secondes : avec le seul hub, le coursier qui avait rangé son téléphone ne
// voyait jamais passer les courses, et elles partaient à celui qui regardait son écran
// au bon moment.
//
// AVANT AddDispatchInfrastructure, et l'ordre est significatif : celle-ci enregistre
// LoggingOfferNotifier en TryAdd, donc seulement si le port est encore libre. Enregistré
// après, il serait ignoré — et le hub existerait sans que rien ne pousse dedans.
//
// Les deux implémentations sont enregistrées SOUS LEUR PROPRE TYPE : elles ne sont plus
// le port, elles sont ses deux moitiés. Seul le composite répond à IOfferNotifier.
builder.Services.AddScoped<SignalROfferNotifier>();
builder.Services.AddSingleton<PushOfferNotifier>();
builder.Services.AddScoped<IOfferNotifier, CompositeOfferNotifier>();

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

        // ─────────────────────────────────────────────────────────────────────
        // LE JETON DU HUB ARRIVE EN QUERY STRING, PAS EN EN-TÊTE.
        //
        // Le handshake WebSocket ne permet pas au client de poser un en-tête
        // Authorization : c'est une limite du protocole, pas un choix. SignalR passe
        // donc le jeton en « ?access_token= », et il faut le récupérer explicitement.
        //
        // RESTREINT AU CHEMIN DU HUB. Accepter un jeton en query string sur toutes les
        // routes le ferait apparaître dans les journaux d'accès de Traefik, dans
        // l'historique des navigateurs et dans les en-têtes Referer — un jeton
        // d'authentification n'a rien à faire dans une URL ailleurs qu'ici.
        // ─────────────────────────────────────────────────────────────────────
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
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

app.MapDispatchObservability();

app.MapInternalEndpoints();
app.MapCourierEndpoints();

// L'autorisation vient de l'attribut [Authorize] du hub ; le rattachement au groupe
// vient du jeton, jamais du client.
app.MapHub<CourierHub>("/hubs/courier");

await app.RunAsync();

// Exposé pour d'éventuels tests d'intégration (WebApplicationFactory<Program>).
public partial class Program
{
}
