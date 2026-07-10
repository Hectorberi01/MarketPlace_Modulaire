using System.Text;
using System.Threading.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Marketplace.Api.Modules;
using Marketplace.Api.Observability;
using Marketplace.Bff.Admin.Endpoints;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 0. Observabilité (logs structurés + traces/métriques OTel + health checks).
builder.AddObservability("bff-admin");

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

// 3ter. CORS — la console d'administration est une application navigateur.
//       Par sécurité, on n'ouvre PAS à toutes les origines par défaut ici :
//       la console admin doit déclarer ses origines (Cors:Origins).
const string AdminCorsPolicy = "admin-console";
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(AdminCorsPolicy, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
        else
        {
            // Surface d'admin : à défaut d'origines déclarées, on reste fermé
            // (aucune origine cross-site autorisée).
            policy.SetIsOriginAllowed(_ => false);
        }
    });
});

// 4. Auth JWT — même clé que l'Api. Les routes /admin exigent en plus le rôle
//    Admin (ou Moderator pour la modération) via RequireRole.
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

// 4bis. Rate limiting — protège les endpoints d'authentification anonymes
//       (login / refresh / mot de passe oublié / reset) contre le brute-force
//       et l'énumération. Fenêtre fixe par adresse IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

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
        Description = "Coller le token JWT (rôle Admin requis), obtenu via /admin/auth/login, sans le préfixe « Bearer »."
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

// Swagger réservé au développement : en production, ne pas exposer la surface
// d'API ni l'UI « Authorize ».
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(AdminCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapObservability();

// Surface /admin (back-office plateforme).
app.MapAdminAuthEndpoints();
app.MapAdminSellerEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminModerationEndpoints();
app.MapAdminOrderEndpoints();
app.MapAdminFinanceEndpoints();
app.MapAdminWalletEndpoints();
app.MapAdminCatalogEndpoints();
app.MapAdminProductEndpoints();
app.MapAdminPlatformEndpoints();

await app.RunAsync();
