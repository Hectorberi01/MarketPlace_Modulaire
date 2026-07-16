using Marketplace.Modules.Identity.Application.Abstractions;

namespace Marketplace.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Section « Identity:Registration » de la configuration.
///
/// Les valeurs par défaut sont les plus STRICTES. C'est volontaire : une option de
/// sécurité qui s'ouvre toute seule quand on oublie de la déclarer n'est pas une
/// option, c'est un piège. Un fichier de configuration absent doit fermer la porte,
/// pas l'ouvrir.
/// </summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Identity:Registration";

    /// <summary>Inscription publique (app acheteur, site) : approbation requise.</summary>
    public bool RequireApprovalForBuyers { get; set; } = true;

    /// <summary>
    /// Comptes créés depuis la console d'administration : approbation requise.
    ///
    /// Faux par défaut, et c'est le seul assouplissement. Un administrateur qui vient
    /// de créer un compte, RCCM en main, n'a rien à s'auto-valider : le clic
    /// n'apporterait aucune garantie supplémentaire, seulement une file qui se
    /// remplit de son propre travail.
    /// </summary>
    public bool RequireApprovalForAdminCreated { get; set; } = false;
}

/// <summary>Expose les options aux handlers, sans les faire dépendre de la config.</summary>
internal sealed class RegistrationPolicy : IRegistrationPolicy
{
    private readonly RegistrationOptions _options;

    public RegistrationPolicy(RegistrationOptions options) => _options = options;

    public bool RequireApprovalForBuyers => _options.RequireApprovalForBuyers;

    public bool RequireApprovalForAdminCreated => _options.RequireApprovalForAdminCreated;
}
