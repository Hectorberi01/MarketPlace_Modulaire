using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Infrastructure.Persistence.Configurations;

internal sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("return_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new ReturnRequestId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.OfferId).IsRequired();
        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.SellerId).IsRequired();
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        // Plafond remboursable, figé au prix payé. Non nullable avec défaut 0 : les
        // lignes existantes valent 0, ce que le domaine interprète comme « plafond
        // inconnu » — voir `ReturnRequest.RefundableAmount`.
        builder.Property(r => r.RefundableAmount)
            .HasColumnType("numeric(18,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(r => r.RefundAmount).HasColumnType("numeric(18,2)");
        builder.Property(r => r.RefundId);
        builder.Property(r => r.Carrier).HasMaxLength(100);
        builder.Property(r => r.TrackingNumber).HasMaxLength(100);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.ResolvedAtUtc);

        // Traçabilité du remboursement : QUAND il a été décidé, QUAND l'argent est
        // parti, et sous quelle référence. Les trois sont nullables : un retour rejeté
        // n'en aura jamais aucune.
        builder.Property(r => r.RefundApprovedAtUtc);
        builder.Property(r => r.RefundExecutedAtUtc);

        // La PREUVE que l'argent est parti. Le jour où un client affirme n'avoir rien
        // reçu, c'est cette référence qui permet de retrouver l'opération chez FedaPay.
        builder.Property(r => r.RefundReference).HasMaxLength(120);

        builder.HasIndex(r => r.BuyerId);
        builder.HasIndex(r => r.SellerId);
        builder.HasIndex(r => new { r.OrderId, r.OfferId });

        // La file des versements à exécuter est relue en permanence par le back-office.
        // Sans cet index, elle scanne toute la table des retours à chaque affichage.
        builder.HasIndex(r => r.Status);

        builder.Ignore(r => r.DomainEvents);
    }
}
