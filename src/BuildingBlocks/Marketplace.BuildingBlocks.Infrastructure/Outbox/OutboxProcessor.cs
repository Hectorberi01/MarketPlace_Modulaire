using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Infrastructure.Events;
using Marketplace.BuildingBlocks.Infrastructure.Serialization;
using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Processeur d'outbox d'un module : poll les messages non traités, les publie
/// (dispatch in-process) et les marque traités. Générique sur le DbContext du
/// module pour rester une seule implémentation réutilisée partout.
///
/// Un seul artefact en Phase 1 ; en multi-instances, protéger la lecture par un
/// SELECT ... FOR UPDATE SKIP LOCKED pour ne pas publier deux fois (cf. dossier).
/// </summary>
public sealed class OutboxProcessor<TDbContext> : BackgroundService
    where TDbContext : DbContext, IOutboxDbContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor<TDbContext>> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Arrêt normal de l'hôte : on sort proprement sans faire planter le host.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du traitement de l'outbox de {Context}", typeof(TDbContext).Name);

                // Pause avant nouvel essai, elle aussi protégée contre l'annulation.
                try
                {
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            try
            {
                var type = EventTypeName.Resolve(message.Type);
                if (JsonSerializer.Deserialize(message.Content, type, SerializerOptions) is not IntegrationEvent integrationEvent)
                {
                    throw new InvalidOperationException($"Désérialisation impossible pour {message.Type}");
                }

                await dispatcher.DispatchAsync(integrationEvent, cancellationToken);
                message.ProcessedOnUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // On garde la trace ; le message reste non traité et sera réessayé.
                message.Error = ex.Message;
                _logger.LogError(ex, "Échec de publication de l'outbox message {MessageId}", message.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
