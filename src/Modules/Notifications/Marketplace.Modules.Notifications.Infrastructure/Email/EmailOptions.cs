namespace Marketplace.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Configuration de l'envoi d'e-mails. Section : « Notifications:Email ».
/// Le secret (<see cref="ApiKey"/>) vient du vault, jamais du dépôt.
///
/// Fournisseur : <b>Resend</b> — le même que le site vitrine hbaexpress-site, pour n'avoir
/// qu'un seul domaine à authentifier (SPF/DKIM) et qu'une seule facture. API HTTP simple,
/// aucune dépendance SMTP à traîner.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Clé d'API Resend (« re_… »). Vide = envoi désactivé.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Expéditeur, ex. « HBA Express &lt;no-reply@hbaexpress.com&gt; ».
    ///
    /// Le domaine doit être VÉRIFIÉ chez Resend (SPF + DKIM). Avec un domaine non
    /// vérifié, Resend refuse l'envoi (403) — et comme l'échec se produit dans l'outbox, il
    /// serait rejoué toutes les 5 secondes sans jamais aboutir. Voir la garde de démarrage
    /// dans NotificationsModuleInstaller.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Base publique des liens cliquables, ex. « https://hbaexpress.com ».
    ///
    /// Sert à fabriquer les URL de vérification et de réinitialisation. Sans elle, l'e-mail
    /// partirait avec un lien relatif — donc mort. C'est pourquoi elle est exigée en
    /// production au même titre que la clé.
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(From)
        && !string.IsNullOrWhiteSpace(AppBaseUrl);

    /// <summary>Construit un lien absolu propre, quelle que soit la présence d'un « / » final.</summary>
    public string Link(string path)
        => $"{AppBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
