using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Sellers.Application;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Sellers (schéma « sellers »). Hérite de ModuleDbContext
/// pour l'Unit of Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class SellersDbContext : ModuleDbContext, ISellerUnitOfWork
{
    public const string SchemaName = "sellers";

    private readonly ICacheService _cache;

    public SellersDbContext(
        DbContextOptions<SellersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Seller> Sellers => Set<Seller>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SellersDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Invalidation du cache — point de passage unique, comme dans Catalog.
    ///
    /// Le vendeur est mis en cache sous DEUX clés : par identifiant de vendeur, et
    /// par identifiant d'utilisateur (la connexion vendeur passe par là). Les deux
    /// doivent tomber ensemble. N'en évincer qu'une, c'est laisser l'autre servir
    /// un nom de boutique périmé — un bug d'autant plus retors qu'il ne se
    /// manifeste que sur l'un des deux chemins de lecture.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var keysToEvict = CollectCacheKeysToEvict();

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (keysToEvict.Count > 0)
        {
            await _cache.RemoveManyAsync(keysToEvict, cancellationToken);
        }

        return affected;
    }

    /// <summary>
    /// ─────────────────────────────────────────────────────────────────────────────
    /// ON ÉVINCE PAR AGRÉGAT, PAS PAR ENTITÉ MODIFIÉE.
    ///
    /// La version précédente déduisait le vendeur à évincer de la FK ombre « SellerId »
    /// portée par la pièce KYB :
    ///
    ///     if ((p?.CurrentValue ?? p?.OriginalValue) is Guid id &amp;&amp; id != Guid.Empty)
    ///
    /// Ce test ne se déclenchait JAMAIS sur une SUPPRESSION. Quand un enfant est retiré
    /// de la collection du parent, EF rompt d'abord la relation : la FK ombre — de type
    /// `Guid`, donc NON nullable — retombe à `Guid.Empty`. Elle n'est pas `null`, si
    /// bien que le `??` ne consultait jamais `OriginalValue`, et le garde-fou
    /// `!= Guid.Empty` rejetait le vendeur. Aucune clé évincée.
    ///
    /// Le symptôme n'était pas « une pièce mal affichée ». Le vendeur supprimait une
    /// pièce, la base la supprimait bien, l'écran continuait de l'afficher (résumé en
    /// cache) — et le second clic sur la corbeille répondait « Pièce KYB introuvable
    /// pour cette boutique ». Un utilisateur devant une ligne fantôme qu'il ne peut ni
    /// faire disparaître ni supprimer.
    ///
    /// La correction ne consiste pas à ajouter un cas de plus. Elle consiste à cesser
    /// de raisonner sur l'état d'une ENTITÉ pour raisonner sur l'AGRÉGAT : le résumé
    /// mis en cache contient les pièces KYB, donc TOUT vendeur chargé dans ce contexte
    /// au moment d'une écriture est suspect. C'est plus large, c'est volontaire — et
    /// c'est de toute façon un seul agrégat par requête.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private List<string> CollectCacheKeysToEvict()
    {
        // Rien à écrire : rien à évincer. Évite de purger le cache sur une simple
        // lecture, ce qui reviendrait à ne pas en avoir.
        if (!ChangeTracker.HasChanges())
        {
            return [];
        }

        var keys = new HashSet<string>();

        // Tout vendeur SUIVI, quel que soit son état — y compris « Unchanged ».
        //
        // Ajouter ou retirer une pièce ne modifie pas l'entité Seller elle-même :
        // elle reste « Unchanged » alors que son résumé, lui, a changé. C'est
        // précisément le cas que l'ancienne version laissait passer.
        foreach (var seller in ChangeTracker.Entries<Seller>().Select(e => e.Entity))
        {
            keys.Add(SellersCacheKeys.Seller(seller.Id.Value));
            // Les deux clés tombent ensemble : n'en évincer qu'une laisserait l'autre
            // servir un résumé périmé, sur l'un seulement des deux chemins de lecture.
            keys.Add(SellersCacheKeys.SellerByUser(seller.UserId));
        }

        // Filet : une pièce touchée sans que son parent soit chargé (cas qui ne
        // devrait pas se produire — on passe toujours par l'agrégat — mais qui, s'il
        // survenait, laisserait un résumé périmé sans aucun signe).
        //
        // `OriginalValue` D'ABORD : sur une suppression, c'est la seule valeur qui
        // désigne encore le parent.
        foreach (var entry in ChangeTracker.Entries<KybDocument>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var fk = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "SellerId");
            if (fk is null)
            {
                continue;
            }

            foreach (var candidate in new[] { fk.OriginalValue, fk.CurrentValue })
            {
                if (candidate is Guid sellerId && sellerId != Guid.Empty)
                {
                    keys.Add(SellersCacheKeys.Seller(sellerId));
                }
            }
        }

        return [.. keys];
    }
}
