using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Disputes.Contracts;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Disputes : ouverture d'un litige par une partie,
/// fil de messages, et arbitrage par la plateforme (examen, décision, escalade).
/// </summary>
public static class DisputesEndpoints
{
    /// <summary>Enregistre les routes de litige.</summary>
    public static IEndpointRouteBuilder MapDisputesEndpoints(this IEndpointRouteBuilder app)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // UN LITIGE CONTIENT DES PREUVES : PHOTOS DE COLIS, FACTURES, PARFOIS PIÈCES
        // D'IDENTITÉ.
        //
        // Le groupe était nu, et `GET /{id}` renvoyait le FIL COMPLET — messages,
        // PhotoUrl, auteurs, montant d'arbitrage — à n'importe quel compte
        // authentifié. Le BFF Mobile faisait déjà le bon contrôle, commentaire à
        // l'appui ; cet hôte-ci ne l'avait jamais reçu.
        //
        // La correction est celle du BFF Mobile, mot pour mot : on ne lit et on
        // n'écrit que dans SON litige. Voir GuardOwnDisputeAsync.
        // ═════════════════════════════════════════════════════════════════════════
        var mine = app.MapAuthenticatedGroup("/api/disputes").WithTags("Disputes");

        mine.MapPost("/", OpenAsync).WithName("OpenDispute");
        mine.MapGet("/mine", ListMineAsync).WithName("ListMyDisputes");
        mine.MapGet("/{id:guid}", GetAsync).WithName("GetDispute");
        mine.MapPost("/{id:guid}/messages", AddMessageAsync).WithName("AddDisputeMessage");

        // Back-office : arbitrage, et la vue « tous les litiges d'une commande » — qui
        // n'a aucun usage légitime pour une partie, et servait surtout à lire ceux des
        // autres. Le rôle vient du groupe : plus besoin de le répéter par route, et
        // l'oublier sur une route future ne créera plus de faille.
        var backOffice = app.MapAdminGroup("/api/disputes").WithTags("Disputes");

        backOffice.MapGet("/order/{orderId:guid}", ListByOrderAsync).WithName("ListDisputesByOrder");
        backOffice.MapPost("/{id:guid}/review", ReviewAsync).WithName("StartDisputeReview");
        backOffice.MapPost("/{id:guid}/resolve", ResolveAsync).WithName("ResolveDispute");
        backOffice.MapPost("/{id:guid}/escalate", EscalateAsync).WithName("EscalateDispute");

        return app;
    }

    /// <summary>Ouvre un litige sur une commande.</summary>
    private static async Task<IResult> OpenAsync(OpenBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new OpenDisputeCommand(userId, body.OrderId, body.Type, body.Message, body.PhotoUrl), ct);
        return result.Match(id => Results.Created($"/api/disputes/{id}", new { id }));
    }

    /// <summary>Liste les litiges de l'utilisateur connecté.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyDisputesQuery(userId), ct);
        return result.Match(items => Results.Ok(items));
    }

    /// <summary>Liste les litiges d'une commande.</summary>
    private static async Task<IResult> ListByOrderAsync(Guid orderId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListDisputesByOrderQuery(orderId), ct)).Match(items => Results.Ok(items));

    /// <summary>Récupère un litige et son fil de messages.</summary>
    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal, Guid id, ISender sender, CancellationToken ct)
    {
        var guard = await GuardOwnDisputeAsync(principal, id, sender, ct);
        return guard.Failure ?? Results.Ok(guard.Dispute!);
    }

    /// <summary>Ajoute un message (et preuve) au litige.</summary>
    private static async Task<IResult> AddMessageAsync(
        Guid id, MessageBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        // Le userId du jeton était bien transmis — mais transmis n'est pas vérifié :
        // Dispute.AddMessage ne contrôle que le STATUT du litige, jamais l'appartenance.
        // On pouvait donc injecter messages et photos dans le litige d'un inconnu, avant
        // arbitrage.
        var guard = await GuardOwnDisputeAsync(principal, id, sender, ct);
        if (guard.Failure is not null)
        {
            return guard.Failure;
        }

        var userId = CurrentUserId(principal)!.Value;
        var result = await sender.Send(new AddDisputeMessageCommand(id, userId, body.Body, body.PhotoUrl), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Le litige demandé appartient-il à l'appelant ?
    ///
    /// UN 404 DANS TOUS LES CAS DE REFUS, jamais un 403. Répondre « interdit » sur un
    /// litige existant et « introuvable » sur un autre laisserait énumérer les litiges de
    /// la plateforme un identifiant à la fois. C'est la règle déjà tenue par le BFF
    /// Mobile, et par les lectures de commande et de paiement.
    ///
    /// Un administrateur passe : c'est lui qui arbitre.
    /// </summary>
    private static async Task<(IResult? Failure, DisputeSummary? Dispute)> GuardOwnDisputeAsync(
        ClaimsPrincipal principal, Guid id, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return (Results.Unauthorized(), null);
        }

        var result = await sender.Send(new GetDisputeQuery(id), ct);
        if (result.IsFailure)
        {
            return (Results.NotFound(new { error = "dispute_not_found" }), null);
        }

        if (!principal.IsInRole("Admin") && !principal.IsInRole("Moderator")
            && result.Value.RaisedBy != userId)
        {
            return (Results.NotFound(new { error = "dispute_not_found" }), null);
        }

        return (null, result.Value);
    }

    /// <summary>Passe le litige en examen.</summary>
    private static async Task<IResult> ReviewAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new StartDisputeReviewCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Tranche le litige.</summary>
    private static async Task<IResult> ResolveAsync(Guid id, ResolveBody body, ISender sender, CancellationToken ct)
        => (await sender.Send(new ResolveDisputeCommand(id, body.Resolution), ct)).Match(() => Results.NoContent());

    /// <summary>Escalade le litige.</summary>
    private static async Task<IResult> EscalateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new EscalateDisputeCommand(id), ct)).Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record OpenBody(Guid OrderId, string Type, string Message, string? PhotoUrl);
    public sealed record MessageBody(string Body, string? PhotoUrl);
    public sealed record ResolveBody(string Resolution);
}
