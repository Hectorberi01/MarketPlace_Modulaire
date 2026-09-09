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
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Returns.Application.Returns.EventHandlers;
using Marketplace.Modules.Returns.Domain.Returns;
using Marketplace.Modules.Returns.Domain.Returns.Events;
using Marketplace.Modules.Returns.Infrastructure.Persistence;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Returns.Infrastructure;

/// <summary>Enregistre le module Returns : DbContext, repository, handlers, validators, outbox.</summary>
public sealed class ReturnsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Returns";

    public Assembly ApplicationAssembly => typeof(RequestReturnCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<ReturnsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ReturnsDbContext.SchemaName)));

        services.AddScoped<IReturnsUnitOfWork>(sp => sp.GetRequiredService<ReturnsDbContext>());

        services.AddScoped<IReturnRequestRepository, ReturnRequestRepository>();

        // Règles commerciales du retour (fenêtre après livraison). Singleton : elles ne
        // changent pas d'une requête à l'autre, et un redémarrage est le moment normal
        // de les faire évoluer.
        services.AddSingleton<IReturnPolicySettings, ReturnPolicySettings>();

        // Deux événements distincts, et la distinction est TOUT le correctif :
        // « validé » (décision, aucun argent) et « versé » (argent parti, référence
        // FedaPay à l'appui). Seul le second engage la comptabilité.
        services.AddScoped<IDomainEventHandler<ReturnRefundApprovedDomainEvent>, ReturnRefundApprovedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ReturnRefundedDomainEvent>, ReturnRefundedDomainEventHandler>();

        // La cagnotte du client a été créditée : la demande de retour se clôt d'elle-même.
        // C'est le retour de boucle qui remplace la confirmation manuelle chez FedaPay.
        services.AddScoped<IIntegrationEventHandler<CustomerRefundCreditedIntegrationEvent>, ConfirmRefundOnCustomerWalletCreditedHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<ReturnsDbContext>();
    }
}
