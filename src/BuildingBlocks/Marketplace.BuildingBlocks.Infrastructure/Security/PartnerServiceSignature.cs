using System.Security.Cryptography;
using System.Text;

namespace Marketplace.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Vérifie la signature d'un appel entrant venu d'un SERVICE PARTENAIRE — un système
/// qui n'est ni un utilisateur, ni un PSP.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE VÉRIFICATEUR EXISTE À CÔTÉ DE CELUI DES WEBHOOKS PSP.
///
/// La plateforme savait authentifier deux sortes d'appelants : un UTILISATEUR par son
/// jeton, et un PRESTATAIRE DE PAIEMENT par la signature de son webhook. Le service
/// coursier n'est ni l'un ni l'autre : il n'a pas de compte, et son secret n'a rien à
/// faire dans la configuration des passerelles de paiement.
///
/// Le mécanisme est le même — HMAC-SHA256 du corps brut, comparaison en temps constant,
/// FERMÉ PAR DÉFAUT — mais le secret est distinct, et c'est le point : un secret par
/// interlocuteur, pour qu'une fuite ne compromette qu'un seul canal.
///
/// FERMÉ PAR DÉFAUT, ET IL FAUT INSISTER. Le seul appel que cet endpoint autorise fait
/// SORTIR de l'argent du portefeuille de la plateforme vers celui d'un coursier.
/// Accepter un appel non signé parce qu'aucun secret n'est configuré reviendrait à
/// laisser n'importe qui se payer. En production, secret absent = appel rejeté.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PartnerServiceSignature
{
    /// <summary>
    /// Autorise un appel NON signé lorsqu'aucun secret n'est configuré. Faux par défaut ;
    /// à positionner à vrai uniquement en environnement de développement.
    /// </summary>
    public static bool AllowUnsignedWhenSecretMissing { get; set; }

    /// <summary>Signature HMAC-SHA256 du corps brut, en hexadécimal minuscule.</summary>
    public static string Compute(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
    }

    /// <summary>
    /// L'appel est-il authentique ? Comparaison en TEMPS CONSTANT : une comparaison de
    /// chaînes ordinaire s'arrête au premier caractère différent, ce qui laisse deviner
    /// la signature octet par octet en mesurant le temps de réponse.
    /// </summary>
    public static bool Verify(string rawBody, string? signatureHeader, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return AllowUnsignedWhenSecretMissing;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var expected = Compute(rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader.Trim()));
    }
}
