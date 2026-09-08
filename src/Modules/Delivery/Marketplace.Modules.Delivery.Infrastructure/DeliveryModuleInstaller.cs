using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Delivery.Application.Abstractions;
using Marketplace.Modules.Delivery.Application.Couriers;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Domain.Couriers;
using Marketplace.Modules.Delivery.Contracts.IntegrationEvents;
using Marketplace.Modules.Delivery.Domain.Couriers.Events;
using Marketplace.Modules.Delivery.Infrastructure.Dispatch;
using Marketplace.Modules.Delivery.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Delivery.Infrastructure;

/// <summary>
/// Enregistre le module Delivery : DbContext, repository, API publique, handlers, outbox.
///
/// Ce module est VOLONTAIREMENT MINCE. Il porte l'identité du coursier côté marketplace
/// — de quoi le payer et de quoi refuser de le payer. Le dispatch, la géolocalisation et
/// l'application coursier vivent dans le service autonome décrit par
/// docs/CDC_Service_Livraison_Coursier.md, et n'ont rien à faire ici.
/// </summary>
public sealed class DeliveryModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Delivery";

    public Assembly ApplicationAssembly => typeof(RegisterCourierCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<DeliveryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveryDbContext.SchemaName)));

        services.AddScoped<IDeliveryUnitOfWork>(sp => sp.GetRequiredService<DeliveryDbContext>());

        services.AddScoped<ICourierRepository, CourierRepository>();
        services.AddScoped<IDeliveryModuleApi, Public.DeliveryModuleApi>();

        services.AddScoped<IDomainEventHandler<CourierActivatedDomainEvent>, CourierActivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CourierSuspendedDomainEvent>, CourierSuspendedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        InstallDispatchChannel(services, configuration);

        services.AddOutboxProcessor<DeliveryDbContext>();
    }

    /// <summary>
    /// Le canal sortant vers le SERVICE DE DISPATCH.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// C'EST CE MODULE QUI PORTE CE CANAL, ET NON SHIPPING NI ORDERING.
    ///
    /// Les événements consommés ici viennent d'ailleurs — « commande prête » de Shipping,
    /// « commande annulée » d'Ordering. On aurait donc pu poser ces gestionnaires chez
    /// eux. Ce serait une erreur d'attribution : Shipping ne sait pas qu'un service
    /// coursier existe, et n'a aucune raison de l'apprendre. Delivery, si — c'est le
    /// pendant marketplace de ce service, il porte déjà l'identité de paiement du
    /// coursier et publie son droit de travailler.
    ///
    /// Un seul module connaît donc l'adresse du dispatch, son secret et sa forme d'appel.
    /// Le jour où ce canal deviendra un producteur Kafka, il n'y aura qu'un endroit à
    /// changer.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static void InstallDispatchChannel(IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration[DispatchServiceClient.BaseUrlKey];

        services.AddHttpClient<IDispatchServiceClient, DispatchServiceClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            }

            // COURT, DÉLIBÉRÉMENT. Cet appel est fait par le drain d'outbox, qui traite
            // les messages en série : une attente longue sur un service injoignable
            // bloquerait la file entière derrière lui. Mieux vaut échouer vite et laisser
            // le réessai faire son travail.
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // ─────────────────────────────────────────────────────────────────────────
        // SANS CES QUATRE LIGNES, LES ÉVÉNEMENTS SE DISPATCHENT VERS PERSONNE.
        //
        // Le répartiteur d'événements d'intégration résout les gestionnaires du
        // conteneur, en parcourt la liste, et ne dit rien si elle est vide. Un événement
        // sans gestionnaire est donc marqué traité sans avoir rien produit — c'est
        // exactement ce qui arrivait à « commande prête à livrer ».
        // ─────────────────────────────────────────────────────────────────────────
        services.AddScoped<IIntegrationEventHandler<OrderReadyForDeliveryIntegrationEvent>,
            SendOrderReadyToDispatchHandler>();
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
            SendOrderCancelledToDispatchHandler>();
        services.AddScoped<IIntegrationEventHandler<CourierActivatedIntegrationEvent>,
            SendCourierActivatedToDispatchHandler>();
        services.AddScoped<IIntegrationEventHandler<CourierSuspendedIntegrationEvent>,
            SendCourierSuspendedToDispatchHandler>();
    }
}
