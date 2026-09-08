namespace Marketplace.Modules.Identity.Contracts;

/// <summary>
/// Le compte Mobile Money sur lequel verser de l'argent À un utilisateur.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN MOYEN DE PAIEMENT LU À L'ENVERS.
///
/// Identity connaît les moyens de paiement d'un client parce qu'il s'en sert pour
/// ENCAISSER. Rembourser, c'est le même numéro parcouru dans l'autre sens — et il
/// n'y a aucune raison de demander deux fois au client le numéro qu'il a déjà
/// enregistré, ni de créer un second endroit où il pourrait devenir faux.
///
/// Seul le Mobile Money sort d'ici : une carte n'est stockée que sous forme de
/// quatre derniers chiffres, avec lesquels on ne verse rien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Provider">Opérateur (mtnmomo, moovmoney, celtis…).</param>
/// <param name="AccountNumber">MSISDN, chiffres uniquement.</param>
/// <param name="AccountName">Nom du titulaire, tel que le PSP l'affichera.</param>
public sealed record UserPayoutAccount(string Provider, string AccountNumber, string AccountName);
