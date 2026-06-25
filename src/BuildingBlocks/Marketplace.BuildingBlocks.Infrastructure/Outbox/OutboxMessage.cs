namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Ligne d'outbox : un IntegrationEvent sérialisé, écrit dans la MÊME
/// transaction que le changement d'état métier. Un processeur le publie ensuite
/// (in-process aujourd'hui, Kafka demain) — garantie « au moins une fois ».
/// Vit dans le schéma du module propriétaire.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Nom de type stable « FullName, AssemblyName » (sans version).</summary>
    public string Type { get; init; } = default!;

    /// <summary>Charge utile JSON de l'event.</summary>
    public string Content { get; init; } = default!;

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public DateTime? ProcessedOnUtc { get; set; }

    public string? Error { get; set; }
}
