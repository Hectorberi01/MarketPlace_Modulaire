using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace HbaExpress.Dispatch.Api.Extensions;

/// <summary>
/// Traduit un conflit de verrou optimiste en <b>409 Conflict</b>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST LA MOITIÉ MANQUANTE DU VERROU SUR L'ACCEPTATION D'UNE COURSE.
///
/// Deux coursiers acceptent la même course au même instant. Les deux passent la règle
/// métier en mémoire — chacun a bien une offre vivante. C'est PostgreSQL qui tranche,
/// via le jeton `xmin` : le premier UPDATE gagne, le second touche zéro ligne et EF lève
/// DbUpdateConcurrencyException.
///
/// Sans cette traduction, le perdant reçoit un 500. Or il ne s'est rien passé de grave,
/// et surtout le message est FAUX : son application afficherait une panne du service là
/// où un autre coursier a simplement été plus rapide. Il rappellerait, verrait encore une
/// erreur, et finirait par croire l'application cassée.
///
/// Le 409 dit la vérité — « quelqu'un l'a prise » — et c'est exactement ce que
/// DeliveryJob.Accept répond déjà quand la course est perdue AVANT d'atteindre la base
/// (job.already_taken). Les deux chemins convergent donc vers le même code HTTP, ce qui
/// laisse une seule branche à écrire côté application mobile.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ConcurrencyExceptionHandler> _logger;

    public ConcurrencyExceptionHandler(ILogger<ConcurrencyExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException concurrency)
        {
            return false;
        }

        // AVERTISSEMENT, PAS ERREUR : le verrou vient de faire son travail. Une
        // fréquence anormale ici signalerait en revanche une vraie contention.
        _logger.LogWarning(
            concurrency,
            "Conflit de concurrence sur {Path} : une écriture simultanée a été rejetée.",
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                title = "job.already_taken",
                status = StatusCodes.Status409Conflict,
                detail = "Cette course vient d'être prise par un autre coursier."
            },
            cancellationToken);

        return true;
    }
}
