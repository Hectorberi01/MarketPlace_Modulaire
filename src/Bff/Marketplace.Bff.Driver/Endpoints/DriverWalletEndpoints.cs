using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Bff.Driver.Endpoints;

/// <summary>
/// LE PORTEFEUILLE DU COURSIER — /driver/wallet/*.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST LA PARTIE QUI LÉGITIME LA CONSOLE WEB.
///
/// Le coursier ne peut pas faire avancer une course depuis un navigateur : les
/// transitions exigent une position récente (CourierActionPolicy, côté service de
/// dispatch). Mais consulter ce qu'il a gagné, relire ses mouvements et demander un
/// retrait n'ont rien à voir avec le fait d'être sur la route — et se font bien mieux sur
/// un grand écran que sur un téléphone, à la fin de la journée.
///
/// Ces routes sont donc IDENTIQUES pour l'application et pour le web. La règle « pas de
/// course depuis le web » ne les concerne pas, et il n'y a rien à leur ajouter pour ça.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DriverWalletEndpoints
{
    public static IEndpointRouteBuilder MapDriverWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var wallet = app.MapDriverGroup("/driver/wallet", "Driveur · Portefeuille");

        wallet.MapGet("/", WalletAsync).WithName("DriverWallet");
        wallet.MapGet("/transactions", TransactionsAsync).WithName("DriverWalletTransactions");
        wallet.MapGet("/withdrawals", WithdrawalsAsync).WithName("DriverWithdrawals");
        wallet.MapPost("/withdrawals", RequestWithdrawalAsync).WithName("DriverRequestWithdrawal");

        return app;
    }

    /// <summary>Solde du coursier : retirable, et retraits déjà en route.</summary>
    private static Task<IResult> WalletAsync(
        ClaimsPrincipal user, ISender sender, IDeliveryModuleApi couriers, CancellationToken ct)
        => DriverBff.WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new GetCourierWalletQuery(courierId), ct)).Match(w => Results.Ok(w)));

    /// <summary>
    /// Relevé des mouvements. Chaque ligne porte l'identifiant de la course qui l'a
    /// produite : c'est ce qui permet au coursier de rattacher un montant à une livraison
    /// précise, plutôt que d'écrire au support pour comprendre son solde.
    /// </summary>
    private static Task<IResult> TransactionsAsync(
        ClaimsPrincipal user, int? take, ISender sender, IDeliveryModuleApi couriers, CancellationToken ct)
        => DriverBff.WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new ListCourierWalletTransactionsQuery(courierId, take ?? 50), ct))
                .Match(t => Results.Ok(t)));

    /// <summary>Historique des demandes de retrait.</summary>
    private static Task<IResult> WithdrawalsAsync(
        ClaimsPrincipal user, ISender sender, IDeliveryModuleApi couriers, CancellationToken ct)
        => DriverBff.WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new ListCourierWithdrawalsQuery(courierId), ct)).Match(w => Results.Ok(w)));

    /// <summary>
    /// Demande de versement sur le Mobile Money du coursier. Les fonds sont RETENUS
    /// immédiatement, mais rien ne part avant validation admin : même règle que pour un
    /// vendeur, et pour la même raison — un versement sortant ne se déclenche pas sur la
    /// seule foi d'un appel HTTP.
    ///
    /// UN COURSIER SUSPENDU PEUT TOUJOURS RETIRER. La suspension lui retire les courses à
    /// venir, pas ce qu'il a déjà gagné — voir WithdrawalPayoutResolver, qui ne contrôle
    /// délibérément pas le statut. Une suspension n'est pas une confiscation.
    /// </summary>
    private static Task<IResult> RequestWithdrawalAsync(
        ClaimsPrincipal user, WithdrawRequest request, ISender sender,
        IDeliveryModuleApi couriers, CancellationToken ct)
        => DriverBff.WithCourierAsync(user, couriers, ct, async courierId =>
            (await sender.Send(new RequestCourierWithdrawalCommand(courierId, request.Amount), ct))
                .Match(w => Results.Ok(w)));

    /// <summary>Corps de la demande de retrait : le montant, et rien d'autre.</summary>
    public sealed record WithdrawRequest(decimal Amount);
}
