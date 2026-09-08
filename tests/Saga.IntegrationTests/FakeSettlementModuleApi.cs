using System.Collections.Concurrent;
using Marketplace.Modules.Settlement.Contracts;

namespace Saga.IntegrationTests;

/// <summary>
/// Substitut in-memory de l'API Settlement : isole la saga Ordering du portefeuille
/// réel tout en traçant les engagements, consommations et libérations de cagnotte —
/// les trois moments où de l'argent bouge pendant le checkout.
///
/// Le solde est paramétrable (<see cref="AvailableCredit"/>) pour reproduire le cas
/// qui compte : une cagnotte partiellement engagée ailleurs entre l'affichage du
/// panier et la validation.
/// </summary>
public sealed class FakeSettlementModuleApi : ISettlementModuleApi
{
    public decimal AvailableCredit { get; set; }

    public ConcurrentBag<(Guid OrderId, decimal Amount)> Reserved { get; } = new();
    public ConcurrentBag<(Guid OrderId, decimal Amount)> Confirmed { get; } = new();
    public ConcurrentBag<(Guid OrderId, decimal Amount)> Released { get; } = new();

    public Task<decimal> GetSpendableCreditAsync(Guid customerId, CancellationToken cancellationToken = default)
        => Task.FromResult(AvailableCredit);

    public Task<decimal> TryReserveCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        // Plafonné au solde, comme le domaine : c'est ce plafonnement qui fait qu'une
        // seconde commande n'engage rien plutôt que d'échouer.
        var moved = Math.Min(amount, AvailableCredit);
        if (moved <= 0m)
        {
            return Task.FromResult(0m);
        }

        AvailableCredit -= moved;
        Reserved.Add((orderId, moved));
        return Task.FromResult(moved);
    }

    public Task ConfirmCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        Confirmed.Add((orderId, amount));
        return Task.CompletedTask;
    }

    public Task ReleaseCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        AvailableCredit += amount;
        Released.Add((orderId, amount));
        return Task.CompletedTask;
    }
}
