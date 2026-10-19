using System.Text;
using Marketplace.Host.Extensions;
using Marketplace.Host.Middleware;
using Marketplace.Host.Modules;
using Marketplace.Host.Observability;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;
using Marketplace.Host.Admin.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Marketplace.Host.Configuration;

/// <summary>
/// Enregistrement des services de l'hôte : observabilité, MediatR, modules, Redis,
/// SignalR, CORS, authentification JWT, autorisation, limitation de débit,
/// gestionnaires d'exceptions et Swagger. Program.cs ne garde que la composition
/// et le pipeline.
/// </summary>
public static class DependencyInjection
{
    public static WebApplicationBuilder AddMarketplaceApi(this WebApplicationBuilder builder)
    {
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

        // 3ter. SignalR (messagerie temps réel). Le backplane Redis reste utile malgré
        // l'hôte unique : il fait traverser les pushs entre les répliques du conteneur.
        var signalr = builder.Services.AddSignalR();
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            signalr.AddStackExchangeRedis(redisConnection);
        }

        // 3quater. CORS — trois politiques, une par surface navigateur. Les origines se
        // déclarent par surface (Cors:Client:Origins, Cors:Seller:Origins,
        // Cors:Admin:Origins). Aucune origine déclarée = aucune origine autorisée.
        builder.Services.AddCors(options =>
        {
            AjouterPolitiqueCors(options, CorsPolicies.Client, LireOrigines(builder.Configuration, "Cors:Client:Origins"));
            AjouterPolitiqueCors(options, CorsPolicies.Seller, LireOrigines(builder.Configuration, "Cors:Seller:Origins"));
            AjouterPolitiqueCors(options, CorsPolicies.Admin, LireOrigines(builder.Configuration, "Cors:Admin:Origins"));
        });

        // 4. Authentification JWT (jetons émis par le module Identity).
        var jwtSection = builder.Configuration.GetSection("Jwt");
        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException("Clé de signature JWT absente (Jwt:SigningKey).");

        // REFUSE DE DÉMARRER en production avec la clé de dev PUBLIQUE (celle qui est
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

                // SignalR transmet le jeton via ?access_token= sur le handshake WebSocket :
                // l'en-tête Authorization n'est pas disponible côté navigateur.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken)
                            && (path.StartsWithSegments("/api/client/hubs")
                                || path.StartsWithSegments("/api/sellers/hubs")))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        // Limitation de débit. AddProxyForwardedHeaders est indissociable : sans la vraie IP
        // du client, le limiteur partitionnerait sur celle de Traefik — une seule partition
        // pour tout le trafic.
        builder.Services.AddAuthRateLimiter();
        builder.Services.AddProxyForwardedHeaders();

        // Politique de repli d'autorisation. NE PAS RETIRER : sans elle, une route qui oublie
        // `RequireAuthorization()` est anonyme ; avec elle, l'oubli donne un 401.
        //
        // `.AllowAnonymous()` est explicite et prime : webhooks PSP, retour de paiement,
        // connexion, sondes de santé.
        //
        // Elle ne protège que de l'ANONYME. Les trois surfaces partagent la clé JWT, donc un
        // jeton acheteur est valide sur /api/admin : chaque groupe sensible exige EN PLUS un
        // rôle, voir ApiAuthorization.
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        // Client HTTP court (supervision) : ping des /health des surfaces + Prometheus.
        builder.Services.AddHttpClient("monitoring", c => c.Timeout = TimeSpan.FromSeconds(6));

        // Cookie de rafraîchissement de la console d'administration : le jeton ne transite
        // pas dans le corps JSON, où une XSS pourrait le lire depuis localStorage.
        // SameSite=Strict par défaut (console et API sur le même domaine racine) ;
        // Auth__RefreshCookie__SameSite=None si la console déménage.
        builder.Services.AddSingleton(new AdminAuthCookieOptions
        {
            SameSite = Enum.TryParse<SameSiteMode>(builder.Configuration["Auth:RefreshCookie:SameSite"], ignoreCase: true, out var sameSite)
                ? sameSite
                : SameSiteMode.Strict,
        });

        // 5. API.
        // Réponses d'erreur normalisées (ProblemDetails) — voir UseExceptionHandler plus bas.
        // Un conflit de verrou optimiste devient un 409 « réessayez », et non un 500 « nous
        // sommes cassés ». Enregistré AVANT AddProblemDetails : le premier handler qui traite
        // l'exception gagne.
        builder.Services.AddConcurrencyExceptionHandler();
        builder.Services.AddUniqueViolationExceptionHandler();

        builder.Services.AddProblemDetails();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            // schemaIds qualifiés par le nom complet. INDISPENSABLE depuis la fusion :
            // les trois surfaces exposent des DTO imbriqués de même nom court
            // (StatusRequest côté produits et côté offres, par exemple).
            c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", ".").Replace("`", "_"));

            // Authentification Bearer (JWT) dans la doc : ajoute le bouton « Authorize ».
            c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = "Coller le token JWT (obtenu via /api/client/auth/login, /api/sellers/auth/login ou /api/admin/auth/login), sans le préfixe « Bearer »."
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

        return builder;
    }

    /// <summary>
    /// Deux écritures acceptées : tableau indexé (Cors__Client__Origins__0) ou liste
    /// séparée par des virgules (Cors__Client__Origins="https://a,https://b"). La forme
    /// indexée oblige à connaître le nombre d'origines au moment d'écrire le compose ;
    /// la liste simple se pilote depuis le vault.
    /// </summary>
    private static string[] LireOrigines(IConfiguration configuration, string cle)
        => configuration.GetSection(cle).Get<string[]>()
           ?? configuration[cle]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           ?? Array.Empty<string>();

    private static void AjouterPolitiqueCors(CorsOptions options, string nom, string[] origines)
        => options.AddPolicy(nom, policy =>
        {
            if (origines.Length > 0)
            {
                policy.WithOrigins(origines).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
            else
            {
                policy.SetIsOriginAllowed(_ => false);
            }
        });
}
