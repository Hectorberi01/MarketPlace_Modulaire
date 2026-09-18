using MediatR;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Delivery.Application.Couriers;
using Marketplace.Modules.Delivery.Contracts;
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
/// Elle ne pilote AUCUNE course. Le dispatch, la disponibilité et les notes vivent dans
/// le service coursier déployé à part.
///
/// UNE SEULE ROUTE FAIT EXCEPTION, ET SEULEMENT EN APPARENCE : « positions ». Elle ne
/// recopie rien — elle interroge le service à l'instant de la demande et transmet sa
/// réponse sans la garder. La règle qu'on tenait à écrire ici était « pas deux vérités
/// sur la même personne » ; un relais sans stockage ne crée pas de seconde vérité, une
/// table de positions dans la marketplace en créerait une.
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
        couriers.MapGet("/withdrawals/processing", ProcessingWithdrawalsAsync);

        // La carte de la flotte, sur l'écran Livraison. Route de LECTURE PURE : elle ne
        // passe pas par MediatR parce qu'il n'y a ni commande, ni requête de domaine —
        // juste un appel sortant, dont le module Delivery est le seul à connaître
        // l'adresse et le secret.
        couriers.MapGet("/positions", FleetAsync);

        // ═══════════════════════════════════════════════════════════════════════════
        // L'ARGENT D'UN COURSIER, ENFIN VISIBLE DE L'ADMINISTRATION.
        //
        // `GetCourierWalletQuery` et le relevé existaient — exposés au seul BFF LIVREUR.
        // Un administrateur voyait donc les demandes de retrait d'un coursier sans
        // pouvoir voir ni son solde, ni ce qu'il avait gagné, ni en combien de courses.
        // Il approuvait un versement sans pouvoir répondre à la seule question qui
        // compte : cet argent, d'où vient-il ?
        //
        // DES LECTURES, ET AUCUNE ÉCRITURE. Approuver ou refuser un retrait reste où
        // c'est déjà : `/admin/wallet/withdrawals/{id}/approve`, un seul chemin pour les
        // trois acteurs. En ouvrir un second ici ferait deux endroits où le même geste
        // s'écrit, donc deux endroits où le garde-fou de simulation devrait être tenu
        // d'accord.
        //
        // PAS DE ROUTE `/wallet` SÉPARÉE : le relevé porte déjà les trois soldes, et une
        // route que rien n'appelle est une surface que personne ne verra casser.
        // ═══════════════════════════════════════════════════════════════════════════
        couriers.MapGet("/{id:guid}/statement", StatementAsync);
        couriers.MapGet("/{id:guid}/withdrawals", WithdrawalsAsync);

        // Le sélecteur de l'écran Règlement. Vue MAIGRE et bornée : `GET ""` juste
        // au-dessus rend des `CourierSummary` complets, compte Mobile Money inclus, ce
        // qui n'a rien à faire dans un menu déroulant.
        couriers.MapGet("/picker", PickerAsync);

        return app;
    }

    /// <summary>Liste des coursiers, filtrable par statut (« Pending », « Active », « Suspended »).</summary>
    private static async Task<IResult> ListAsync(string? status, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCouriersQuery(status), ct)).Match(list => Results.Ok(list));

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var coursier = await sender.Send(new GetCourierQuery(id), ct);
        if (coursier.IsFailure)
        {
            return coursier.Match(c => Results.Ok(c));
        }

        // La photo est sur le COMPTE, pas sur le coursier : la jointure se fait ici,
        // comme sur /driver/me. Un échec de lecture du compte ne fait pas échouer la
        // fiche — l'administration a besoin du statut et du compte de versement bien
        // plus que d'une photo.
        var compte = await sender.Send(new GetUserQuery(coursier.Value.UserId), ct);
        var c = coursier.Value;

        return Results.Ok(new AdminCourierView(
            c.Id, c.UserId, c.DisplayName, c.Phone, c.Status, c.CanBePaid,
            c.ExternalRef, c.Payout, c.CreatedAtUtc,
            compte.IsSuccess ? compte.Value.AvatarUrl : null));
    }

    /// <summary>CourierSummary, plus la photo du compte. Voir la jointure ci-dessus.</summary>
    public sealed record AdminCourierView(
        Guid Id,
        Guid UserId,
        string DisplayName,
        string Phone,
        string Status,
        bool CanBePaid,
        string? ExternalRef,
        CourierPayoutSummary? Payout,
        DateTime CreatedAtUtc,
        string? AvatarUrl);

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

    /// <summary>
    /// Versements coursiers partis chez le prestataire et non confirmés.
    ///
    /// Le pendant de cette file existait pour les vendeurs et pour les clients, pas
    /// pour les coursiers — alors que l'approbation d'un retrait ne fait aucune
    /// différence entre les trois. Un retrait coursier approuvé disparaissait donc de
    /// toutes les surfaces d'administration.
    /// </summary>
    private static async Task<IResult> ProcessingWithdrawalsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListProcessingCourierWithdrawalsQuery(), ct)).Match(list => Results.Ok(list));

    /// <summary>
    /// Où sont les coursiers en ce moment.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// RÉPOND 200 MÊME QUAND LE SERVICE DE DISPATCH EST À TERRE, ET C'EST DÉLIBÉRÉ.
    ///
    /// Un 502 ferait de cette route une erreur comme une autre dans le client : la carte
    /// afficherait un message générique, ou rien. Or l'écran a quelque chose de précis à
    /// dire — quelle panne, et surtout que la carte vide qu'il montre ne veut PAS dire
    /// « aucun coursier sur le terrain ». Cette phrase-là voyage dans le corps, pas dans
    /// un code de statut.
    ///
    /// L'instantané porte `reachable`. Un client qui l'ignore affiche une flotte vide et
    /// se trompe — c'est le seul contrat de cette route, et il est explicite.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> FleetAsync(IDeliveryModuleApi delivery, CancellationToken ct)
        => Results.Ok(await delivery.GetFleetAsync(ct));

    /// <summary>Les coursiers pour un menu déroulant : nom, téléphone, statut.</summary>
    private static async Task<IResult> PickerAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCouriersForPickerQuery(), ct)).Match(l => Results.Ok(l));

    /// <summary>
    /// Le relevé d'un coursier sur une période. Par défaut, les trente derniers jours.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES DATES REÇUES SONT RAMENÉES EN UTC AVANT D'ÊTRE UTILISÉES.
    ///
    /// Une date de chaîne de requête arrive avec le `Kind` que la liaison lui a donné,
    /// et ce n'est pas toujours `Utc` — « 2026-09-01T00:00:00Z » peut ressortir en
    /// heure LOCALE du serveur. Deux conséquences, l'une visible et l'autre non :
    ///
    ///   - Npgsql REFUSE d'écrire un DateTime de `Kind` local dans une colonne
    ///     « timestamp with time zone ». La requête lève, et l'écran affiche une erreur
    ///     technique sur une saisie parfaitement légitime.
    ///   - Un `Kind` non spécifié passe, lui, silencieusement — et décale la période du
    ///     fuseau du serveur. Une course livrée le 1er à 00h30 tombe alors dans le mois
    ///     précédent, et le relevé est faux sans que rien ne le signale.
    ///
    /// La conversion est faite ICI, à la frontière, plutôt que dans le handler : c'est
    /// le seul endroit qui sait que la valeur vient du dehors.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> StatementAsync(
        Guid id, ISender sender, CancellationToken ct, DateTime? from = null, DateTime? to = null)
    {
        var fin = EnUtc(to) ?? DateTime.UtcNow;
        var debut = EnUtc(from) ?? fin.AddDays(-30);

        return (await sender.Send(new GetCourierStatementQuery(id, debut, fin), ct))
            .Match(r => Results.Ok(r));
    }

    /// <summary>Demandes de retrait d'un coursier, toutes périodes et tous statuts.</summary>
    private static async Task<IResult> WithdrawalsAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCourierWithdrawalsQuery(id), ct)).Match(w => Results.Ok(w));

    /// <summary>
    /// Ramène une date reçue à de l'UTC, quel que soit le `Kind` que la liaison lui a
    /// donné. `Unspecified` est traité comme de l'UTC : c'est ce que le client envoie,
    /// et le supposer local décalerait la période sans le dire.
    /// </summary>
    private static DateTime? EnUtc(DateTime? valeur)
        => valeur is not { } d
            ? null
            : d.Kind switch
            {
                DateTimeKind.Utc => d,
                DateTimeKind.Local => d.ToUniversalTime(),
                _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
            };

    public sealed record RegisterCourierRequest(
        Guid UserId, string DisplayName, string Phone,
        string PayoutProvider, string PayoutAccountNumber,
        string? PayoutAccountName = null, string? ExternalRef = null);

    public sealed record SuspendCourierRequest(string Reason);

    public sealed record ChangePayoutRequest(string Provider, string AccountNumber, string? AccountName = null);

    public sealed record LinkExternalRefRequest(string ExternalRef);
}
