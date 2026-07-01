using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Marketplace.Api.Modules;
using Marketplace.Api.RealTime;
using Marketplace.Bff.Seller.Endpoints;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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

// 3ter. SignalR (messagerie temps réel). Backplane Redis partagé avec le BFF
// Mobile pour que les pushs traversent les processus (acheteur ↔ vendeur).
var signalr = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalr.AddStackExchangeRedis(redisConnection);
}

// 3quater. CORS — le back-office vendeur est une application navigateur.
const string SellerCorsPolicy = "seller-console";
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(SellerCorsPolicy, policy =>
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

// 4. Auth JWT — même clé que l'Api : le vendeur se connecte avec son compte
//    Identity ; la boutique est résolue à partir de l'utilisateur du jeton.
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

        // SignalR transmet le jeton via ?access_token= sur le handshake WebSocket.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/seller/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Plusieurs groupes exposent des DTO imbriqués de même nom court (ex.
    // StatusRequest pour produits et offres). On qualifie les schemaIds par le
    // nom complet pour éviter les collisions à la génération de swagger.json.
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("+", ".").Replace("`", "_"));

    // Authentification Bearer (JWT) dans la doc : ajoute le bouton « Authorize »
    // et applique le schéma à toutes les opérations protégées.
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Coller le token JWT (obtenu via /seller/auth/login), sans le préfixe « Bearer »."
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

if (app.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();
    foreach (var contextType in dbContextTypes)
    {
        var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
        await dbContext.Database.MigrateAsync();
    }
}

// Swagger activé sans condition (surface de test du BFF). À restreindre à
// l'environnement de dev avant un déploiement public.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(SellerCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", surface = "seller" })).WithTags("System");

// Surface /seller (back-office vendeur).
app.MapSellerAuthEndpoints();
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

// Hub temps réel (messagerie). Sous /seller pour être routé par Traefik + CORS navigateur.
app.MapHub<ChatHub>("/seller/hubs/chat").RequireCors(SellerCorsPolicy);

await app.RunAsync();
