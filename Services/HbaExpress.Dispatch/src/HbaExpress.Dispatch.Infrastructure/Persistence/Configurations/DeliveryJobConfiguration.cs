using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HbaExpress.Dispatch.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryJobConfiguration : IEntityTypeConfiguration<DeliveryJob>
{
    public void Configure(EntityTypeBuilder<DeliveryJob> builder)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // LE SECOND ÉTAGE DU VERROU « UN SEUL GAGNANT ».
        //
        // DeliveryJob.Accept refuse déjà d'accepter une course qui n'est plus Offered.
        // Ce contrôle lit un état CHARGÉ EN MÉMOIRE : deux transactions parallèles
        // lisent toutes deux « Offered », passent toutes deux, et assignent deux
        // coursiers à la même course. Deux hommes roulent vers la même boutique.
        //
        // xmin tranche. EF l'ajoute au WHERE de l'UPDATE : la seconde transaction
        // touche 0 ligne, EF lève DbUpdateConcurrencyException, et l'application
        // répond « course déjà prise ». Le contrôle métier n'est pas inutile pour
        // autant — c'est lui qui donne un message juste dans le cas ordinaire.
        //
        // ─────────────────────────────────────────────────────────────────────────
        // VÉRIFICATION EXIGÉE PAR L'EXTENSION, FAITE ICI EXPLICITEMENT :
        // un jeton n'est évalué QUE dans un UPDATE de la table parente.
        //
        // Accept() écrit Status, AssignedCourierId et AcceptedAtUtc — TROIS COLONNES DE
        // delivery_jobs. L'UPDATE a donc bien lieu, et le verrou mord.
        //
        // ATTENTION SI LE MODÈLE ÉVOLUE : le jour où l'assignation ne vivrait plus que
        // sur les lignes ENFANTS — par exemple en marquant seulement l'offre gagnante —
        // EF n'émettrait plus aucun UPDATE sur le parent, et ce verrou deviendrait
        // silencieusement inerte. Rien ne le signalerait : les tests unitaires
        // passeraient, et deux coursiers gagneraient la même course en production.
        // C'est exactement le piège rencontré sur InventoryItem.Reserve() côté
        // marketplace, qui a dû se doter d'une colonne StockVersion pour le contourner.
        // ─────────────────────────────────────────────────────────────────────────
        // ═════════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();

        builder.ToTable("delivery_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasConversion(id => id.Value, value => new DeliveryJobId(value))
            .ValueGeneratedNever();

        builder.Property(j => j.OrderId).IsRequired();
        builder.Property(j => j.ShippingFee).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(j => j.Currency).HasMaxLength(3).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.CurrentRound).IsRequired();
        builder.Property(j => j.CreatedAtUtc).IsRequired();
        builder.Property(j => j.AcceptedAtUtc);
        builder.Property(j => j.CompletedAtUtc);
        builder.Property(j => j.FailureReason).HasMaxLength(400);

        builder.Property(j => j.AssignedCourierId)
            .HasConversion(
                id => id!.Value.Value,
                value => new CourierId(value));

        // UNE COURSE PAR COMMANDE, IMPOSÉ PAR LA BASE. L'événement « commande prête »
        // peut arriver deux fois : deux vendeurs qui terminent à la même seconde
        // travaillent dans deux transactions distinctes, et chacune peut conclure que
        // tout est prêt. Un contrôle applicatif seul les laisserait toutes deux passer.
        builder.HasIndex(j => j.OrderId).IsUnique();

        // La file du redispatch : les courses encore offertes, les plus anciennes
        // d'abord. C'est un balayage périodique, il doit rester à quelques millisecondes.
        builder.HasIndex(j => new { j.Status, j.CreatedAtUtc });

        builder.OwnsOne(j => j.Dropoff, dropoff =>
        {
            dropoff.ToTable("delivery_job_dropoffs");
            dropoff.Property(d => d.BuyerId).IsRequired();
            dropoff.Property(d => d.Recipient).HasMaxLength(120);
            dropoff.Property(d => d.Phone).HasMaxLength(20);
            dropoff.Property(d => d.CommuneCode).HasMaxLength(40).IsRequired();
            dropoff.Property(d => d.Quartier).HasMaxLength(120);
            dropoff.Property(d => d.Landmark).HasMaxLength(200);
            dropoff.Property(d => d.Line).HasMaxLength(200);

            dropoff.OwnsOne(d => d.Position, position =>
            {
                position.Property(p => p.Latitude).HasColumnName("Latitude");
                position.Property(p => p.Longitude).HasColumnName("Longitude");
            });
        });

        builder.Navigation(j => j.Dropoff).IsRequired();

        builder.HasMany(j => j.Pickups)
            .WithOne()
            .HasForeignKey("DeliveryJobId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(j => j.Offers)
            .WithOne()
            .HasForeignKey("DeliveryJobId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(j => j.Pickups).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(j => j.Offers).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(j => j.PickupsInSequence);
        builder.Ignore(j => j.FirstPickup);
        builder.Ignore(j => j.DomainEvents);
    }
}

internal sealed class PickupPointConfiguration : IEntityTypeConfiguration<PickupPoint>
{
    public void Configure(EntityTypeBuilder<PickupPoint> builder)
    {
        builder.ToTable("delivery_job_pickups");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // ═════════════════════════════════════════════════════════════════════════
        // LA CLÉ ÉTRANGÈRE EST DÉCLARÉE ICI, AVEC LE TYPE DE LA CLÉ QU'ELLE VISE.
        //
        // Deux pièges se cumulent, et il faut les deux réponses.
        //
        // 1. « DeliveryJobId » est une propriété FANTÔME, fabriquée par EF depuis le
        //    HasForeignKey posé sur l'agrégat parent. L'index plus bas la référence par
        //    son nom, et ApplyConfigurationsFromAssembly n'ordonne pas les
        //    configurations : si celle-ci passe en premier, la propriété n'existe pas
        //    encore et EF refuse de la créer sans type.
        //
        // 2. SON TYPE EST « DeliveryJobId », PAS « Guid ». La clé primaire de la course
        //    est le type fort — c'est une conversion de valeur qui la range en uuid dans
        //    la table, pas un Guid nu. Déclarée en Guid, EF valide le modèle puis refuse
        //    la relation : « cannot target the primary key {'Id' : DeliveryJobId}
        //    because it is not compatible ».
        //
        // La conversion est reprise à l'identique de celle du parent : une propriété
        // fantôme n'hérite de rien, il lui faut la sienne.
        // ═════════════════════════════════════════════════════════════════════════
        builder.Property<DeliveryJobId>("DeliveryJobId")
            .HasConversion(id => id.Value, value => new DeliveryJobId(value))
            .IsRequired();

        builder.Property(p => p.Sequence).IsRequired();
        builder.Property(p => p.ShipmentId).IsRequired();
        builder.Property(p => p.SellerId).IsRequired();
        builder.Property(p => p.SellerName).HasMaxLength(160).IsRequired();
        builder.Property(p => p.SellerPhone).HasMaxLength(20);
        builder.Property(p => p.CommuneCode).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Quartier).HasMaxLength(120);
        builder.Property(p => p.Landmark).HasMaxLength(200);
        builder.Property(p => p.Line).HasMaxLength(200);
        builder.Property(p => p.IsPickedUp).IsRequired();
        builder.Property(p => p.PickedUpAtUtc);
        builder.Property(p => p.Incident).HasMaxLength(400);

        builder.OwnsOne(p => p.Position, position =>
        {
            position.Property(x => x.Latitude).HasColumnName("Latitude");
            position.Property(x => x.Longitude).HasColumnName("Longitude");
        });

        builder.HasMany(p => p.Parcels)
            .WithOne()
            .HasForeignKey("PickupPointId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Parcels).UsePropertyAccessMode(PropertyAccessMode.Field);

        // « Traité » se DÉDUIT de IsPickedUp et d'Incident : aucune colonne, donc aucune
        // migration, et aucun troisième état à maintenir cohérent avec les deux autres.
        // EF ne la mapperait pas de toute façon (ni accesseur en écriture ni champ de
        // stockage) ; on le dit quand même, comme pour les propriétés calculées de la
        // course, pour qu'un futur refactoring en propriété stockée soit un choix et non
        // un accident.
        builder.Ignore(p => p.IsResolved);

        builder.HasIndex("DeliveryJobId", "Sequence").IsUnique();
    }
}

internal sealed class ParcelConfiguration : IEntityTypeConfiguration<Parcel>
{
    public void Configure(EntityTypeBuilder<Parcel> builder)
    {
        builder.ToTable("delivery_job_parcels");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        // Ici « Guid » est JUSTE : PickupPoint est un Entity<Guid>, sa clé n'est pas un
        // type fort. La règle n'est pas « toujours le type fort », c'est « le type de la
        // clé visée ».
        builder.Property<Guid>("PickupPointId").IsRequired();
        builder.Property(p => p.Sku).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Quantity).IsRequired();
    }
}

internal sealed class DispatchOfferConfiguration : IEntityTypeConfiguration<DispatchOffer>
{
    public void Configure(EntityTypeBuilder<DispatchOffer> builder)
    {
        builder.ToTable("dispatch_offers");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        // Même règle que pour les ramassages : type fort, conversion propre.
        builder.Property<DeliveryJobId>("DeliveryJobId")
            .HasConversion(id => id.Value, value => new DeliveryJobId(value))
            .IsRequired();

        builder.Property(o => o.CourierId)
            .HasConversion(id => id.Value, value => new CourierId(value))
            .IsRequired();

        builder.Property(o => o.Round).IsRequired();
        builder.Property(o => o.SentAtUtc).IsRequired();
        builder.Property(o => o.ExpiresAtUtc).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.RespondedAtUtc);

        // « Quelles offres ce coursier a-t-il en attente ? » — la question que pose
        // l'application du livreur à chaque ouverture.
        builder.HasIndex(o => new { o.CourierId, o.Status });

        // UNE SEULE OFFRE PAR COURSIER ET PAR VAGUE. Sans cette contrainte, un bug de
        // dispatch pourrait proposer deux fois la même course au même coursier, et son
        // taux d'acceptation compterait deux refus pour une seule sollicitation.
        builder.HasIndex("DeliveryJobId", "CourierId", "Round").IsUnique();
    }
}
