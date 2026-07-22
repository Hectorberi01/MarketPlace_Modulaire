using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Catalog.Application;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Catalog. Vit dans le schéma « catalog » : pas de JOIN ni
/// de foreign key vers un autre schéma. Hérite de ModuleDbContext pour l'Unit of
/// Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class CatalogDbContext : ModuleDbContext, ICatalogUnitOfWork
{
    public const string SchemaName = "catalog";

    private readonly ICacheService _cache;

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Applique d'abord les configs du module (Product…), puis la base
        // (schéma par défaut + outbox).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// ─────────────────────────────────────────────────────────────────────────
    /// INVALIDATION DU CACHE — POINT DE PASSAGE UNIQUE.
    ///
    /// L'autre solution aurait été d'invalider dans chacun des SEIZE handlers de
    /// commande du module. Elle a été écartée : il aurait suffi qu'UN handler
    /// oublie son invalidation — ou qu'un dix-septième soit écrit demain sans y
    /// penser — pour que le catalogue serve indéfiniment une donnée périmée. Ce
    /// bug-là est invisible en développement (le cache y est vide) et permanent en
    /// production, sans erreur ni trace dans les journaux.
    ///
    /// Le ChangeTracker, lui, SAIT exactement ce qui a changé, et toute écriture
    /// passe forcément par ici. Rien ne peut être oublié.
    ///
    /// ⚠️ L'ORDRE COMPTE : on évince APRÈS le commit.
    ///
    /// Évincer avant, ce serait offrir à une lecture concurrente l'occasion de
    /// recharger l'ANCIENNE valeur en cache juste avant que la nouvelle ne soit
    /// validée — le cache resterait alors faux jusqu'à l'expiration de son TTL.
    ///
    /// Il subsiste une fenêtre de quelques millisecondes entre le commit et
    /// l'éviction, pendant laquelle une lecture peut réécrire l'ancienne valeur.
    /// C'est le compromis assumé du cache-aside, et les TTL courts le bornent. La
    /// fermer exigerait un verrou distribué, dont le coût dépasserait de loin le
    /// préjudice : voir un nom de produit périmé pendant cinq minutes.
    /// ─────────────────────────────────────────────────────────────────────────
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // AVANT le save : après, les entités ajoutées passent à « Unchanged » et les
        // supprimées sont détachées. Le ChangeTracker aurait tout oublié.
        var keysToEvict = CollectCacheKeysToEvict();

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (keysToEvict.Count > 0)
        {
            await _cache.RemoveManyAsync(keysToEvict, cancellationToken);
        }

        return affected;
    }

    private List<string> CollectCacheKeysToEvict()
    {
        var keys = new HashSet<string>();
        var touchedProducts = new HashSet<Guid>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Product product:
                    touchedProducts.Add(product.Id.Value);
                    keys.Add(CatalogCacheKeys.ProductsBySeller(product.SellerId));
                    break;

                // Variantes et médias sont les ENFANTS de l'agrégat. Ajouter une
                // photo modifie l'enfant, pas le Product : un invalidateur qui ne
                // regarderait que les Product laisserait la fiche en cache avec son
                // ancienne image. C'est exactement le genre d'oubli que ce point de
                // passage unique existe pour rendre impossible.
                case ProductVariant:
                case ProductMedia:
                    if (TryReadProductId(entry, out var childProductId))
                    {
                        touchedProducts.Add(childProductId);
                    }
                    break;

                case Category category:
                    keys.Add(CatalogCacheKeys.Category(category.Id.Value));
                    keys.Add(CatalogCacheKeys.AllCategories);
                    break;

                case Brand brand:
                    keys.Add(CatalogCacheKeys.Brand(brand.Id.Value));
                    keys.Add(CatalogCacheKeys.AllBrands);
                    break;
            }
        }

        foreach (var productId in touchedProducts)
        {
            keys.Add(CatalogCacheKeys.Product(productId));

            // La liste de la boutique doit tomber elle aussi. Les enfants ne portent
            // pas le SellerId : on le relit sur l'agrégat, forcément suivi puisque
            // les enfants ne se modifient qu'à travers lui.
            var parent = ChangeTracker.Entries<Product>()
                .Select(e => e.Entity)
                .FirstOrDefault(p => p.Id.Value == productId);

            if (parent is not null)
            {
                keys.Add(CatalogCacheKeys.ProductsBySeller(parent.SellerId));
            }
        }

        return [.. keys];
    }

    /// <summary>
    /// Lit la clé étrangère « ProductId » d'une variante ou d'un média. C'est une
    /// propriété SHADOW (déclarée dans ProductConfiguration, absente du modèle de
    /// domaine) : elle n'existe que dans le ChangeTracker.
    /// </summary>
    private static bool TryReadProductId(EntityEntry entry, out Guid productId)
    {
        productId = Guid.Empty;

        var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "ProductId");
        if (property is null)
        {
            return false;
        }

        // Sur une suppression, CurrentValue peut déjà être vidée : OriginalValue
        // garde alors la seule trace du parent.
        var raw = property.CurrentValue ?? property.OriginalValue;

        // La FK « ProductId » référence Product.Id, qui est un value object
        // fortement typé (`readonly record struct ProductId(Guid Value)`). Dans le
        // ChangeTracker sa valeur CLR est donc un ProductId, PAS un Guid nu — ne
        // tester que `is Guid` renvoyait toujours false, la fiche produit n'était
        // jamais purgée après un ajout de variante/média sur un produit existant,
        // et le cache servait 0 variante pendant tout le TTL. On accepte les deux
        // formes par prudence (une future config pourrait exposer le Guid brut).
        var value = raw switch
        {
            ProductId typed => typed.Value,
            Guid guid => guid,
            _ => Guid.Empty,
        };

        if (value != Guid.Empty)
        {
            productId = value;
            return true;
        }

        return false;
    }
}
