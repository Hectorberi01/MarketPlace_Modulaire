namespace Marketplace.Modules.Returns.Application.Abstractions;

/// <summary>
/// Les règles commerciales du retour, fournies par la configuration.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE INTERFACE PLUTÔT QU'UN `IConfiguration` DANS LE HANDLER.
///
/// La couche Application ne connaît pas Microsoft.Extensions.Configuration, et ce n'est
/// pas un dogme : c'est ce qui permet de tester la fenêtre de retour sans monter un
/// arbre de configuration. C'est le même motif qu'`IAuthTokenSettings` côté Identity.
///
/// UN DÉLAI EST UNE DÉCISION COMMERCIALE, PAS UNE CONSTANTE. Sept jours est une valeur
/// par défaut, pas une vérité : elle se négocie avec le service client, elle change avec
/// les catégories, et elle pourrait devoir changer un lundi matin. Elle n'a donc rien à
/// faire en dur dans un agrégat.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IReturnPolicySettings
{
    /// <summary>
    /// Délai pendant lequel un retour reste ouvrable APRÈS LA LIVRAISON.
    ///
    /// Compté depuis la réception, jamais depuis la commande : une livraison lente ne
    /// doit pas manger la fenêtre de l'acheteur.
    /// </summary>
    TimeSpan WindowAfterDelivery { get; }
}
