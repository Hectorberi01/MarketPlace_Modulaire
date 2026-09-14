using Microsoft.Extensions.DependencyInjection.Extensions;
using Marketplace.BuildingBlocks.Application.Options;
using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Cart.Application.Abstractions;
using Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;
using Marketplace.Modules.Cart.Application.Carts.EventHandlers;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Cart.Domain.Carts;
using Marketplace.Modules.Cart.Domain.Carts.Events;
using Marketplace.Modules.Cart.Infrastructure.Persistence;
using Marketplace.Modules.Cart.Infrastructure.Public;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Cart.Infrastructure;

/// <summary>Enregistre le module Cart : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class CartModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Cart";

    public Assembly ApplicationAssembly => typeof(AddItemToCartCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // ─────────────────────────────────────────────────────────────────────────
        // LE TAUX DU FRAIS DE SERVICE, LU PAR LES DEUX MODULES QUI EN ONT BESOIN.
        //
        // Cart l'ANNONCE dans le panier, Ordering le FIGE sur la commande. Les deux
        // installeurs enregistrent donc le même singleton depuis la même section :
        // `TryAddSingleton` fait que le premier chargé gagne et que le second ne le
        // remplace pas — deux instances porteraient de toute façon la même valeur,
        // mais l'écrasement silencieux est le genre de détail dont on ne veut pas
        // dépendre de l'ordre de chargement des modules.
        // ─────────────────────────────────────────────────────────────────────────
        services.TryAddSingleton(PlatformFeeOptions.Depuis(configuration.GetSection("Pricing")["PlatformFeeRate"]));

        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<CartDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CartDbContext.SchemaName)));

        services.AddScoped<ICartUnitOfWork>(sp => sp.GetRequiredService<CartDbContext>());

        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<ICartModuleApi, CartModuleApi>();

        services.AddScoped<IDomainEventHandler<CartCheckedOutDomainEvent>, CartCheckedOutDomainEventHandler>();

        // Chorégraphie : clôture du panier quand une commande est placée.
        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, CloseCartOnOrderPlacedHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<CartDbContext>();
    }
}
