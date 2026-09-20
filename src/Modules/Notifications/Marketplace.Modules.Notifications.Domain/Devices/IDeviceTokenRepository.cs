namespace Marketplace.Modules.Notifications.Domain.Devices;

/// <summary>Accès aux jetons d'appareil (push) — port du domaine.</summary>
public interface IDeviceTokenRepository
{
    Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task AddAsync(DeviceToken device, CancellationToken cancellationToken = default);
    void Remove(DeviceToken device);

    /// <summary>Jetons actifs d'un utilisateur (tous ses appareils).</summary>
    Task<IReadOnlyList<DeviceToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// LES UTILISATEURS JOIGNABLES PAR PUSH, pour une diffusion.
    ///
    /// C'EST LE MODULE NOTIFICATIONS QUI DÉFINIT L'AUDIENCE, pas Identity. Énumérer la
    /// table des utilisateurs depuis ici pour leur écrire à tous serait franchir une
    /// frontière de module pour fabriquer une liste de diffusion — et créer une ligne de
    /// notification par compte, y compris pour les dizaines de milliers qui n'ont jamais
    /// installé l'application.
    ///
    /// L'audience d'une diffusion push, c'est l'ensemble des gens qui ont enregistré un
    /// appareil : ceux-là, et eux seuls, peuvent la recevoir. Ceux qui n'ont que le site
    /// voient le bandeau d'annonce, qui est la même information par un autre chemin.
    ///
    /// <paramref name="platform"/> restreint à « android » ou « ios » quand une annonce
    /// ne concerne qu'un magasin. Null = les deux.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<IReadOnlyList<Guid>> ListUserIdsAsync(string? platform, int max, CancellationToken cancellationToken = default);

    /// <summary>Supprime les jetons devenus invalides (retour « unregistered » de FCM).</summary>
    Task RemoveByTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken cancellationToken = default);
}
