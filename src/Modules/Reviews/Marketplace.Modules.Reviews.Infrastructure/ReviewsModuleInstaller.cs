using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Reviews.Application.Abstractions;
using Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;
using Marketplace.Modules.Reviews.Application.Reviews.EventHandlers;
using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Reviews.Domain.Reviews;
using Marketplace.Modules.Reviews.Domain.Reviews.Events;
using Marketplace.Modules.Reviews.Infrastructure.Persistence;
using Marketplace.Modules.Reviews.Infrastructure.Public;

namespace Marketplace.Modules.Reviews.Infrastructure;

/// <summary>Enregistre le module Reviews : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class ReviewsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Reviews";

    public Assembly ApplicationAssembly => typeof(SubmitReviewCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<ReviewsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ReviewsDbContext.SchemaName)));

        services.AddScoped<IReviewsUnitOfWork>(sp => sp.GetRequiredService<ReviewsDbContext>());

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IReviewsModuleApi, ReviewsModuleApi>();

        services.AddScoped<IDomainEventHandler<ReviewPublishedDomainEvent>, ReviewPublishedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ReviewRejectedDomainEvent>, ReviewRejectedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<ReviewsDbContext>();
    }
}
