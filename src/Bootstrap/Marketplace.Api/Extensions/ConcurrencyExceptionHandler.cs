using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Traduit un conflit de concurrence (verrou optimiste `xmin`) en <b>409 Conflict</b>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE HANDLER, LE VERROU OPTIMISTE SERAIT PIRE QUE L'ABSENCE DE VERROU.
///
/// Les agrégats financiers (Wallet, Payment, Order,
/// InventoryItem) portent désormais un jeton de concurrence. Deux écritures
/// simultanées : la première gagne, la seconde lève `DbUpdateConcurrencyException`.
///
/// Sans traduction, cette exception remonte au gestionnaire par défaut → <b>500</b>.
/// Un 500 est un message qui dit « nous sommes cassés ». Le client abandonne, ou pire,
/// signale un incident. Alors qu'il ne s'est rien passé de grave : deux requêtes se
/// sont croisées, et l'une doit simplement être rejouée.
///
/// Le 409 dit la vérité : « conflit, réessayez ». C'est actionnable — pour un humain
/// comme pour un PSP, qui rejoue ses webhooks automatiquement sur un code non-2xx.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// CE HANDLER NE REJOUE RIEN, ET C'EST VOULU.
///
/// Un retry automatique côté serveur serait un piège : `ModuleDbContext` dispatche les
/// événements de domaine et draine l'outbox <b>avant</b> `base.SaveChangesAsync`.
/// Rejouer la commande dans le même scope re-dispatcherait ces événements et
/// dupliquerait les messages d'outbox — on corrigerait un bug d'argent en en créant un
/// autre. Le rejeu doit venir d'une requête neuve, avec un scope neuf.
/// </para>
public sealed class ConcurrencyExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ConcurrencyExceptionHandler> _logger;

    public ConcurrencyExceptionHandler(ILogger<ConcurrencyExceptionHandler> logger)
        => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException concurrency)
        {
            // Pas notre affaire : on laisse la main au gestionnaire suivant.
            return false;
        }

        // En Warning, pas en Error : ce n'est PAS une panne. C'est le verrou qui fait son
        // travail — il vient d'empêcher un double débit. En revanche, une fréquence
        // anormale ici signale une vraie contention à investiguer.
        _logger.LogWarning(
            concurrency,
            "Conflit de concurrence sur {Path} : une écriture simultanée a été rejetée (le verrou optimiste a joué son rôle).",
            httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                title = "concurrency.conflict",
                status = StatusCodes.Status409Conflict,
                detail = "Une autre opération a modifié ces données au même instant. "
                       + "Rien n'a été enregistré. Réessayez.",
            },
            cancellationToken);

        return true;
    }
}

public static class ConcurrencyExceptionHandlerExtensions
{
    /// <summary>
    /// À enregistrer AVANT `AddProblemDetails()` / le gestionnaire par défaut : les
    /// IExceptionHandler sont essayés dans l'ordre d'enregistrement, et le premier qui
    /// renvoie `true` gagne.
    /// </summary>
    public static IServiceCollection AddConcurrencyExceptionHandler(this IServiceCollection services)
        => services.AddExceptionHandler<ConcurrencyExceptionHandler>();
}
