using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Traduit une violation de CONTRAINTE D'UNICITÉ PostgreSQL (SQLSTATE 23505) en
/// <b>409 Conflict</b>.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// TROIS 500 QUI N'AVAIENT AUCUNE RAISON D'EN ÊTRE.
///
/// Le dépôt ne rattrapait NULLE PART une violation d'unicité : aucune occurrence de
/// `DbUpdateException`, `PostgresException` ni `23505` dans tout le code. Le seul
/// gestionnaire enregistré traitait la concurrence. Trois cas parfaitement ordinaires
/// remontaient donc en 500 :
///
///   • Un vendeur saisit un SKU déjà pris par un AUTRE vendeur. Le domaine ne vérifie
///     l'unicité qu'au sein du produit ; la base l'impose globalement. La variante
///     n'est pas créée, le produit reste invendable, et le message ne dit pas quoi
///     corriger.
///   • Deux créations simultanées du même produit — double-tap sur l'application,
///     rejeu sur un réseau instable : `SlugExistsAsync` puis `INSERT`, sans verrou.
///     L'une des deux part en 500, APRÈS que les images ont été téléversées.
///   • Même schéma sur la création d'offre et d'article de stock.
///
/// UN 500 EST UN MENSONGE ICI. Il dit « nous sommes cassés » et invite à réessayer à
/// l'identique, ce qui échouera pareillement. Le 409 dit ce qui s'est passé : la
/// valeur est déjà prise, changez-la.
///
/// CE HANDLER NE DIT PAS QUELLE VALEUR EST PRISE, NI PAR QUI.
///
/// Le message d'erreur natif de PostgreSQL contient la ligne en conflit. Le renvoyer
/// ferait de cette route un oracle d'énumération : on découvrirait les noms de
/// produits et les SKU des concurrents en tâtonnant. On nomme donc le CHAMP concerné
/// — ce que l'appelant a besoin de savoir pour corriger — et rien de plus. Le détail
/// va dans les journaux.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UniqueViolationExceptionHandler : IExceptionHandler
{
    /// <summary>SQLSTATE PostgreSQL : unique_violation.</summary>
    private const string UniqueViolation = "23505";

    private readonly ILogger<UniqueViolationExceptionHandler> _logger;

    public UniqueViolationExceptionHandler(ILogger<UniqueViolationExceptionHandler> logger)
        => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // L'exception utile est TOUJOURS enveloppée : EF lève `DbUpdateException` et
        // met la `PostgresException` en cause interne. La chercher directement à la
        // racine ne la trouverait jamais.
        var postgres = FindPostgresException(exception);
        if (postgres is null || postgres.SqlState != UniqueViolation)
        {
            return false;
        }

        var (code, detail) = Describe(postgres.ConstraintName);

        // Warning et non Error : la base vient de faire son travail. Mais on trace le
        // nom de la contrainte, sans quoi une violation sur une contrainte non
        // reconnue ci-dessous serait indiagnosticable depuis le message générique.
        _logger.LogWarning(
            postgres,
            "Violation d'unicité sur {Path} (contrainte {Constraint}) traduite en 409.",
            httpContext.Request.Path,
            postgres.ConstraintName ?? "inconnue");

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                title = code,
                status = StatusCodes.Status409Conflict,
                detail
            },
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Remonte la chaîne des causes. EF enveloppe systématiquement, et une couche
    /// supplémentaire (intercepteur, résilience) peut s'ajouter : on ne suppose pas
    /// une profondeur donnée.
    /// </summary>
    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    /// <summary>
    /// Traduit le nom de la contrainte en message utile. Les noms viennent des
    /// `HasIndex(...).IsUnique()` des configurations EF, préfixés `ix_`.
    ///
    /// La correspondance est faite sur un FRAGMENT du nom, pas sur l'égalité : les
    /// noms d'index sont générés et changent au gré des migrations, et un message
    /// dégradé vaut mieux qu'un handler qui cesse silencieusement de reconnaître ce
    /// qu'il traitait hier.
    /// </summary>
    private static (string Code, string Detail) Describe(string? constraintName)
    {
        var name = constraintName?.ToLowerInvariant() ?? string.Empty;

        if (name.Contains("sku"))
        {
            return ("catalog.variant.sku_taken",
                "Cette référence (SKU) est déjà utilisée. Les SKU sont uniques sur toute la place : choisissez-en un autre.");
        }

        if (name.Contains("slug"))
        {
            return ("catalog.product.slug_taken",
                "Un produit portant ce nom existe déjà. Choisissez un nom légèrement différent.");
        }

        if (name.Contains("shop_name") || name.Contains("shopname"))
        {
            return ("sellers.seller.shop_name_taken",
                "Ce nom de boutique est déjà pris.");
        }

        if (name.Contains("email"))
        {
            return ("identity.user.email_taken",
                "Cette adresse e-mail est déjà utilisée.");
        }

        return ("persistence.unique_violation",
            "Une de ces valeurs doit être unique et est déjà utilisée. Modifiez-la et réessayez.");
    }
}

public static class UniqueViolationExceptionHandlerExtensions
{
    /// <summary>
    /// À enregistrer AVANT le gestionnaire par défaut, comme
    /// <see cref="ConcurrencyExceptionHandler"/> : les IExceptionHandler sont essayés
    /// dans l'ordre d'enregistrement et le premier qui renvoie `true` gagne. L'ordre
    /// entre ces deux-là est indifférent — ils ne reconnaissent pas les mêmes
    /// exceptions.
    /// </summary>
    public static IServiceCollection AddUniqueViolationExceptionHandler(this IServiceCollection services)
        => services.AddExceptionHandler<UniqueViolationExceptionHandler>();
}
