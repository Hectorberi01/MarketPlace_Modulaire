namespace Marketplace.Modules.Platform.Application.Abstractions;

/// <summary>
/// Chiffrement des secrets des fournisseurs.
///
/// LA CLÉ MAÎTRE NE VIT PAS EN BASE, et c'est tout l'intérêt. Une sauvegarde Postgres
/// qui fuite ne livre alors que des valeurs opaques. La clé vient du vault, par
/// variable d'environnement, comme la clé de signature JWT — même niveau d'exigence.
///
/// L'IMPLÉMENTATION REFUSE DE DÉMARRER EN PRODUCTION SANS CLÉ. Une alternative
/// séduisante — « pas de clé, on stocke en clair » — ferait qu'un déploiement ayant
/// oublié la variable écrirait les clés d'API des PSP en texte dans la base, sans
/// erreur et sans symptôme. C'est exactement la forme de panne que la garde de
/// Payments existe pour empêcher ailleurs.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Chiffre une valeur. Le résultat est auto-descriptif (version + nonce + tag).</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Déchiffre. Lève si la valeur a été altérée ou si la clé a changé : une
    /// authentification qui échoue n'est pas un détail à avaler, elle signifie soit
    /// une base modifiée, soit la mauvaise clé maître.
    /// </summary>
    string Unprotect(string protectedValue);

    /// <summary>Quatre premiers caractères de la valeur en clair, pour l'écran d'administration.</summary>
    string Preview(string protectedValue);
}
