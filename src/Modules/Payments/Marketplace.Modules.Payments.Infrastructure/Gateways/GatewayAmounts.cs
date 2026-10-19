using System.Globalization;
using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways;

/// <summary>
/// Contrôles de devise et de format de montant communs aux passerelles PSP.
///
/// Ils vivaient dans HttpPaymentGatewayBase. KkiaPay ne fait aucun appel réseau et
/// n'hérite donc pas de cette base, alors qu'il encaisse en XOF avec un montant
/// entier : recopier les gardes aurait créé deux versions d'un contrôle d'argent.
/// </summary>
internal static class GatewayAmounts
{
    /// <summary>
    /// ══════════════════════════════════════════════════════════════════════════════
    /// ON N'ENCAISSE PAS DANS UNE DEVISE QU'ON NE SAIT PAS TRAITER.
    ///
    /// FedaPay, MTN MoMo et Moov envoyaient au prestataire la devise lue dans la
    /// CONFIGURATION (« Payments:*:Currency ») avec le montant lu dans la COMMANDE.
    /// Les deux ne se rencontraient nulle part. Une commande de 50,00 EUR partait donc
    /// comme une charge de 50 XOF : la valeur numérique de la commande, l'étiquette de
    /// la configuration. L'acheteur payait environ un trente-troisième du dû, et le
    /// paiement était ensuite marqué ENCAISSÉ pour le montant complet — c'est-à-dire
    /// une commande livrée contre presque rien, sans qu'aucun écran ne le signale.
    ///
    /// POURQUOI REFUSER PLUTÔT QUE CONVERTIR. Convertir supposerait un taux de change :
    /// lequel, à quelle date, avec quelle marge, et opposable à qui ? Aucune de ces
    /// questions n'a de réponse dans ce module. Une conversion inventée est une erreur
    /// silencieuse de plus, du même genre que celle qu'on corrige.
    ///
    /// POURQUOI REFUSER PLUTÔT QU'ACCEPTER TEL QUEL. Accepter en silence EST le défaut.
    /// Ces trois passerelles sont mono-devise par contrat (compte marchand ouvert dans
    /// une devise) : leur envoyer autre chose produit soit un rejet du prestataire,
    /// soit — et c'est le cas constaté — un encaissement faux.
    ///
    /// POURQUOI UNE EXCEPTION ET PAS UN Result. Le port <c>IPaymentGateway</c> renvoie
    /// une <c>GatewaySession</c>, pas un <c>Result</c> : l'exception est le seul refus
    /// que ce contrat sait exprimer, et c'est déjà celui qu'utilisent ces adaptateurs
    /// pour toute réponse inexploitable du prestataire. L'appelant n'en attrape aucune,
    /// donc l'initiation échoue en erreur serveur — bruyante, tracée, et surtout SANS
    /// paiement créé côté prestataire. C'est le comportement voulu : mieux vaut un
    /// refus visible qu'un encaissement faux réputé réussi.
    /// ══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static string EnsureCurrencyIsSupported(string provider, string? requestedCurrency, string? configuredCurrency)
    {
        var requested = (requestedCurrency ?? string.Empty).Trim().ToUpperInvariant();
        var configured = (configuredCurrency ?? string.Empty).Trim().ToUpperInvariant();

        if (requested.Length == 0)
        {
            throw new InvalidOperationException(
                $"{provider} : la commande ne porte aucune devise. Aucun encaissement n'est tenté.");
        }

        // Configuration vide : on ne DEVINE pas que le compte marchand est en XOF. Une
        // devise implicite est exactement ce qui a produit le défaut d'origine.
        if (configured.Length == 0)
        {
            throw new InvalidOperationException(
                $"{provider} : aucune devise configurée pour cette passerelle. " +
                $"Renseigne « Payments:{provider}:Currency » avant d'encaisser du {requested}.");
        }

        if (!string.Equals(requested, configured, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{provider} : la commande est libellée en {requested}, cette passerelle est configurée en {configured}. " +
                "L'encaissement est REFUSÉ — envoyer le montant de la commande sous l'étiquette de la devise " +
                "configurée ferait payer un montant sans rapport avec ce qui est dû.");
        }

        return requested;
    }

    /// <summary>
    /// Montant en unités ENTIÈRES, pour les prestataires dont le champ « amount » est un
    /// entier (FedaPay). Refuse une devise à décimales : la tronquer silencieusement est
    /// la seconde moitié du défaut — 8 999,10 envoyés comme 8 999, la commande restant
    /// enregistrée à 8 999,10 et marquée payée.
    ///
    /// Depuis que <see cref="Money"/> arrondit selon la devise, un montant issu du
    /// domaine tombe déjà sur la grille : ce garde-fou n'a plus rien à tronquer, il
    /// vérifie seulement que personne ne l'a contourné.
    /// </summary>
    public static long ToWholeUnits(string provider, decimal amount, string currency)
    {
        if (!Money.IsZeroDecimal(currency))
        {
            throw new InvalidOperationException(
                $"{provider} : la devise {currency} admet des décimales, or le prestataire n'accepte qu'un montant entier. " +
                "L'encaissement est REFUSÉ plutôt que tronqué.");
        }

        return (long)Money.Round(amount, currency);
    }

    /// <summary>
    /// Montant formaté avec le nombre de décimales de SA devise, pour les prestataires
    /// dont le champ « amount » est une chaîne (MTN MoMo, Moov) : « 5000 » en XOF,
    /// « 50.00 » en EUR. C'était <c>(long)Math.Round(amount)</c>, donc une troncature à
    /// l'entier appliquée à toute devise, centimes compris.
    /// </summary>
    public static string FormatAmountForCurrency(decimal amount, string currency)
        => Money.Round(amount, currency)
            .ToString("F" + Money.DecimalsFor(currency), CultureInfo.InvariantCulture);
}
