namespace Marketplace.Modules.Delivery.Contracts;

/// <summary>
/// UN COURSIER DANS UN MENU DÉROULANT — et rien de plus.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// POURQUOI UNE SECONDE VUE PLUTÔT QUE `CourierSummary`.
///
/// `CourierSummary` porte le COMPTE MOBILE MONEY. C'est légitime — il existe pour dire
/// où verser l'argent d'un coursier — mais remplir un sélecteur avec cette vue envoie
/// dans le navigateur le numéro de versement et le nom de compte de TOUTE la flotte,
/// pour n'afficher qu'un nom et un téléphone. La console ne s'en sert pas, personne ne
/// le voit, et cela part quand même à chaque ouverture de l'écran.
///
/// La règle est simple et vaut au-delà de ce cas : une liste sert ce qu'elle affiche.
/// Le compte de versement se demande coursier par coursier, quand on a une raison de le
/// regarder.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record CourierPickerView(Guid Id, string DisplayName, string Phone, string Status);

/// <summary>
/// La liste du sélecteur, ET COMBIEN IL Y EN A EN TOUT.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE TOTAL N'EST PAS UN ORNEMENT : SANS LUI, LA TRONCATURE EST INDÉTECTABLE.
///
/// La liste est bornée (voir la constante « Plafond » du gestionnaire, module Delivery).
/// Un écran qui reçoit cinq cents lignes ne peut pas savoir s'il en manque zéro ou deux
/// mille : il affiche ce qu'il a, l'administrateur cherche un livreur, ne le trouve pas,
/// et lit « Aucun résultat » — c'est-à-dire « ce livreur n'existe pas », quand la vérité
/// est « je ne vous l'ai pas envoyé ».
///
/// Ce total ne coûte rien : le dépôt charge déjà toute la flotte avant de la borner. Le
/// compter était une ligne ; l'écran peut enfin DIRE ce qui manque.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Items">Les coursiers rendus, dans la limite du plafond.</param>
/// <param name="Total">Le nombre de coursiers existants, borne comprise ou non.</param>
public sealed record CourierPickerList(IReadOnlyList<CourierPickerView> Items, int Total);

/// <summary>Le compte Mobile Money d'un coursier, vu de l'extérieur du module.</summary>
public sealed record CourierPayoutSummary(string Provider, string AccountNumber, string AccountName);

/// <summary>
/// Vue publique d'un coursier. Volontairement pauvre : elle porte de quoi le payer et
/// de quoi décider s'il a le droit de l'être, rien de plus. Sa position, sa zone et sa
/// disponibilité appartiennent au service coursier.
/// </summary>
public sealed record CourierSummary(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string Phone,
    string Status,
    bool CanBePaid,
    string? ExternalRef,
    CourierPayoutSummary? Payout,
    DateTime CreatedAtUtc);
