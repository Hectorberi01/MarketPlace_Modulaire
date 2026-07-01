using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Wishlist.Application.Abstractions;
using Marketplace.Modules.Wishlist.Application.Wishlists;
using Marketplace.Modules.Wishlist.Domain.Wishlists;
using Marketplace.Modules.Wishlist.Infrastructure.Persistence;

namespace Marketplace.Modules.Wishlist.Infrastructure;

/// <summary>Enregistre le module Wishlist : DbContext, repository, validators, outbox.</summary>
public sealed class WishlistModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Wishlist";

    public Assembly ApplicationAssembly => typeof(AddToWishlistCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<WishlistDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", WishlistDbContext.SchemaName)));

        services.AddScoped<IWishlistUnitOfWork>(sp => sp.GetRequiredService<WishlistDbContext>());

        services.AddScoped<IWishlistRepository, WishlistRepository>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<WishlistDbContext>();
    }
}
