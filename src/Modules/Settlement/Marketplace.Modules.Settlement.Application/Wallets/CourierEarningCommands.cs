using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Le service de dispatch annonce une course livrée : on paie le coursier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MONTANT VIENT DE L'EXTÉRIEUR, ET LE MONOLITHE NE LE RECALCULE PAS.
///
/// C'est le service coursier qui sait ce qu'une course vaut : il connaît la distance
/// parcourue, le nombre de points de ramassage, l'heure, la zone. La marketplace ne
/// connaît qu'un forfait de livraison choisi au checkout, qui n'a pas vocation à
/// décider de la rémunération d'un coursier.
///
/// Ce choix a un prix, assumé : QUI APPELLE CETTE COMMANDE DÉCIDE COMBIEN SORT DU
/// PORTEFEUILLE DE LA PLATEFORME. L'endpoint qui l'expose doit donc être authentifié
/// comme un service, pas comme un utilisateur, et le montant doit être plafonné le jour
/// où la règle de tarification sera arrêtée. En attendant, la trace est complète :
/// chaque paiement porte l'identifiant de la course et se relit au grand livre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CreditCourierDeliveryCommand(
    Guid CourierId, Guid OrderId, Guid DeliveryJobId, decimal Amount, string Currency = "XOF") : ICommand;

public sealed class CreditCourierDeliveryCommandValidator : AbstractValidator<CreditCourierDeliveryCommand>
{
    public CreditCourierDeliveryCommandValidator()
    {
        RuleFor(c => c.CourierId).NotEmpty();
        RuleFor(c => c.DeliveryJobId).NotEmpty().WithMessage("L'identifiant de la course est obligatoire (clé d'idempotence).");
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("La rémunération doit être positive.");
    }
}

internal sealed class CreditCourierDeliveryCommandHandler : ICommandHandler<CreditCourierDeliveryCommand>
{
    private readonly IDeliveryModuleApi _couriers;
    private readonly WalletMutations _wallets;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public CreditCourierDeliveryCommandHandler(
        IDeliveryModuleApi couriers,
        WalletMutations wallets,
        IIntegrationEventPublisher publisher,
        ISettlementUnitOfWork unitOfWork)
    {
        _couriers = couriers;
        _wallets = wallets;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CreditCourierDeliveryCommand command, CancellationToken cancellationToken)
    {
        var courier = await _couriers.GetCourierAsync(command.CourierId, cancellationToken);
        if (courier is null)
        {
            return Result.Failure(Error.NotFound("settlement.courier_not_found", "Coursier introuvable."));
        }

        // Un coursier en attente de vérification ou suspendu ne reçoit pas de NOUVEAU
        // crédit. Son solde acquis, lui, reste le sien et reste retirable — voir le
        // résolveur de versement.
        if (!courier.CanBePaid)
        {
            return Result.Failure(Error.Validation(
                "settlement.courier_not_payable",
                $"Ce coursier n'est pas en mesure d'être payé (statut « {courier.Status} »)."));
        }

        // VERROU DE REJEU. Le service de dispatch est un système distinct : son appel
        // peut être rejoué sur timeout réseau sans qu'il sache si le premier a abouti.
        // Sans ce garde, une course serait payée deux fois — et il n'y a aucun moyen de
        // reprendre l'argent d'un coursier qui l'a déjà retiré.
        if (await _wallets.HasLedgerEntryAsync(
                WalletMutations.CourierDeliveryReferenceType, command.DeliveryJobId, cancellationToken))
        {
            return Result.Success();
        }

        await _wallets.PayCourierForDeliveryAsync(
            command.CourierId, command.Amount, command.Currency, command.DeliveryJobId, cancellationToken);

        await _publisher.PublishAsync(
            new CourierEarningCreditedIntegrationEvent
            {
                CourierId = command.CourierId,
                OrderId = command.OrderId,
                DeliveryJobId = command.DeliveryJobId,
                Amount = command.Amount,
                Currency = command.Currency
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
