using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Marketplace.Modules.Shipping.Domain.Zones;

namespace Marketplace.Modules.Shipping.Infrastructure.Persistence.Configurations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LA GRILLE TARIFAIRE EN BASE.
///
/// Deux collections, deux traitements différents — et la différence n'est pas
/// stylistique.
///
/// LES COMMUNES sont des chaînes plates, filtrées par appartenance et jamais
/// interrogées individuellement en SQL. `text[]` natif PostgreSQL les stocke sans
/// conversion, et laisse la porte ouverte à un index GIN le jour où le nombre de
/// zones le justifiera. C'est déjà le traitement des étiquettes produit.
///
/// LES TARIFS sont des objets-valeur à cinq champs. Une table enfant serait le
/// réflexe, mais un tarif n'a pas d'identité : on ne le référence jamais, on ne le
/// liste jamais hors de sa zone, et une jointure de plus sur le chemin du checkout
/// se paierait à chaque devis. En jsonb, la zone se lit d'une seule ligne.
///
/// LES DEUX SONT DES « FIELD-ONLY PROPERTIES ». L'agrégat n'expose que des vues en
/// lecture seule (`CommuneCodes`, `Rates`), qu'EF ne sait pas remplir ; ce sont donc
/// les champs qui sont mappés, et les vues sont ignorées. Sans ces deux `Ignore`, EF
/// tenterait de mapper la même donnée deux fois.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class ShippingZoneConfiguration : IEntityTypeConfiguration<ShippingZone>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ShippingZone> builder)
    {
        builder.ToTable("shipping_zones");

        builder.HasKey(z => z.Id);

        builder.Property(z => z.Id)
            .HasConversion(id => id.Value, value => new ShippingZoneId(value))
            .ValueGeneratedNever();

        builder.Property(z => z.Code).HasMaxLength(ShippingZone.MaxCode).IsRequired();
        builder.Property(z => z.Name).HasMaxLength(ShippingZone.MaxName).IsRequired();
        builder.Property(z => z.IsFallback).IsRequired();
        builder.Property(z => z.IsActive).IsRequired();
        builder.Property(z => z.CreatedAtUtc).IsRequired();

        // Le code identifie la zone de façon unique : c'est lui qui circule dans les
        // URL d'administration, et deux zones homonymes rendraient toute correction
        // ambiguë.
        builder.HasIndex(z => z.Code).IsUnique();

        // ─────────────────────────────────────────────────────────────────────────
        // UNE SEULE ZONE DE REPLI, IMPOSÉE PAR LA BASE.
        //
        // L'agrégat ne peut pas garantir cette règle : elle porte sur l'ENSEMBLE des
        // zones, et aucune instance ne voit les autres. Le résolveur sait déjà
        // départager plusieurs replis de façon stable, mais départager n'est pas
        // empêcher — deux replis actifs veulent dire que deux administrateurs ont
        // chacun cru fixer le tarif national. Index unique partiel : la contrainte
        // ne porte que sur les replis actifs, et l'écriture fautive échoue au lieu
        // de créer une ambiguïté silencieuse.
        // ─────────────────────────────────────────────────────────────────────────
        builder.HasIndex(z => z.IsFallback)
            .IsUnique()
            .HasFilter("\"IsFallback\" = true AND \"IsActive\" = true")
            .HasDatabaseName("ux_shipping_zones_single_active_fallback");

        builder.Property<List<string>>("_communeCodes")
            .HasColumnName("CommuneCodes")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property<List<ShippingRate>>("_rates")
            .HasColumnName("Rates")
            .HasColumnType("jsonb")
            // Les deux lambdas sont des ARBRES D'EXPRESSION, pas des délégués : elles
            // ne peuvent contenir ni corps à instructions ni groupe de méthodes. D'où
            // deux appels à des méthodes statiques nommées, seule forme qu'un arbre
            // d'expression accepte ici — c'est déjà le procédé du convertisseur de
            // dimensions du catalogue.
            .HasConversion(
                rates => Serialize(rates),
                json => Deserialize(json),
                new ValueComparer<List<ShippingRate>>(
                    (left, right) => AreEqual(left, right),
                    value => ComputeHash(value),
                    value => Clone(value)))
            .IsRequired();

        builder.Ignore(z => z.CommuneCodes);
        builder.Ignore(z => z.Rates);
        builder.Ignore(z => z.DomainEvents);
    }

    private static string Serialize(List<ShippingRate> rates)
        => JsonSerializer.Serialize(rates.Select(RateRow.From).ToList(), JsonOptions);

    private static bool AreEqual(List<ShippingRate>? left, List<ShippingRate>? right)
        => left is null ? right is null : right is not null && left.SequenceEqual(right);

    private static int ComputeHash(List<ShippingRate> rates)
        => rates.Aggregate(0, (hash, rate) => HashCode.Combine(hash, rate.GetHashCode()));

    private static List<ShippingRate> Clone(List<ShippingRate> rates) => [.. rates];

    private static List<ShippingRate> Deserialize(string json)
    {
        var rows = JsonSerializer.Deserialize<List<RateRow>>(json, JsonOptions) ?? [];

        // `Create` revalide chaque ligne relue. Une ligne abîmée en base est ÉCARTÉE
        // plutôt que de faire échouer le chargement de la zone : un tarif illisible
        // doit coûter une option de livraison, pas l'accès à toute la grille — et le
        // repli existe précisément pour ces cas.
        return [.. rows
            .Select(r => ShippingRate.Create(r.ServiceCode, r.Label, r.Eta, r.Amount, r.Currency))
            .Where(r => r.IsSuccess)
            .Select(r => r.Value)];
    }

    /// <summary>
    /// Forme JSON du tarif. Un type dédié, et non l'objet-valeur sérialisé
    /// directement : le format en base ne doit pas suivre les renommages du domaine,
    /// sous peine qu'un `Rename` de propriété rende illisibles toutes les grilles
    /// déjà enregistrées.
    /// </summary>
    private sealed record RateRow(string ServiceCode, string Label, string Eta, decimal Amount, string Currency)
    {
        public static RateRow From(ShippingRate rate)
            => new(rate.ServiceCode, rate.Label, rate.Eta, rate.Amount, rate.Currency);
    }
}
