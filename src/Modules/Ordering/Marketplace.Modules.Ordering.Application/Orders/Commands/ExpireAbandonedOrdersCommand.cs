using MediatR;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Domain.Orders;

namespace Marketplace.Modules.Ordering.Application.Orders.Commands;

/// <summary>
/// Annule les commandes restées en attente de paiement au-delà du délai, et libère tout
/// ce qu'elles retenaient. Renvoie le nombre de commandes expirées.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE COMMANDE ABANDONNÉE N'EST PAS UNE COMMANDE INERTE.
///
/// Elle retient du stock — invendable pour tout le monde — et, depuis que la cagnotte
/// se dépense au checkout, de l'argent du client immobilisé dans son compte réservé :
/// ni dépensable, ni retirable, et invisible sur son relevé. Le client voit son solde
/// avoir fondu sans qu'aucune ligne n'explique pourquoi.
///
/// Rien ne balayait ces commandes : le trou existait déjà pour le stock, en silence,
/// depuis toujours. La cagnotte le rend visible, et donc intenable.
///
/// LE DÉLAI EST GÉNÉREUX À DESSEIN. Un paiement Mobile Money peut mettre de longues
/// minutes à se confirmer, et un client qui ferme l'application pendant l'opération la
/// reprend souvent plus tard. Expirer trop tôt annulerait des commandes en train
/// d'aboutir — bien pire que de laisser du stock retenu une nuit de plus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ExpireAbandonedOrdersCommand(TimeSpan OlderThan, int BatchSize = 50) : ICommand<int>;

internal sealed class ExpireAbandonedOrdersCommandHandler : ICommandHandler<ExpireAbandonedOrdersCommand, int>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ISender _sender;
    private readonly ILogger<ExpireAbandonedOrdersCommandHandler> _logger;

    public ExpireAbandonedOrdersCommandHandler(
        IOrderRepository orderRepository,
        ISender sender,
        ILogger<ExpireAbandonedOrdersCommandHandler> logger)
    {
        _orderRepository = orderRepository;
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(ExpireAbandonedOrdersCommand command, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - command.OlderThan;
        var ids = await _orderRepository.ListAbandonedIdsAsync(cutoff, command.BatchSize, cancellationToken);

        var expired = 0;
        foreach (var id in ids)
        {
            // ON RÉUTILISE L'ANNULATION ORDINAIRE, et c'est délibéré : elle porte déjà
            // toute la compensation — libération du stock, restitution de la cagnotte,
            // publication de « commande annulée ». Écrire ici un second chemin de
            // libération donnerait deux endroits à tenir d'accord sur ce qui doit être
            // rendu, et l'un des deux finirait par oublier quelque chose.
            var cancelled = await _sender.Send(
                new CancelOrderCommand(id, "Paiement non finalisé : commande expirée automatiquement."),
                cancellationToken);

            if (cancelled.IsSuccess)
            {
                expired++;
                continue;
            }

            // Un échec isolé n'interrompt pas le lot : la commande suivante n'y est pour
            // rien, et le balayage repassera de toute façon.
            _logger.LogWarning(
                "Expiration de la commande {OrderId} impossible ({Code}) — reprise au prochain balayage.",
                id, cancelled.Error.Code);
        }

        return expired;
    }
}
