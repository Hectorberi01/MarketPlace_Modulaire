using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Returns.Application.Returns;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>Référence du versement effectué chez FedaPay — la preuve que l'argent est parti.</summary>
public sealed record ConfirmRefundRequest(string ProviderReference);

/// <summary>
/// BFF Admin — LE FILET DE SÉCURITÉ DES REMBOURSEMENTS.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE FILE N'EST PLUS LE CHEMIN NORMAL, ET C'EST VOULU.
///
/// FedaPay n'expose aucun endpoint de remboursement : l'opération se fait depuis
/// leur tableau de bord, à la main. Le remboursement était donc, par nécessité, un
/// geste humain — et chaque geste oublié laissait un client sans son argent.
///
/// Depuis que le remboursement crédite la CAGNOTTE du client, ce geste a disparu du
/// chemin normal : Settlement crédite dès la validation du montant, et la demande se
/// clôt d'elle-même. Une ligne qui apparaît ici signale donc soit une demande
/// antérieure à ce changement, soit un crédit de cagnotte qui n'est jamais arrivé —
/// c'est-à-dire une PANNE, pas une tâche quotidienne.
///
/// La route de confirmation manuelle reste ouverte pour ces cas-là. Elle ne devrait
/// pratiquement jamais servir ; si elle sert souvent, c'est la chorégraphie qu'il
/// faut aller regarder, pas la file qu'il faut vider plus vite.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class AdminRefundEndpoints
{
    public static IEndpointRouteBuilder MapAdminRefundEndpoints(this IEndpointRouteBuilder app)
    {
        var refunds = app.MapAdminGroup("/admin/refunds", "Admin · Remboursements");

        refunds.MapGet("/pending", PendingAsync);
        refunds.MapPost("/{id:guid}/confirm", ConfirmAsync);

        return app;
    }

    /// <summary>
    /// Les remboursements validés dont l'argent n'est pas encore parti, les plus
    /// anciens d'abord — c'est-à-dire les clients qui attendent depuis le plus
    /// longtemps, et donc ceux qui ouvriront un litige en premier.
    /// </summary>
    private static async Task<IResult> PendingAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingRefundsQuery(), ct)).Match(r => Results.Ok(r));

    /// <summary>
    /// Confirme qu'un versement a RÉELLEMENT été effectué HORS CAGNOTTE, référence
    /// FedaPay à l'appui. Voie de secours : le chemin normal est le crédit automatique
    /// de la cagnotte, qui confirme la demande sans intervention.
    ///
    /// Cette route exige une preuve, et c'est toujours le point essentiel. L'appel
    /// déclenche en aval la contre-passation comptable : le gain du vendeur est repris,
    /// la commission de la plateforme restituée. Le faire avant que l'argent ne soit
    /// parti débiterait un vendeur pour un versement qui peut encore échouer — et
    /// personne, dans les comptes, ne saurait dire que c'est arrivé.
    ///
    /// Autrement dit : ne cliquez ici QU'APRÈS avoir vu l'opération passer chez FedaPay.
    /// </summary>
    private static async Task<IResult> ConfirmAsync(Guid id, ConfirmRefundRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmReturnRefundCommand(id, request.ProviderReference), ct))
            .Match(() => Results.NoContent());
}
