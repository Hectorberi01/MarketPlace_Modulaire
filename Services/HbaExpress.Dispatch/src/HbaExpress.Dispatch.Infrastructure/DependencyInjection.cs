using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Infrastructure.Notifications;
using HbaExpress.Dispatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HbaExpress.Dispatch.Infrastructure;

/// <summary>Branche la persistance et les ports sortants du service de dispatch.</summary>
public static class DispatchInfrastructureServices
{
    /// <summary>Emplacement de la chaîne de connexion, dans cet ordre de priorité.</summary>
    public const string ConnectionStringName = "Dispatch";

    public static IServiceCollection AddDispatchInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);

        services.AddDbContext<DispatchDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DispatchDbContext.SchemaName)));

        // Le DbContext EST l'unité de travail. Un second enregistrement plutôt qu'une
        // seconde instance : deux DbContext dans la même requête, ce serait deux
        // ChangeTracker, donc un SaveChanges qui n'enregistre pas ce qu'on croit.
        services.AddScoped<IDispatchUnitOfWork>(sp => sp.GetRequiredService<DispatchDbContext>());

        services.AddScoped<ICourierRepository, CourierRepository>();
        services.AddScoped<IDeliveryJobRepository, DeliveryJobRepository>();

        // TryAdd : l'hôte qui branchera SignalR enregistrera le sien AVANT d'appeler
        // cette méthode, et gardera la main.
        services.TryAddScoped<IOfferNotifier, LoggingOfferNotifier>();

        return services;
    }

    /// <summary>
    /// Chaîne de connexion, avec un message d'erreur qui dit où la mettre.
    ///
    /// REFUSER DE DÉMARRER PLUTÔT QUE DE RETOMBER SUR « localhost ». Un repli silencieux
    /// vers une base locale donnerait un service qui démarre, répond aux sondes de santé,
    /// et perd toutes les courses dans une base vide que personne ne regarde.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration["DISPATCH_DB"];
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "CHAÎNE DE CONNEXION DU SERVICE DE DISPATCH ABSENTE.\n\n" +
                "Renseignez ConnectionStrings:Dispatch (appsettings / variable " +
                "ConnectionStrings__Dispatch), ou la variable DISPATCH_DB.\n\n" +
                "Exemple : Host=postgres;Port=5432;Database=dispatch;Username=dispatch;Password=…");
        }

        return connectionString;
    }
}
