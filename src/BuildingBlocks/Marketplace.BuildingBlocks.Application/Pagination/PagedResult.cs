using System;
using System.Collections.Generic;
using System.Linq;

namespace Marketplace.BuildingBlocks.Application.Pagination;

/// <summary>
/// Page de résultats d'une requête de liste, accompagnée du <b>total non paginé</b>
/// (indispensable au front pour afficher « page 2 / 7 ») et, optionnellement, de
/// <b>facettes</b> — la répartition par statut calculée sur l'ensemble filtré, pas
/// sur la seule page, pour que les graphes restent justes.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyDictionary<string, int>? Facets = null)
{
    /// <summary>Nombre total de pages pour la taille de page courante.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(Total / (double)PageSize) : 0;

    /// <summary>Page vide (aucun résultat), en conservant page/taille demandées.</summary>
    public static PagedResult<T> Empty(int page, int pageSize)
        => new(Array.Empty<T>(), 0, page, pageSize);

    /// <summary>Projette les éléments en préservant total, pagination et facettes.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
        => new(Items.Select(selector).ToList(), Total, Page, PageSize, Facets);
}

/// <summary>Normalisation des paramètres de pagination (bornes défensives).</summary>
public static class PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Ramène page ≥ 1 et pageSize dans [1, 100] (défaut 20).</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }
}
