using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>Met à jour les champs descriptifs d'un produit existant.</summary>
public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string Description,
    Guid? BrandId = null,
    string? Gtin = null,
    string? Ean = null,
    Guid? ProductGroupId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<string>? Tags = null,
    /// <summary>
    /// La catégorie où ranger le produit. NULLE = on ne la change pas.
    ///
    /// ÉCRITE EN DERNIÈRE POSITION, ce qui est laid et voulu : ce type est un record
    /// positionnel, et l'insérer à sa place logique — près du nom — décalerait tous
    /// les arguments des appelants existants. Le compilateur n'y verrait rien : les
    /// paramètres voisins sont des `Guid?` et des `string?`, donc un décalage passerait
    /// la compilation pour écrire la marque dans le groupe de produits.
    ///
    /// CHANGER DE CATÉGORIE RETIRE DE LA VENTE un produit actif — voir
    /// `Product.ChangeCategory`. Ce n'est pas un effet de bord discret : c'est la
    /// contrepartie du fait que seule l'administration publie.
    /// </summary>
    Guid? CategoryId = null) : ICommand;
