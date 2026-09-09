using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Application.Returns;

/// <summary>Ouvre une demande de retour pour une ligne d'une commande de l'acheteur.</summary>
public sealed record RequestReturnCommand(Guid BuyerId, Guid OrderId, Guid OfferId, string Reason) : ICommand<Guid>;

public sealed class RequestReturnCommandValidator : AbstractValidator<RequestReturnCommand>
{
    public RequestReturnCommandValidator()
    {
        RuleFor(c => c.BuyerId).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.OfferId).NotEmpty();
        RuleFor(c => c.Reason).Must(v => Enum.TryParse<ReturnReason>(v, true, out _)).WithMessage("Motif de retour invalide.");
    }
}

internal sealed class RequestReturnCommandHandler : ICommandHandler<RequestReturnCommand, Guid>
{
    private readonly IReturnRequestRepository _repository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly IReturnPolicySettings _policy;
    private readonly IReturnsUnitOfWork _unitOfWork;

    public RequestReturnCommandHandler(
        IReturnRequestRepository repository,
        IOrderingModuleApi orderingModuleApi,
        IReturnPolicySettings policy,
        IReturnsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _orderingModuleApi = orderingModuleApi;
        _policy = policy;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RequestReturnCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReturnReason>(command.Reason, ignoreCase: true, out var reason))
        {
            return Result.Failure<Guid>(Error.Validation("returns.reason_invalid", "Motif de retour inconnu."));
        }

        var order = await _orderingModuleApi.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<Guid>(Error.NotFound("returns.order.not_found", "Commande introuvable."));
        }

        // MÊME RÉPONSE QUE « COMMANDE INTROUVABLE », À LA VIRGULE PRÈS.
        //
        // Le refus était 403 `returns.not_owner`, juste après un 404
        // `returns.order.not_found` : deux réponses qui, mises côte à côte, disent si un
        // identifiant de commande correspond à quelque chose. La correction est ici et
        // pas seulement au bord, parce que cette commande est envoyée depuis DEUX hôtes
        // — le BFF client et Marketplace.Api — et qu'une garde posée dans l'un ne
        // couvre pas l'autre.
        //
        // Les trois conflits qui suivent (statut, ligne, retour déjà ouvert) ne posent
        // pas le même problème : on ne les atteint qu'en étant propriétaire.
        if (order.BuyerId != command.BuyerId)
        {
            return Result.Failure<Guid>(Error.NotFound("returns.order.not_found", "Commande introuvable."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // « LIVRÉE » EST ACCEPTÉE. LA REFUSER FERMAIT LA FENÊTRE À L'INSTANT PRÉCIS OÙ
        // L'ACHETEUR VOYAIT LA MARCHANDISE.
        //
        // Ce contrôle n'admettait que `Confirmed`. Or la livraison fait passer la
        // commande de `Confirmed` à `Delivered` : le droit au retour s'éteignait donc au
        // moment où il commence à avoir un sens. Un acheteur ne peut constater qu'un
        // article ne convient pas qu'après l'avoir reçu.
        //
        // Le défaut est resté invisible tant que la boucle de livraison était rompue :
        // aucune commande n'atteignait `Delivered`, donc personne ne butait dessus. Il
        // serait devenu un blocage total le jour même de la réparation de cette boucle.
        //
        // `SubmitReviewCommandHandler` avait EXACTEMENT le même contrôle et il a été
        // corrigé — il accepte les deux états. Le module Returns ne l'avait pas été.
        // Deux modules, une même règle, une seule des deux copies rectifiée : c'est le
        // motif à surveiller, pas le cas particulier.
        // ═════════════════════════════════════════════════════════════════════════
        var isConfirmed = string.Equals(order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase);
        var isDelivered = string.Equals(order.Status, "Delivered", StringComparison.OrdinalIgnoreCase);

        if (!isConfirmed && !isDelivered)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "returns.order_not_eligible",
                "Seule une commande payée ou livrée peut faire l'objet d'un retour."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LA FENÊTRE COURT DEPUIS LA LIVRAISON, ET ELLE N'EXISTAIT PAS DU TOUT.
        //
        // Aucune limite de temps n'était posée nulle part : un retour pouvait être ouvert
        // sur une commande de n'importe quel âge, et le montant remboursable est figé au
        // prix payé. La plateforme portait donc une créance sans échéance sur chaque
        // ligne jamais retournée.
        //
        // DEUX CAS SORTENT DE LA FENÊTRE, ET AUCUN N'EST UN OUBLI :
        //
        //  • Commande CONFIRMÉE non encore livrée — l'acheteur n'a rien reçu, le délai
        //    n'a pas commencé. Refuser ici lui interdirait d'annuler un article qu'il
        //    attend toujours, ce qui est le contraire du service rendu.
        //
        //  • Commande livrée SANS date de livraison — celles livrées avant l'existence
        //    de `DeliveredAtUtc`. On ACCEPTE. Le doute profite à l'acheteur : refuser
        //    reviendrait à opposer une échéance qu'on est incapable de dater, et ces
        //    commandes s'épuiseront d'elles-mêmes.
        // ═════════════════════════════════════════════════════════════════════════
        if (isDelivered && order.DeliveredAtUtc is { } deliveredAt)
        {
            var deadline = deliveredAt + _policy.WindowAfterDelivery;
            if (DateTime.UtcNow > deadline)
            {
                return Result.Failure<Guid>(Error.Conflict(
                    "returns.window_closed",
                    $"Le délai de retour de {_policy.WindowAfterDelivery.TotalDays:0} jours après la livraison est écoulé."));
            }
        }

        var line = order.Lines.FirstOrDefault(l => l.OfferId == command.OfferId);
        if (line is null)
        {
            return Result.Failure<Guid>(Error.Conflict("returns.line_not_found", "Cette ligne n'est pas dans la commande."));
        }

        if (await _repository.ExistsOpenAsync(command.OrderId, command.OfferId, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("returns.already_open", "Un retour est déjà ouvert pour cette ligne."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LE PLAFOND EST CE QUI RESTE SUR LA LIGNE, PAS SON TOTAL.
        //
        // `LineTotal` était pris tel quel à chaque nouveau retour. Or `ExistsOpenAsync`
        // n'exclut, à dessein, que les états terminaux : une fois le remboursement
        // VERSÉ, la ligne redevenait libre — avec son plafond d'origine. On pouvait donc
        // ouvrir un retour, se faire rembourser, en rouvrir un, recommencer. L'index
        // `(OrderId, OfferId)` n'est pas unique, et rien ne bornait le cumul.
        //
        // Le cas EN COURS avait été fermé avec soin, et son commentaire le raconte. Le
        // cas CUMULÉ ne l'avait pas été : c'est le second ordre qui manquait, pas la
        // vigilance.
        //
        // ZÉRO RESTANT N'EST PAS UNE ERREUR DE L'ACHETEUR, c'est une ligne déjà
        // intégralement rendue — d'où un conflit, pas une validation.
        // ═════════════════════════════════════════════════════════════════════════
        var alreadyRefundedOnLine = await _repository.SumCommittedRefundsForLineAsync(
            command.OrderId, command.OfferId, cancellationToken);

        var refundableOnLine = line.LineTotal - alreadyRefundedOnLine;
        if (refundableOnLine <= 0m)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "returns.line_fully_refunded",
                "Cette ligne a déjà été intégralement remboursée."));
        }

        // Le RESTANT devient le plafond du futur remboursement, figé au prix payé.
        var result = ReturnRequest.Create(
            order.Id, command.OfferId, command.BuyerId, line.SellerId, reason, order.Currency, refundableOnLine);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}
