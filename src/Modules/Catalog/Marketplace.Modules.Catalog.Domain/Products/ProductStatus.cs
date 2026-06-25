namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>Cycle de vie d'une fiche produit (cf. dossier, module Catalog).</summary>
public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}
