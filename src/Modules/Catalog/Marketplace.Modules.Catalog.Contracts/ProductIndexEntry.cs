namespace Marketplace.Modules.Catalog.Contracts;

/// <summary>
/// Projection minimale d'un produit, destinée à la RECONSTRUCTION d'un read model.
/// Ne porte que ce dont un index a besoin : de quoi écrire le document et décider de
/// sa visibilité. Pas de variantes, pas de galerie — seulement l'image principale.
/// </summary>
public sealed record ProductIndexEntry(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Slug,
    string Status,
    string? PrimaryImageUrl);
