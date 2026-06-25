using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Reviews.Application.Reviews.Commands.SubmitReview;

/// <summary>Dépose un avis sur un produit d'une commande confirmée de l'acheteur.</summary>
public sealed record SubmitReviewCommand(
    Guid BuyerId, Guid ProductId, Guid OrderId, int Rating, string Title, string Body) : ICommand<Guid>;
