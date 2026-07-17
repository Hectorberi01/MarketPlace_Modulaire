using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Users;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Identity.Application.Users.EventHandlers;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.Addresses;
using Marketplace.Modules.Identity.Domain.PaymentMethods;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;
using Marketplace.Modules.Identity.Domain.Users.Events;
using Marketplace.Modules.Identity.Infrastructure.Persistence;
using Marketplace.Modules.Identity.Infrastructure.Public;
using Marketplace.Modules.Identity.Infrastructure.Security;

namespace Marketplace.Modules.Identity.Infrastructure;

/// <summary>
/// Enregistre tout le module Identity : DbContext (schéma propre), repositories,
/// API publique, services d'auth (BCrypt, JWT, TOTP), handlers d'events,
/// validators, processeur d'outbox.
/// </summary>
public sealed class IdentityModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Identity";

    public Assembly ApplicationAssembly => typeof(RegisterUserCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName)));

        services.AddScoped<IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IAddressRepository, AddressRepository>();
        services.AddScoped<IPaymentMethodRepository, PaymentMethodRepository>();
        services.AddScoped<IIdentityModuleApi, IdentityModuleApi>();
        services.AddScoped<AuthTokenIssuer>();

        // Options + services de sécurité.
        services.AddSingleton(BuildJwtOptions(configuration));
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ISecureTokenGenerator, Sha256TokenGenerator>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IAuthTokenSettings, AuthTokenSettings>();

        // Politique d'activation des comptes (section « Identity:Registration »).
        // Section absente = valeurs par défaut = les plus strictes.
        services.AddSingleton(BuildRegistrationOptions(configuration));
        services.AddSingleton<IRegistrationPolicy, RegistrationPolicy>();

        // Handlers de domain events.
        services.AddScoped<IDomainEventHandler<UserRegisteredDomainEvent>, UserRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<UserEmailConfirmedDomainEvent>, UserEmailConfirmedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<IdentityDbContext>();
    }

    private static JwtOptions BuildJwtOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        return new JwtOptions
        {
            Issuer = section["Issuer"] ?? "marketplace",
            Audience = section["Audience"] ?? "marketplace",
            SigningKey = section["SigningKey"]
                ?? throw new InvalidOperationException("Clé de signature JWT absente (Jwt:SigningKey)."),
            AccessTokenMinutes = ParseInt(section["AccessTokenMinutes"], 15),
            RefreshTokenDays = ParseInt(section["RefreshTokenDays"], 30),
            EmailVerificationHours = ParseInt(section["EmailVerificationHours"], 48)
        };
    }

    /// <summary>
    /// Lit « Identity:Registration » à la main, comme <see cref="BuildJwtOptions"/>.
    ///
    /// Pas de <c>Bind()</c> : ce projet ne référence que
    /// <c>Microsoft.Extensions.Configuration.Abstractions</c>, où l'extension
    /// n'existe pas — elle vit dans le paquet <c>…Configuration.Binder</c>. Ajouter
    /// une dépendance pour lire deux booléens serait disproportionné, et créerait
    /// deux façons de lire la configuration dans le même fichier.
    /// </summary>
    private static RegistrationOptions BuildRegistrationOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(RegistrationOptions.SectionName);
        return new RegistrationOptions
        {
            // Défauts STRICTS : une clé absente ou illisible ferme la porte.
            RequireApprovalForBuyers = ParseBool(section["RequireApprovalForBuyers"], fallback: true),
            RequireApprovalForAdminCreated = ParseBool(section["RequireApprovalForAdminCreated"], fallback: false)
        };
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;

    /// <summary>
    /// Tolère « 1 » et « 0 » en plus de « true »/« false ».
    ///
    /// Ces valeurs arrivent d'un fichier .env, pas d'un JSON typé : quelqu'un
    /// écrira <c>REQUIRE_APPROVAL_BUYERS=1</c> un jour, et un <c>bool.TryParse</c>
    /// seul répondrait « false » en silence — c'est-à-dire l'exact contraire de
    /// l'intention, sur un réglage de sécurité.
    /// </summary>
    private static bool ParseBool(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        if (bool.TryParse(normalized, out var parsed))
        {
            return parsed;
        }

        // Minuscules : « YES » et « ON » sont des réponses aussi valables que « yes ».
        return normalized.ToLowerInvariant() switch
        {
            "1" or "yes" or "on" => true,
            "0" or "no" or "off" => false,
            _ => fallback
        };
    }
}
