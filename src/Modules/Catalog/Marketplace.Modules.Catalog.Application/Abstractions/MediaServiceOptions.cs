namespace Marketplace.Modules.Catalog.Application.Abstractions;

/// <summary>
/// Configuration du service média externe (section « Media » de la config).
/// Tant que ClientId est vide, le module utilise un stub hors-ligne (aucune
/// dépendance réseau en dev) ; renseigner ClientId active l'adaptateur réel.
/// </summary>
public sealed class MediaServiceOptions
{
    /// <summary>Base URL du service média.</summary>
    public string BaseUrl { get; set; } = "https://hbamediacore.fr/";

    /// <summary>Identifiant client (en-tête « x-client-id ») — secret, hors du dépôt.</summary>
    public string ClientId { get; set; } = "4f227e9b-d341-4a30-86c6-0a891a399951";

    /// <summary>Visibilité par défaut des fichiers uploadés (PUBLIC / PRIVATE).</summary>
    public string DefaultVisibility { get; set; } = "PUBLIC";

    /// <summary>L'adaptateur réel n'est actif que si la base URL et le ClientId sont renseignés.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ClientId);
}
