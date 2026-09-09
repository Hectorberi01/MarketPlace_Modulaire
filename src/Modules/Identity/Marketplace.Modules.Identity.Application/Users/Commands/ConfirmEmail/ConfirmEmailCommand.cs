using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.ConfirmEmail;

/// <summary>
/// Confirme l'e-mail d'un compte à partir du jeton reçu par lien.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ADRESSE, ET NON L'IDENTIFIANT — LE CHANGEMENT EST LÀ POUR FERMER UN ORACLE.
///
/// Cette commande prenait un <c>UserId</c>. Conséquence en amont : la route qui renvoie
/// un code de vérification devait DIVULGUER cet identifiant pour que l'écran de saisie
/// puisse ensuite confirmer. Elle rendait donc l'identifiant quand l'adresse existait, et
/// null sinon — un oracle d'existence de compte, interrogeable sans authentification, et
/// dont chaque réponse positive envoyait un vrai e-mail à la personne visée.
///
/// En prenant l'ADRESSE, plus rien n'a besoin d'être divulgué : l'application la connaît
/// déjà, puisque l'utilisateur vient de la saisir. La route de renvoi peut alors répondre
/// la même chose dans tous les cas.
///
/// C'EST UNE RUPTURE DE CONTRAT, assumée : une application qui envoie encore un
/// <c>userId</c> ne confirmera plus. Elle n'en obtiendrait de toute façon aucun, la route
/// de renvoi ayant cessé d'en fournir — les deux changements se livrent ensemble.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ConfirmEmailCommand(string Email, string Token) : ICommand;
