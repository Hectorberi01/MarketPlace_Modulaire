using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Returns.Domain.Returns;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Returns.Application.Returns.EventHandlers;

/// <summary>
/// Clôt une demande de retour dès que la cagnotte du client a été créditée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CONFIRMATION N'EST PLUS UN GESTE, C'EST UNE CONSÉQUENCE.
///
/// « Confirmer le remboursement » exigeait jusqu'ici qu'un administrateur aille voir
/// chez FedaPay si l'argent était parti, puis recopie la référence. La règle du
/// domaine était juste — on ne déclare pas « remboursé » sans preuve — mais la preuve
/// était une chaîne de caractères tapée à la main, que rien ne vérifiait.
///
/// Le crédit de cagnotte fournit une preuve d'une autre nature : une écriture au grand
/// livre, dont l'existence est vérifiable et dont la somme doit rendre le solde du
/// client. La demande se clôt donc toute seule, sur une preuve plus solide que celle
/// qu'un humain pouvait fournir.
///
/// L'ancienne route de confirmation manuelle reste ouverte : elle sert aux
/// remboursements versés hors cagnotte, et aux demandes créées avant ce changement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ConfirmRefundOnCustomerWalletCreditedHandler
    : IIntegrationEventHandler<CustomerRefundCreditedIntegrationEvent>
{
    private readonly IReturnRequestRepository _repository;
    private readonly IReturnsUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmRefundOnCustomerWalletCreditedHandler> _logger;

    public ConfirmRefundOnCustomerWalletCreditedHandler(
        IReturnRequestRepository repository,
        IReturnsUnitOfWork unitOfWork,
        ILogger<ConfirmRefundOnCustomerWalletCreditedHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        CustomerRefundCreditedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var request = await _repository.GetByIdAsync(new ReturnRequestId(e.ReturnRequestId), cancellationToken);
        if (request is null)
        {
            _logger.LogError(
                "Cagnotte créditée pour le retour {ReturnRequestId}, INTROUVABLE dans Returns. " +
                "Le client a son argent mais la demande reste ouverte — à traiter à la main.",
                e.ReturnRequestId);
            return;
        }

        // IDEMPOTENCE PAR LE DOMAINE : la règle « seul un remboursement en attente de
        // versement peut être confirmé » rejette d'elle-même un rejeu. On ne teste donc
        // pas le statut ici — la seule vérité est celle de l'agrégat, et la dupliquer
        // dans le handler créerait deux endroits à tenir d'accord.
        var confirmed = request.ConfirmRefundExecuted(e.LedgerReference, DateTime.UtcNow);
        if (confirmed.IsFailure)
        {
            _logger.LogInformation(
                "Retour {ReturnRequestId} : confirmation ignorée ({Code}) — déjà clos ou dans un autre état.",
                e.ReturnRequestId, confirmed.Error.Code);
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Retour {ReturnRequestId} remboursé sur cagnotte ({Amount} {Currency}, réf. {Reference}).",
            e.ReturnRequestId, e.Amount, e.Currency, e.LedgerReference);
    }
}
