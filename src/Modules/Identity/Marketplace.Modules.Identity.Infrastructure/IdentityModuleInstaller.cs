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

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var parsed) ? parsed : fallback;
}
