using HBA.Deliveries.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Deliveries.Infrastructure.Persistence;

internal sealed class WebhookDeliveryRepository : IWebhookDeliveryRepository
{
    private readonly DeliveriesDbContext _dbContext;

    public WebhookDeliveryRepository(DeliveriesDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(
        DateTime nowUtc, int take = 50, CancellationToken cancellationToken = default)
        // SUIVI, et non AsNoTracking : la boucle modifie ces lignes (tentative,
        // prochaine échéance, statut) et les enregistre à la fin du tour. Les
        // détacher rendrait ces écritures silencieusement sans effet — la file se
        // remplirait sans jamais se vider, en réessayant les mêmes envois.
        => await _dbContext.WebhookDeliveries
            .Where(w => w.Status == WebhookStatus.Pending && w.NextAttemptAtUtc <= nowUtc)
            .OrderBy(w => w.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default)
        => await _dbContext.WebhookDeliveries.AddAsync(delivery, cancellationToken);
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WebhookDeliveryId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.PartnerId).IsRequired();
        builder.Property(w => w.EventId).IsRequired();
        builder.Property(w => w.EventType).HasMaxLength(60).IsRequired();

        // Le corps signé, tel qu'il partira. jsonb serait tentant — il ne l'est
        // pas : PostgreSQL réordonne les clés d'un jsonb et normalise les espaces,
        // donc la chaîne relue ne serait plus celle qui a été signée, et AUCUNE
        // signature ne serait vérifiable par le partenaire.
        builder.Property(w => w.Payload).HasColumnType("text").IsRequired();

        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(w => w.Attempts).IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.NextAttemptAtUtc).IsRequired();
        builder.Property(w => w.DeliveredAtUtc);
        builder.Property(w => w.LastStatusCode);
        builder.Property(w => w.LastError).HasMaxLength(500);

        // ─────────────────────────────────────────────────────────────────────
        // INDEX PARTIEL SUR LA SEULE FILE VIVANTE.
        //
        // La boucle pose toutes les quinze secondes la même question : « qu'est-ce
        // qui est dû ? ». Sur une table qui ne fait que grossir, un index complet
        // deviendrait surtout un index sur « Delivered » — la valeur la plus
        // fréquente, et la seule dont on ne fait plus jamais rien.
        // ─────────────────────────────────────────────────────────────────────
        builder.HasIndex(w => new { w.Status, w.NextAttemptAtUtc })
            .HasDatabaseName("ix_webhook_deliveries_due")
            .HasFilter("\"Status\" = 'Pending'");

        // Retrouver l'historique d'un partenaire : c'est la requête du support
        // quand on demande « ai-je bien reçu la notification de la commande X ? ».
        builder.HasIndex(w => new { w.PartnerId, w.CreatedAtUtc })
            .HasDatabaseName("ix_webhook_deliveries_partner");
    }
}
