namespace Marketplace.Modules.Settlement.Application.Abstractions;

/// <summary>
/// Le plafond de ce qu'une course peut coûter à la plateforme.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SEUL CONTRÔLE ENTRE L'APPELANT DU WEBHOOK ET LE PORTEFEUILLE DE LA PLATEFORME.
///
/// Le montant d'une course est calculé par le service de dispatch, pas ici — lui seul
/// connaît la distance et les points de ramassage. Conséquence directe : qui appelle
/// « course livrée » décide combien sort. La signature HMAC dit QUI appelle ; elle ne dit
/// rien de COMBIEN.
///
/// Et une erreur de virgule dans le service, une variable de configuration mal typée, ou
/// un secret compromis produisaient jusqu'ici une course à 900 000 F sans que rien ne
/// s'y oppose. L'argent d'un Mobile Money ne se reprend pas.
///
/// DEUX PLAFONDS, ET LE RELATIF EST LE PLUS IMPORTANT :
///
///  • RELATIF — une course ne peut pas coûter plus que les frais de livraison encaissés
///    sur la commande. C'est un invariant, pas un réglage : la plateforme ne verse jamais
///    pour une livraison plus qu'elle n'a facturé pour cette livraison. Il s'adapte tout
///    seul à chaque commande, là où un montant fixe vieillit.
///  • ABSOLU — un maximum configuré, filet contre une commande dont les frais de
///    livraison seraient eux-mêmes aberrants. Facultatif : sans lui, le relatif
///    s'applique seul, et il suffit déjà.
///
/// Le jour où une course pourra légitimement coûter plus que les frais encaissés — une
/// subvention de lancement, par exemple — ce sera une décision explicite à écrire ici,
/// pas un contrôle à retirer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="MaxAmount">
/// Plafond absolu par course, ou <c>null</c> pour n'appliquer que le plafond relatif.
/// Lu depuis <c>Settlement:CourierPay:MaxAmount</c>.
/// </param>
public sealed record CourierPayLimits(decimal? MaxAmount)
{
    /// <summary>Emplacement du plafond absolu dans la configuration.</summary>
    public const string MaxAmountKey = "Settlement:CourierPay:MaxAmount";

    /// <summary>Aucun plafond absolu : seul le plafond relatif s'applique.</summary>
    /// <remarks>
    /// Le paramètre est TYPÉ EXPLICITEMENT, et il le faut : tout record reçoit du
    /// compilateur un constructeur de copie <c>CourierPayLimits(CourierPayLimits)</c>, et
    /// un <c>null</c> nu conviendrait aussi bien à celui-ci qu'au constructeur principal.
    /// </remarks>
    public static CourierPayLimits RelativeOnly => new((decimal?)null);
}
