using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Catalog.Domain.Products.Events;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// FAIT DE DOMAINE : LES CHAMPS DESCRIPTIFS D'UN PRODUIT ONT CHANGÉ.
///
/// `Update()` ne levait RIEN — contrairement à `Activate`, `Archive` et `Unpublish`,
/// qui lèvent tous. Le nom et le slug indexés étaient donc figés au jour de la
/// création : un vendeur qui corrigeait le nom de son article restait trouvable sous
/// l'ANCIEN nom uniquement, et introuvable sous le nouveau. Définitivement, puisqu'un
/// read model n'a pas de durée de vie et qu'aucune reconstruction n'existait.
///
/// Le slug est transporté pour la même raison : c'est celui de l'index qui est
/// renvoyé dans les résultats de recherche et qui sert la navigation. Divergent, il
/// mène à une page introuvable.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ProductUpdatedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Slug) : DomainEvent;
