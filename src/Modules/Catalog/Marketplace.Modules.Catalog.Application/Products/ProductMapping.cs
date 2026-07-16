using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products;

/// <summary>
/// Projection <see cref="Product"/> → <see cref="ProductSummary"/>.
///
/// Cette projection était recopiée à l'identique dans quatre fichiers
/// (GetProductQueryHandler, ListAllProducts, ListProductsBySeller, CatalogModuleApi).
/// Ce n'était qu'une redondance tant que rien ne s'appuyait dessus.
///
/// Avec le cache, ça devient un piège : la fiche produit et l'API inter-module
/// partagent désormais la MÊME clé Redis. Si leurs deux projections divergeaient —
/// un champ ajouté ici et pas là — la valeur servie dépendrait de qui a rempli le
/// cache en premier. Un bug non déterministe, qui n'apparaît qu'en production.
///
/// Une seule projection. Elle vit ici.
/// </summary>
public static class ProductMapping
{
    public static ProductSummary ToSummary(Product product)
    {
        var variants = product.Variants
            .Select(v => new ProductVariantSummary(
                v.Id,
                v.Sku.Value,
                v.VariantAttributes,
                v.Barcode,
                v.WeightGrams))
            .ToList();

        var media = product.Media
            .OrderBy(m => m.Position)
            .Select(m => new ProductMediaSummary(
                m.Id,
                m.Url,
                m.Type.ToString(),
                m.IsPrimary,
                m.Position,
                m.AltText))
            .ToList();

        return new ProductSummary(
            product.Id.Value,
            product.SellerId,
            product.CategoryId,
            product.BrandId,
            product.Name,
            product.Description,
            product.Slug.Value,
            product.Status.ToString(),
            product.Gtin,
            product.Ean,
            product.ProductGroupId,
            product.Attributes,
            product.Tags.ToList(),
            variants,
            media);
    }
}
