namespace Marketplace.Modules.Identity.Application.Abstractions;

/// <summary>
/// Politique d'activation des comptes à l'inscription : un compte nouvellement
/// créé est-il utilisable tout de suite, ou attend-il l'aval d'un administrateur ?
///
/// Pourquoi un réglage, et non une constante :
///
/// Exiger l'approbation de TOUS les comptes est le choix le plus sûr, et c'est
/// celui qui est actif. Mais il a un coût que rien n'annule : un acheteur qui
/// s'inscrit à 23 h ne peut rien commander avant qu'un humain ne le valide. Tant
/// que les inscriptions se comptent en dizaines, cela se tient. Le jour où elles
/// se comptent en centaines, la file devient le goulot d'étranglement de la
/// plateforme — et ce jour-là, il faut pouvoir n'exiger l'approbation que des
/// vendeurs sans attendre une livraison de code.
///
/// D'où deux drapeaux distincts plutôt qu'un seul booléen : on peut relâcher
/// l'acheteur en gardant le vendeur sous contrôle, ce qui est très probablement
/// l'état d'équilibre à terme.
/// </summary>
public interface IRegistrationPolicy
{
    /// <summary>
    /// Un compte issu de l'inscription publique (app acheteur, site) attend-il une
    /// validation ? Si faux, il est actif immédiatement.
    /// </summary>
    bool RequireApprovalForBuyers { get; }

    /// <summary>
    /// Un compte créé par la console d'administration attend-il une validation ?
    ///
    /// Répondre « non » se défend : c'est VOUS qui venez de le créer, après examen
    /// du RCCM. Vous faire ensuite valider votre propre saisie n'ajoute aucune
    /// garantie — seulement un clic. Le réglage existe malgré tout, pour le cas où
    /// plusieurs administrateurs se relaient et où la création doit être revue par
    /// un second regard.
    /// </summary>
    bool RequireApprovalForAdminCreated { get; }
}
