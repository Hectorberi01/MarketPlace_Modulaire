using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — LE PORTEFEUILLE DU COURSIER.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// PAS DE RÔLE « COURSIER », ET C'EST DÉLIBÉRÉ.
///
/// Il aurait été naturel d'ajouter un rôle au jeton. Mais un rôle est une donnée
/// recopiée : il faudrait le poser à l'activation du coursier, le retirer à sa
/// suspension, et il vivrait dans un jeton déjà émis, encore valide plusieurs heures
/// après la suspension. Un coursier suspendu continuerait donc d'être vu comme coursier
/// par le serveur, exactement le temps que dure son jeton.
///
/// ÊTRE ENREGISTRÉ COMME COURSIER EST L'AUTORISATION. On lit l'utilisateur du jeton, on
/// demande au module Delivery s'il a un compte coursier, et la réponse est vraie au
/// moment où on la pose. Rien à synchroniser, rien qui puisse dériver.
///
/// Ces routes ne pilotent AUCUNE course : le dispatch, les offres et les positions
/// appartiennent au service coursier déployé à part. Ici, le coursier voit ce qu'il a
/// gagné et demande à le toucher.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class MobileCourierEndpoints
{
    public static IEndpointRouteBuilder MapMobileCourierEndpoints(this IEndpointRouteBuilder app)
    {
        var courier = app.MapGroup("/mobile/courier").WithTags("Mobile · Coursier").RequireAuthorization();

        courier.MapGet("/me", MeAsync);
        courier.MapGet("/wallet", WalletAsync);
        courier.MapGet("/wallet/transactions", TransactionsAsync);
        courier.MapGet("/wallet/withdrawals", WithdrawalsAsync);
        courier.MapPost("/wallet/withdrawals", RequestWithdrawalAsync);

        return app;
    }

    /// <summary>Le compte coursier de l'utilisateur connecté : statut, coordonnées de versement.</summary>
    private static async Task<IResult> MeAsync(
        ClaimsPrincipal user, IDeliveryModuleApi couriers, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var courier = await couriers.GetCourierByUserAsync(userId, ct);
        return courier is null ? Results.NotFound(new { error = "not_a_courier" }) : Results.Ok(courier);
    }

    /// <summary>Solde du coursier : retirable, et retraits déjà en route.</summary>
    private static async Task<IResult> WalletAsync(
        ClaimsPrincipal user, ISender sender, IDeliveryModuleApi couriers, CancellationToken ct)
        => await WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new GetCourierWalletQuery(courierId), ct)).Match(w => Results.Ok(w)));

    /// <summary>
    /// Relevé des mouvements. Chaque ligne porte l'identifiant de la course qui l'a
    /// produite : c'est ce qui permet au coursier de rattacher un montant à une livraison
    /// précise, plutôt que d'écrire au support pour comprendre son solde.
    /// </summary>
    private static async Task<IResult> TransactionsAsync(
        ClaimsPrincipal user, int? take, ISender sender, IDeliveryModuleApi couriers, CancellationToken ct)
        => await WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new ListCourierWalletTransactionsQuery(courierId, take ?? 50), ct))
                .Match(t => Results.Ok(t)));

    /// <summary>Historique des demandes de retrait du coursier.</summary>
    private static async Task<IResult> WithdrawalsAsync(
        ClaimsPrincipal user, ISender sender, IDeliveryModuleApi couriers, CancellationToken ct)
        => await WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new ListCourierWithdrawalsQuery(courierId), ct)).Match(w => Results.Ok(w)));

    /// <summary>
    /// Demande de versement sur le Mobile Money du coursier. Les fonds sont RETENUS
    /// immédiatement, mais rien ne part avant validation admin : même règle que pour un
    /// vendeur, et pour la même raison — un versement sortant ne se déclenche pas sur la
    /// seule foi d'un appel HTTP.
    /// </summary>
    private static async Task<IResult> RequestWithdrawalAsync(
        ClaimsPrincipal user, WithdrawRequest request, ISender sender, IDeliveryModuleApi couriers, CancellationToken ct)
        => await WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new RequestCourierWithdrawalCommand(courierId, request.Amount), ct))
                .Match(w => Results.Ok(w)));

    /// <summary>
    /// Résout le coursier depuis le JETON, jamais depuis la requête.
    ///
    /// Accepter un identifiant de coursier fourni par l'appelant reviendrait à laisser
    /// n'importe qui consulter — et vider — le portefeuille de n'importe quel livreur.
    /// </summary>
    private static async Task<IResult> WithCourierAsync(
        ClaimsPrincipal user,
        IDeliveryModuleApi couriers,
        CancellationToken ct,
        Func<Guid, Task<IResult>> action)
    {
        if (MobileBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var courier = await couriers.GetCourierByUserAsync(userId, ct);
        if (courier is null)
        {
            return Results.NotFound(new { error = "not_a_courier" });
        }

        return await action(courier.Id);
    }

    /// <summary>Corps de la demande de retrait : le montant, et rien d'autre.</summary>
    public record WithdrawRequest(decimal Amount);
}
