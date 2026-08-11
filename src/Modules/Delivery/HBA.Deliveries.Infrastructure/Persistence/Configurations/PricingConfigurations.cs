using System.Text.Json;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Deliveries.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        builder.ToTable("delivery_zones");

        builder.HasKey(z => z.Id);
        builder.Property(z => z.Id)
            .HasConversion(id => id.Value, value => new DeliveryZoneId(value))
            .ValueGeneratedNever();

        builder.Property(z => z.Name).HasMaxLength(120).IsRequired();
        builder.Property(z => z.City).HasMaxLength(120).IsRequired();
        builder.Property(z => z.BaseFee).HasPrecision(12, 2).IsRequired();
        builder.Property(z => z.ExtraFee).HasPrecision(12, 2).IsRequired();
        builder.Property(z => z.RoadFactor).IsRequired();
        builder.Property(z => z.Priority).IsRequired();
        builder.Property(z => z.IsActive).IsRequired();
        builder.Property(z => z.CreatedAtUtc).IsRequired();

        // ─────────────────────────────────────────────────────────────────────
        // LE CONTOUR EN JSONB — ET POURQUOI PAS UN TYPE GÉOMÉTRIQUE.
        //
        // PostGIS ferait mieux : index GiST, requêtes natives, opérateurs
        // spatiaux. Mais c'est une extension à installer sur la base de
        // PRODUCTION, une image Docker à changer, et une dépendance de plus à
        // versionner — pour quelques dizaines de zones que l'on charge en mémoire
        // et teste en microsecondes.
        //
        // Le contour est stocké comme une liste de sommets. Le jour où PostGIS
        // devient justifié — des milliers de zones, ou des requêtes du type
        // « toutes les courses de ce quartier » —, la bascule est une migration
        // de données, pas une réécriture du domaine.
        //
        // ⚠️ LE COMPARATEUR N'EST PAS OPTIONNEL. Sans lui, EF compare les
        // contours par RÉFÉRENCE : une zone rechargée serait vue comme modifiée
        // à chaque SaveChanges, et une zone réellement modifiée pourrait ne pas
        // l'être. C'est l'erreur classique des propriétés converties portant une
        // collection, et elle est silencieuse.
        // ─────────────────────────────────────────────────────────────────────
        builder.Property(z => z.Boundary)
            .HasColumnName("boundary")
            .HasColumnType("jsonb")
            .HasConversion(
                polygon => JsonSerializer.Serialize(polygon.Vertices, Json),
                json => GeoPolygon.Rehydrate(
                    JsonSerializer.Deserialize<List<GeoPoint>>(json, Json) ?? new List<GeoPoint>()),
                new ValueComparer<GeoPolygon>(
                    (left, right) => left != null && right != null && left.Equals(right),
                    polygon => polygon.Vertices.Aggregate(0, (hash, v) => HashCode.Combine(hash, v)),
                    polygon => GeoPolygon.Rehydrate(polygon.Vertices)))
            .IsRequired();

        // Les zones actives sont lues à CHAQUE devis. L'index les sert directement.
        builder.HasIndex(z => new { z.IsActive, z.Priority })
            .HasDatabaseName("ix_delivery_zones_active");
    }
}

internal sealed class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> builder)
    {
        builder.ToTable("pricing_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new PricingRuleId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Version).IsRequired();
        builder.Property(r => r.BasePrice).HasPrecision(12, 2).IsRequired();
        builder.Property(r => r.PricePerKm).HasPrecision(12, 2).IsRequired();
        builder.Property(r => r.MinimumPrice).HasPrecision(12, 2).IsRequired();
        builder.Property(r => r.UrgencySurcharge).HasPrecision(12, 2).IsRequired();
        builder.Property(r => r.MaximumDistanceKm);
        builder.Property(r => r.ValidFromUtc).IsRequired();
        builder.Property(r => r.ValidUntilUtc);
        builder.Property(r => r.IsActive).IsRequired();

        // Nullables : « null » signifie « vaut pour tous ». Stockées en texte,
        // comme partout ailleurs dans ce module.
        builder.Property(r => r.DeliveryType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.VehicleType).HasConversion<string>().HasMaxLength(20);

        // Les paliers de poids appartiennent à la grille : ils naissent et
        // meurent avec elle, et personne ne les interroge séparément.
        builder.OwnsMany(r => r.WeightTiers, tier =>
        {
            tier.ToTable("pricing_weight_tiers");
            tier.WithOwner().HasForeignKey("pricing_rule_id");
            tier.HasKey(t => t.Id);
            tier.Property(t => t.Id).ValueGeneratedNever();
            tier.Property(t => t.FromKg).HasPrecision(9, 3).IsRequired();
            tier.Property(t => t.ToKg).HasPrecision(9, 3);
            tier.Property(t => t.Surcharge).HasPrecision(12, 2).IsRequired();
        });

        builder.HasIndex(r => new { r.IsActive, r.ValidFromUtc })
            .HasDatabaseName("ix_pricing_rules_active");
    }
}

internal sealed class DeliveryQuoteConfiguration : IEntityTypeConfiguration<DeliveryQuote>
{
    public void Configure(EntityTypeBuilder<DeliveryQuote> builder)
    {
        builder.ToTable("delivery_quotes");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id)
            .HasConversion(id => id.Value, value => new DeliveryQuoteId(value))
            .ValueGeneratedNever();

        builder.OwnsOne(q => q.Pickup, p =>
        {
            p.Property(c => c.Latitude).HasColumnName("pickup_latitude").IsRequired();
            p.Property(c => c.Longitude).HasColumnName("pickup_longitude").IsRequired();
        });

        builder.OwnsOne(q => q.Dropoff, p =>
        {
            p.Property(c => c.Latitude).HasColumnName("dropoff_latitude").IsRequired();
            p.Property(c => c.Longitude).HasColumnName("dropoff_longitude").IsRequired();
        });

        builder.OwnsOne(q => q.Weight, w =>
            w.Property(x => x.Kilograms).HasColumnName("weight_kg").HasPrecision(9, 3).IsRequired());

        builder.Property(q => q.Size).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(q => q.DeliveryType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(q => q.VehicleType).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(q => q.DistanceKm).IsRequired();
        builder.Property(q => q.EstimatedDurationMinutes).IsRequired();
        builder.Property(q => q.BasePrice).HasPrecision(12, 2).IsRequired();
        builder.Property(q => q.DistancePrice).HasPrecision(12, 2).IsRequired();
        builder.Property(q => q.ZoneSurcharge).HasPrecision(12, 2).IsRequired();
        builder.Property(q => q.VehicleSurcharge).HasPrecision(12, 2).IsRequired();
        builder.Property(q => q.WeightSurcharge).HasPrecision(12, 2).IsRequired();
        builder.Property(q => q.UrgencySurcharge).HasPrecision(12, 2).IsRequired();
        builder.Property(q => q.Total).HasPrecision(12, 2).IsRequired();
        builder.Property(q => q.Currency).HasMaxLength(3).IsRequired();

        builder.Property(q => q.PricingRuleId).IsRequired();
        builder.Property(q => q.PricingVersion).IsRequired();
        builder.Property(q => q.PickupZoneId);
        builder.Property(q => q.DropoffZoneId);
        builder.Property(q => q.PartnerId);

        builder.Property(q => q.CreatedAtUtc).IsRequired();
        builder.Property(q => q.ExpiresAtUtc).IsRequired();
        builder.Property(q => q.ConsumedByDeliveryId);
        builder.Property(q => q.ConsumedAtUtc);

        // ─────────────────────────────────────────────────────────────────────
        // INDEX SUR LES DEVIS PÉRIMÉS ET NON CONSOMMÉS.
        //
        // Cette table est la seule du module à grossir sans limite utile : la
        // plupart des devis ne deviennent jamais des courses. Il faudra les
        // purger, et cet index sert la requête de purge — pas un besoin
        // fonctionnel.
        // ─────────────────────────────────────────────────────────────────────
        builder.HasIndex(q => q.ExpiresAtUtc)
            .HasDatabaseName("ix_delivery_quotes_expiry")
            .HasFilter("\"ConsumedByDeliveryId\" IS NULL");
    }
}
