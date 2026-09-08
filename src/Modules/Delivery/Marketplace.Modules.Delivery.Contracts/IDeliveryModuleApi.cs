namespace Marketplace.Modules.Delivery.Contracts;

/// <summary>
/// API in-process publique du module Delivery.
///
/// Son unique raison d'être aujourd'hui : permettre à Settlement de savoir OÙ verser
/// l'argent d'un coursier. Le résolveur de versement portait jusqu'ici un commentaire
/// disant « le livreur n'a pas encore de module qui porte ses coordonnées » et renvoyait
/// null ; c'est ce trou que cette interface referme.
/// </summary>
public interface IDeliveryModuleApi
{
    Task<CourierSummary?> GetCourierAsync(Guid courierId, CancellationToken cancellationToken = default);

    /// <summary>Le coursier rattaché à un compte utilisateur (écrans « mon compte coursier »).</summary>
    Task<CourierSummary?> GetCourierByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
