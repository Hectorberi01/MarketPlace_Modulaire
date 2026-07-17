using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Returns.Application.Returns;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>Référence du versement effectué chez FedaPay — la preuve que l'argent est parti.</summary>
public sealed record ConfirmRefundRequest(string ProviderReference);

/// <summary>
/// BFF Admin — la FILE DES VERSEMENTS À EXÉCUTER.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CETTE FILE EXISTE, ET POURQUOI ELLE EST MANUELLE
///
/// FedaPay n'expose AUCUN endpoint de remboursement. Leur documentation est
/// formelle : l'opération se fait depuis leur tableau de bord, et uniquement en MTN
/// Mobile Money. Aucune quantité de code ne changera cela.
///
/// Le versement est donc, par nécessité, un geste humain. Le rôle du logiciel n'est
/// pas de le simuler — c'était précisément le bug — mais de le RENDRE VISIBLE, de
/// n'en oublier aucun, et d'exiger une preuve quand il est fait.
///
/// Cette liste devrait être VIDE en fin de journée. Chaque ligne qui y traîne est un
/// client à qui l'on a promis son argent et qui ne l'a pas reçu.
/// ─────────────────────────────────────────────────────────────────────────────
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
    /// Confirme qu'un versement a RÉELLEMENT été effectué, référence FedaPay à l'appui.
    ///
    /// ⚠️ C'est la seule porte vers le statut « Remboursé », et elle exige une preuve.
    ///
    /// Cet appel déclenche, en aval, la contre-passation comptable : le gain du vendeur
    /// est repris, la commission de la plateforme restituée. Le faire avant que l'argent
    /// ne soit parti débiterait un vendeur pour un versement qui peut encore échouer —
    /// et personne, dans les comptes, ne saurait dire que c'est arrivé.
    ///
    /// Autrement dit : ne cliquez ici QU'APRÈS avoir vu l'opération passer chez FedaPay.
    /// </summary>
    private static async Task<IResult> ConfirmAsync(Guid id, ConfirmRefundRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmReturnRefundCommand(id, request.ProviderReference), ct))
            .Match(() => Results.NoContent());
}
