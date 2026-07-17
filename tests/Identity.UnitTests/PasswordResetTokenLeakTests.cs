using FluentAssertions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// TEST DE NON-RÉGRESSION D'UNE FAILLE CRITIQUE. NE PAS SUPPRIMER, NE PAS ASSOUPLIR.
///
/// `RequestPasswordResetCommand` implémentait `ICommand&lt;string?&gt;` : elle RENVOYAIT le
/// jeton de réinitialisation en clair. Et l'endpoint ANONYME
/// `POST /mobile/auth/password/forgot` le recopiait dans sa réponse HTTP :
///
///     // TODO production : envoyer result.Value par e-mail/SMS et ne pas l'exposer ici.
///     return Results.Ok(new { sent = true, token = result.Value });
///
/// N'importe qui, sans compte, saisissait l'e-mail d'un administrateur, lisait son jeton
/// dans la réponse, et prenait son compte. Les cinq hôtes partagent la même base Identity :
/// c'était la prise de contrôle totale de la plateforme, en deux requêtes.
///
/// La cause profonde n'était pas la négligence : il n'existait AUCUN canal e-mail dans le
/// dépôt. Le jeton n'avait nulle part où aller, alors il est sorti par la seule porte
/// ouverte. Une capacité manquante finit toujours par être contournée.
///
/// ─── POURQUOI CE TEST PORTE SUR LE TYPE, ET PAS SUR UN COMPORTEMENT ───
///
/// Parce que la faille ÉTAIT une propriété du type. Tant que la commande ne peut pas
/// renvoyer de valeur, AUCUN endpoint ne peut divulguer le jeton — il ne le reçoit pas.
/// La garantie est structurelle, pas comportementale : elle ne dépend pas de la vigilance
/// du prochain développeur qui touchera au BFF.
///
/// Si ce test échoue, c'est que quelqu'un a redonné une valeur de retour à la commande.
/// Ne « corrigez » pas le test : le jeton doit partir par e-mail (voir
/// PasswordResetRequestedIntegrationEvent et SendPasswordResetEmailHandler), jamais par
/// HTTP.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class PasswordResetTokenLeakTests
{
    [Fact]
    public void RequestPasswordResetCommand_ne_renvoie_AUCUNE_valeur()
    {
        var type = typeof(RequestPasswordResetCommand);

        var returnsSomething = type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

        returnsSomething.Should().BeFalse(
            "cette commande renvoyait le JETON DE RÉINITIALISATION en clair, qu'un endpoint "
            + "anonyme recopiait dans sa réponse HTTP — permettant de prendre n'importe quel "
            + "compte, administrateurs compris. Le jeton doit partir par e-mail, jamais par "
            + "une valeur de retour. Voir PasswordResetRequestedIntegrationEvent.");
    }

    [Fact]
    public void RequestPasswordResetCommand_reste_bien_une_commande()
    {
        // Garde le pendant du test précédent : on veut « aucune valeur de retour »,
        // pas « plus de commande du tout ».
        typeof(ICommand).IsAssignableFrom(typeof(RequestPasswordResetCommand)).Should().BeTrue();
    }
}
