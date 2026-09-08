using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Delivery.Application.Couriers;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — les coursiers, côté marketplace.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CETTE CONSOLE FAIT, ET CE QU'ELLE NE FAIT PAS.
///
/// Elle décide QUI A LE DROIT D'ÊTRE PAYÉ : enregistrer un coursier, le vérifier, le
/// suspendre, corriger son compte de versement, et valider ses retraits.
///
/// Elle ne pilote AUCUNE course. Le dispatch, les positions, la disponibilité et les
/// notes vivent dans le service coursier déployé à part — les afficher ici supposerait
/// de les recopier, et il y aurait alors deux vérités sur la même personne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class AdminCourierEndpoints
{
    public static IEndpointRouteBuilder MapAdminCourierEndpoints(this IEndpointRouteBuilder app)
    {
        var couriers = app.MapAdminGroup("/admin/couriers", "Admin · Coursiers");

        couriers.MapGet("", ListAsync);
        couriers.MapGet("/{id:guid}", GetAsync);
        couriers.MapPost("", RegisterAsync);
        couriers.MapPost("/{id:guid}/activate", ActivateAsync);
        couriers.MapPost("/{id:guid}/suspend", SuspendAsync);
        couriers.MapPut("/{id:guid}/payout", ChangePayoutAsync);
        couriers.MapPut("/{id:guid}/external-ref", LinkExternalRefAsync);

        // File des retraits livreur, séparée de celle des vendeurs et de celle des
        // clients : ce ne sont pas les mêmes contrôles ni les mêmes interlocuteurs.
        couriers.MapGet("/withdrawals/pending", PendingWithdrawalsAsync);

        return app;
    }

    /// <summary>Liste des coursiers, filtrable par statut (« Pending », « Active », « Suspended »).</summary>
    private static async Task<IResult> ListAsync(string? status, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCouriersQuery(status), ct)).Match(list => Results.Ok(list));

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCourierQuery(id), ct)).Match(c => Results.Ok(c));

    /// <summary>Enregistre un coursier. Il naît NON payable : la vérification reste à faire.</summary>
    private static async Task<IResult> RegisterAsync(RegisterCourierRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new RegisterCourierCommand(
                    request.UserId, request.DisplayName, request.Phone,
                    request.PayoutProvider, request.PayoutAccountNumber,
                    request.PayoutAccountName, request.ExternalRef),
                ct))
            .Match(c => Results.Ok(c));

    /// <summary>Vérification terminée : le coursier devient payable.</summary>
    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivateCourierCommand(id), ct)).Match(c => Results.Ok(c));

    /// <summary>
    /// Suspend un coursier. SON SOLDE RESTE LE SIEN et reste retirable : ce qu'il a
    /// livré, il l'a gagné. Seules les nouvelles courses ne lui sont plus payées.
    /// </summary>
    private static async Task<IResult> SuspendAsync(Guid id, SuspendCourierRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendCourierCommand(id, request.Reason), ct)).Match(c => Results.Ok(c));

    /// <summary>Corrige le compte Mobile Money sur lequel le coursier est payé.</summary>
    private static async Task<IResult> ChangePayoutAsync(Guid id, ChangePayoutRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new ChangeCourierPayoutCommand(id, request.Provider, request.AccountNumber, request.AccountName), ct))
            .Match(c => Results.Ok(c));

    /// <summary>Rattache le coursier à son identifiant dans le service de dispatch.</summary>
    private static async Task<IResult> LinkExternalRefAsync(Guid id, LinkExternalRefRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new LinkCourierExternalRefCommand(id, request.ExternalRef), ct)).Match(c => Results.Ok(c));

    /// <summary>Retraits livreur en attente de validation admin.</summary>
    private static async Task<IResult> PendingWithdrawalsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingCourierWithdrawalsQuery(), ct)).Match(list => Results.Ok(list));

    public sealed record RegisterCourierRequest(
        Guid UserId, string DisplayName, string Phone,
        string PayoutProvider, string PayoutAccountNumber,
        string? PayoutAccountName = null, string? ExternalRef = null);

    public sealed record SuspendCourierRequest(string Reason);

    public sealed record ChangePayoutRequest(string Provider, string AccountNumber, string? AccountName = null);

    public sealed record LinkExternalRefRequest(string ExternalRef);
}
