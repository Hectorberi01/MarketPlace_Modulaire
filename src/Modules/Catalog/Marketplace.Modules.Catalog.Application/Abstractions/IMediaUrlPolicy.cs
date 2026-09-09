namespace Marketplace.Modules.Catalog.Application.Abstractions;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// QUELLES URL DE MÉDIA LA PLACE ACCEPTE-T-ELLE DE SERVIR ?
///
/// La route « attacher un média par URL » acceptait n'importe quelle chaîne : ni
/// schéma, ni hôte, ni appartenance au stockage de la plateforme. Un vendeur pouvait
/// donc faire pointer l'image principale de sa fiche vers l'hôte de son choix — pixel
/// de traçage relevant l'adresse IP de chaque acheteur qui ouvre la fiche anonyme,
/// contenu remplacé APRÈS le passage du modérateur, ou URI de données.
///
/// La validation d'upload par octets d'en-tête, pourtant rigoureuse, était
/// intégralement contournée par cette route : elle ne téléverse rien, donc elle n'a
/// jamais d'octets à inspecter.
///
/// LA RÈGLE : la place ne sert que des médias qu'elle héberge. Ce que le navigateur
/// de l'acheteur va chercher doit être sous le contrôle de la plateforme, sans quoi
/// « modéré » ne veut rien dire — le contenu peut changer après la décision.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IMediaUrlPolicy
{
    /// <summary>
    /// Cette URL peut-elle être attachée à un produit ?
    ///
    /// Vrai uniquement pour une URL absolue en HTTPS dont l'hôte est celui du
    /// stockage de la plateforme. Toute autre forme est refusée — y compris HTTP en
    /// clair, qui exposerait l'acheteur à une substitution en transit.
    /// </summary>
    bool IsAllowed(string? url);

    /// <summary>
    /// Description des hôtes acceptés, pour le message d'erreur. Sans elle, le
    /// vendeur refusé ne saurait pas quoi corriger.
    /// </summary>
    string AllowedDescription { get; }
}
