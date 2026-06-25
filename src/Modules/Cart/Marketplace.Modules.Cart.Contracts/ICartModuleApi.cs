namespace Marketplace.Modules.Cart.Contracts;

/// <summary>
/// API in-process publique du module Cart. Ordering l'appelle au checkout pour
/// récupérer le panier valorisé (lignes + prix effectifs) et le figer dans une
/// commande, sans accéder à la base du panier.
/// </summary>
public interface ICartModuleApi
{
    Task<CartSummary?> GetActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default);

    Task<CartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default);
}
