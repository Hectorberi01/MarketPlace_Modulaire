using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Cart.Contracts.IntegrationEvents;
using Marketplace.Modules.Cart.Domain.Carts.Events;

namespace Marketplace.Modules.Cart.Application.Carts.EventHandlers;

/// <summary>
/// Publie l'IntegrationEvent « panier validé ».
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CET ÉVÉNEMENT N'A AUCUN CONSOMMATEUR — ET LA DOC PRÉTENDAIT LE CONTRAIRE.
///
/// Elle annonçait « consommé par Ordering / analytics ». Vérification faite sur `src`
/// et `tests` (hors `bin`/`obj`), les deux seules occurrences de
/// `CartCheckedOutIntegrationEvent` sont le contrat et cette publication : personne
/// ne l'écoute. Ordering ne s'en sert pas — il n'a jamais eu à s'en servir, c'est
/// LUI qui, en publiant `OrderPlaced`, fait fermer le panier
/// (`CloseCartOnOrderPlacedHandler`). La flèche allait donc dans l'autre sens que ce
/// que la doc décrivait, et un lecteur pressé pouvait croire que fermer un panier
/// suffisait à créer une commande. C'est exactement la croyance qui avait laissé
/// vivre `POST /api/cart/checkout`.
///
/// La publication est CONSERVÉE : le fait métier est réel, il est désormais toujours
/// consécutif à une commande, et le contrat sert de point d'accroche à une future
/// consommation (analytique, relance). Mais tant que personne n'y est abonné, il ne
/// faut compter sur lui pour rien.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class CartCheckedOutDomainEventHandler : IDomainEventHandler<CartCheckedOutDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public CartCheckedOutDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(CartCheckedOutDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new CartCheckedOutIntegrationEvent { CartId = domainEvent.CartId, BuyerId = domainEvent.BuyerId },
            cancellationToken);
}
