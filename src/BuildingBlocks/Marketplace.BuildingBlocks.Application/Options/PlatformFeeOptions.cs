namespace Marketplace.BuildingBlocks.Application.Options;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE FRAIS DE SERVICE DE LA PLATEFORME, PRÉLEVÉ SUR LA COMMANDE.
///
/// La plateforme se rémunérait en majorant le prix de CHAQUE article de 15 %
/// (10 % de commission + 5 % de frais provider), majoration invisible pour l'acheteur
/// et fondue dans l'étiquette. Elle prélève désormais un frais UNIQUE, annoncé, de
/// 2 % du total des articles.
///
/// POURQUOI CE TYPE VIT DANS LES BLOCS COMMUNS. Deux modules ont besoin du même taux,
/// et pour des raisons différentes :
///
///   • Cart s'en sert pour ANNONCER le montant dans le panier, avant la commande ;
///   • Ordering s'en sert pour le FIGER sur la commande, où il fait foi.
///
/// Le loger dans l'un des deux aurait forcé l'autre à en dépendre — Ordering dépendant
/// de Cart, ou l'inverse — pour une simple valeur de configuration. Les deux lisent
/// donc la même section, et le seul chiffre qui compte reste celui gelé sur la
/// commande.
///
/// LE TAUX EST FIGÉ SUR LA COMMANDE, JAMAIS RELU. C'est la même règle que pour la TVA
/// et pour le barème des offres : passer le frais de 2 % à 3 % ne doit pas réécrire
/// rétroactivement ce qu'ont payé les commandes d'hier, sous peine qu'une facture
/// réimprimée ne corresponde plus à celle remise au client.
///
/// L'ASSIETTE EST LE TOTAL DES ARTICLES, REMISES DÉDUITES — pas les frais de livraison.
/// Facturer un pourcentage sur le transport reviendrait à prélever une commission sur
/// une prestation que la plateforme refacture déjà à l'euro près ; et l'acheteur, lui,
/// comprend « 2 % de mon panier », pas « 2 % de mon panier plus la course ».
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PlatformFeeOptions
{
    /// <summary>Frais de service (fraction, ex. 0.02 = 2 %). Zéro désactive le prélèvement.</summary>
    public decimal PlatformFeeRate { get; set; } = 0.02m;

    /// <summary>
    /// Construit les options depuis la valeur BRUTE lue en configuration.
    ///
    /// Ce type vit dans les blocs communs, qui ne référencent pas
    /// `Microsoft.Extensions.Configuration` — et n'ont aucune raison de le faire pour
    /// un nombre. Chaque installeur lit donc sa section et passe la chaîne ici, de
    /// sorte que l'ANALYSE, elle, n'existe qu'à un seul endroit : culture invariante
    /// (« 0.02 » et non « 0,02 »), et refus des valeurs négatives.
    ///
    /// Une valeur absente ou illisible laisse le défaut en place plutôt que de mettre
    /// le taux à zéro : un frais qui disparaît en silence sur une faute de frappe
    /// serait pire qu'un frais resté à sa valeur par défaut.
    /// </summary>
    public static PlatformFeeOptions Depuis(string? tauxBrut)
    {
        var options = new PlatformFeeOptions();

        if (decimal.TryParse(
                tauxBrut,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var taux)
            && taux >= 0m)
        {
            options.PlatformFeeRate = taux;
        }

        return options;
    }
}
