using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.Host.Extensions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// Une lettre morte, telle qu'affichée à l'administrateur.
///
/// Le CONTENU du message est volontairement absent. Certains événements portent des
/// secrets — <c>PasswordResetRequestedIntegrationEvent</c> transporte le jeton de
/// réinitialisation EN CLAIR. L'exposer dans une console d'administration recréerait, en
/// plus discret, la fuite qu'on vient de fermer. Le type et l'erreur suffisent à
/// diagnostiquer.
/// </summary>
public sealed record DeadLetterView(
    string Module,
    Guid Id,
    string Type,
    DateTime OccurredOnUtc,
    DateTime? DeadLetteredOnUtc,
    int AttemptCount,
    string? Error);

/// <summary>
/// BFF Admin — LES LETTRES MORTES DE L'OUTBOX.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CETTE FILE DOIT ÊTRE REGARDÉE.
///
/// Une lettre morte n'est pas un incident technique : c'est un FAIT MÉTIER QUI NE SE
/// PRODUIRA JAMAIS. Un e-mail de réinitialisation qui ne partira pas — donc un
/// utilisateur enfermé dehors. Un gain vendeur qui ne sera pas crédité — donc de
/// l'argent dû qui n'existe nulle part. Un stock qui ne sera pas libéré — donc un
/// article invendable.
///
/// Avant, ces messages étaient rejoués toutes les 5 secondes, pour l'éternité. C'était
/// intenable — mais au moins, ça finissait par se voir. Un plafond de tentatives sans
/// cette file les aurait enterrés EN SILENCE : on aurait remplacé une boucle bruyante
/// par une perte invisible. Ce serait pire.
///
/// Cette liste doit rester VIDE. Chaque ligne est une promesse non tenue.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// L'outbox est LOCALE À CHAQUE MODULE (une table par schéma, 25 schémas). Ces endpoints
/// balaient donc les 25 DbContext via <see cref="OutboxContextRegistration"/> — le registre
/// peuplé par <c>AddOutboxProcessor</c> sur TOUS les hôtes, y compris ceux qui, comme le BFF
/// Admin, ne drainent pas l'outbox (<c>OUTBOX_ENABLED=false</c>).
/// </para>
/// </summary>
public static class AdminOutboxEndpoints
{
    public static IEndpointRouteBuilder MapAdminOutboxEndpoints(this IEndpointRouteBuilder app)
    {
        // ─────────────────────────────────────────────────────────────────────────
        // LES DEUX ARGUMENTS SONT NÉCESSAIRES, ET PAS SEULEMENT POUR L'ÉTIQUETTE.
        //
        // `MapAdminGroup("/api/admin/outbox")` — un seul argument — ne visait pas la
        // surcharge de ce BFF (qui exige le rôle Admin) mais celle de
        // `ApiAuthorization`, qui accorde Admin OU Moderator. C'était le seul appel
        // de ce type sur vingt-cinq groupes.
        //
        // Un modérateur pouvait donc abandonner définitivement une lettre morte,
        // c'est-à-dire décider qu'un vendeur ne serait jamais crédité.
        // ─────────────────────────────────────────────────────────────────────────
        var outbox = app.MapAdminGroup("/api/admin/outbox", "Admin · Outbox");

        outbox.MapGet("/dead-letters", ListDeadLettersAsync);
        outbox.MapPost("/dead-letters/{module}/{id:guid}/replay", ReplayAsync);
        outbox.MapPost("/dead-letters/{module}/{id:guid}/discard", DiscardAsync);

        return app;
    }

    /// <summary>Toutes les lettres mortes, tous modules confondus, la plus ancienne d'abord.</summary>
    private static async Task<IResult> ListDeadLettersAsync(
        [FromServices] IServiceProvider services,
        [FromServices] IEnumerable<OutboxContextRegistration> registrations,
        CancellationToken ct)
    {
        var items = new List<DeadLetterView>();

        // Séquentiel, et non parallèle : chaque DbContext est « scoped » et n'est pas
        // thread-safe. Les paralléliser ferait sauter EF de façon parfaitement obscure.
        foreach (var registration in registrations.OrderBy(r => r.ModuleName))
        {
            var dbContext = (DbContext)services.GetRequiredService(registration.DbContextType);

            var deadLetters = await dbContext.Set<OutboxMessage>()
                .AsNoTracking()
                .Where(m => m.DeadLetteredOnUtc != null)
                .OrderBy(m => m.DeadLetteredOnUtc)
                .Take(200)
                .Select(m => new DeadLetterView(
                    registration.ModuleName,
                    m.Id,
                    m.Type,
                    m.OccurredOnUtc,
                    m.DeadLetteredOnUtc,
                    m.AttemptCount,
                    m.Error))
                .ToListAsync(ct);

            items.AddRange(deadLetters);
        }

        // La plus ANCIENNE d'abord : c'est celle qui traîne depuis le plus longtemps, donc
        // la promesse la plus anciennement rompue.
        return Results.Ok(new
        {
            count = items.Count,
            items = items.OrderBy(i => i.DeadLetteredOnUtc).ToList(),
        });
    }

    /// <summary>
    /// Remet une lettre morte dans la file, après correction de la cause.
    ///
    /// REJOUER SANS AVOIR CORRIGÉ LA CAUSE NE SERT À RIEN : le message ré-échouera
    /// MaxAttempts fois, puis retombera en lettre morte. Le compteur repart de zéro, ce qui
    /// est voulu — c'est une nouvelle chance, pas une rallonge.
    /// </summary>
    private static async Task<IResult> ReplayAsync(
        string module,
        Guid id,
        [FromServices] IServiceProvider services,
        [FromServices] IEnumerable<OutboxContextRegistration> registrations,
        CancellationToken ct)
        => await MutateAsync(module, id, services, registrations, ct, message =>
        {
            message.DeadLetteredOnUtc = null;
            message.AttemptCount = 0;
            message.NextAttemptAtUtc = null;   // éligible immédiatement
            message.Error = null;
        }, "replayed");

    /// <summary>
    /// Abandonne définitivement une lettre morte : elle est marquée traitée, sans l'avoir été.
    ///
    /// C'EST UNE DÉCISION MÉTIER, PAS UNE OPÉRATION DE MÉNAGE. On déclare ici qu'un fait
    /// ne sera jamais propagé : un vendeur ne sera pas crédité, un client ne recevra pas son
    /// e-mail. À n'utiliser que pour un événement devenu SANS OBJET (commande annulée depuis,
    /// utilisateur supprimé). En cas de doute, rejouer — jamais enterrer.
    /// </summary>
    private static async Task<IResult> DiscardAsync(
        string module,
        Guid id,
        [FromServices] IServiceProvider services,
        [FromServices] IEnumerable<OutboxContextRegistration> registrations,
        CancellationToken ct)
        => await MutateAsync(module, id, services, registrations, ct, message =>
        {
            message.ProcessedOnUtc = DateTime.UtcNow;
            message.Error = (message.Error ?? string.Empty) + " [ABANDONNÉ MANUELLEMENT PAR UN ADMINISTRATEUR]";
        }, "discarded");

    private static async Task<IResult> MutateAsync(
        string module,
        Guid id,
        IServiceProvider services,
        IEnumerable<OutboxContextRegistration> registrations,
        CancellationToken ct,
        Action<OutboxMessage> mutate,
        string action)
    {
        var registration = registrations.FirstOrDefault(
            r => string.Equals(r.ModuleName, module, StringComparison.OrdinalIgnoreCase));

        if (registration is null)
        {
            return Results.NotFound(new { error = "outbox.module_unknown", module });
        }

        var dbContext = (DbContext)services.GetRequiredService(registration.DbContextType);

        // On n'agit QUE sur une lettre morte. Sans ce filtre, un administrateur pourrait
        // remettre à zéro le compteur d'un message encore en cours de temporisation — et,
        // à force de clics, reconstituer la boucle infinie qu'on vient de fermer.
        var message = await dbContext.Set<OutboxMessage>()
            .FirstOrDefaultAsync(m => m.Id == id && m.DeadLetteredOnUtc != null, ct);

        if (message is null)
        {
            return Results.NotFound(new { error = "outbox.dead_letter_not_found", module, id });
        }

        mutate(message);
        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(new { module, id, action });
    }
}
