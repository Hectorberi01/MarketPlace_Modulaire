using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Partners;

/// <summary>État commercial d'un partenaire.</summary>
public enum PartnerStatus
{
    /// <summary>Créé, pas encore autorisé à créer des livraisons.</summary>
    Pending = 0,

    /// <summary>Actif.</summary>
    Active = 1,

    /// <summary>Suspendu — impayé, abus, demande du partenaire.</summary>
    Suspended = 2
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SITE MARCHAND TIERS QUI CONSOMME HBA DELIVERY.
///
/// C'est ce qui transforme le moteur logistique en PRODUIT : sans partenaire, il
/// ne sert que HBAExpress et HBA Food, et le principe directeur du cahier — « ne
/// pas connaître la nature commerciale de ce qui est livré » — n'aurait aucune
/// contrepartie.
///
/// PLUSIEURS CLÉS PAR PARTENAIRE, ET C'EST INDISPENSABLE.
///
/// Avec une seule clé, la faire tourner impose une coupure : on révoque, le site
/// tombe, on redéploie. Personne ne le fait, et la clé reste dix ans. Avec
/// plusieurs, la rotation est sans interruption — émettre, déployer, révoquer
/// l'ancienne — et une clé compromise se coupe sans arrêter le partenaire.
///
/// Le quota est porté ici, et non dans une passerelle : il est PAR PARTENAIRE,
/// pas par adresse IP, et seul le domaine sait à qui appartient une clé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Partner : AggregateRoot<PartnerId>
{
    private readonly List<PartnerApiKey> _apiKeys = new();

    private Partner(PartnerId id, string name, string contactEmail, int dailyQuota)
        : base(id)
    {
        Name = name;
        ContactEmail = contactEmail;
        DailyQuota = dailyQuota;
        Status = PartnerStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private Partner()
    {
        Name = string.Empty;
        ContactEmail = string.Empty;
    }

    public string Name { get; private set; }

    public string ContactEmail { get; private set; }

    /// <summary>Nombre de livraisons créables par jour. 0 = illimité.</summary>
    public int DailyQuota { get; private set; }

    public PartnerStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>URL de rappel des webhooks. Nulle tant que le partenaire n'en veut pas.</summary>
    public string? WebhookUrl { get; private set; }

    /// <summary>
    /// Secret de signature des webhooks. Le partenaire s'en sert pour vérifier
    /// que l'appel vient bien de nous — sans lui, n'importe qui peut lui annoncer
    /// une livraison terminée.
    /// </summary>
    public string? WebhookSecret { get; private set; }

    public IReadOnlyCollection<PartnerApiKey> ApiKeys => _apiKeys.AsReadOnly();

    /// <summary>Le partenaire peut-il créer des livraisons ?</summary>
    public bool CanCreateDeliveries => Status is PartnerStatus.Active;

    public static Result<Partner> Register(string? name, string? contactEmail, int dailyQuota = 0)
    {
        var trimmedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmedName is null)
        {
            return Result.Failure<Partner>(
                Error.Validation("partner.name_required", "Le nom du partenaire est requis."));
        }

        var email = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim();
        if (email is null || !email.Contains('@', StringComparison.Ordinal))
        {
            return Result.Failure<Partner>(
                Error.Validation("partner.email_invalid", "Une adresse de contact valide est requise."));
        }

        if (dailyQuota < 0)
        {
            return Result.Failure<Partner>(
                Error.Validation("partner.quota_negative", "Le quota ne peut pas être négatif."));
        }

        return new Partner(PartnerId.New(), trimmedName, email, dailyQuota);
    }

    public Result Activate()
    {
        Status = PartnerStatus.Active;
        return Result.Success();
    }

    public Result Suspend()
    {
        Status = PartnerStatus.Suspended;
        return Result.Success();
    }

    /// <summary>
    /// Émet une clé. Le secret en clair n'existe QUE dans la valeur renvoyée :
    /// il n'est stocké nulle part et ne pourra pas être retrouvé.
    /// </summary>
    public Result<IssuedApiKey> IssueApiKey(string environmentTag, string? label = null)
    {
        if (Status is PartnerStatus.Suspended)
        {
            return Result.Failure<IssuedApiKey>(
                Error.Conflict("partner.suspended", "Ce partenaire est suspendu."));
        }

        // Plafond volontairement bas : au-delà, ce ne sont plus des clés de
        // rotation mais un oubli de révocation. Chaque clé active est une porte.
        if (_apiKeys.Count(k => k.IsActive) >= 5)
        {
            return Result.Failure<IssuedApiKey>(
                Error.Conflict("partner.too_many_keys",
                    "Ce partenaire a déjà cinq clés actives. Révoquez-en une avant d'en émettre une nouvelle."));
        }

        var (key, issued) = PartnerApiKey.Issue(environmentTag, label);
        _apiKeys.Add(key);

        return issued;
    }

    public Result RevokeApiKey(Guid apiKeyId)
    {
        var key = _apiKeys.FirstOrDefault(k => k.Id == apiKeyId);
        if (key is null)
        {
            return Result.Failure(Error.NotFound("partner.key_not_found", "Clé introuvable."));
        }

        key.Revoke();
        return Result.Success();
    }

    /// <summary>Retrouve la clé active correspondant au préfixe présenté.</summary>
    public PartnerApiKey? FindActiveKey(string prefix)
        => _apiKeys.FirstOrDefault(k => k.IsActive && k.Prefix == prefix);

    /// <summary>Configure le rappel des webhooks. Le secret est fourni par l'appelant.</summary>
    public Result ConfigureWebhook(string? url, string? secret)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            WebhookUrl = null;
            WebhookSecret = null;
            return Result.Success();
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            // HTTPS EXIGÉ, sans exception. Un webhook porte l'état d'une commande
            // et une référence client ; en clair, il est lisible et modifiable par
            // tout intermédiaire réseau.
            return Result.Failure(
                Error.Validation("partner.webhook_https_required", "L'URL de rappel doit être en HTTPS."));
        }

        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            return Result.Failure(
                Error.Validation("partner.webhook_secret_weak",
                    "Le secret de signature doit compter au moins 32 caractères."));
        }

        WebhookUrl = parsed.ToString();
        WebhookSecret = secret;
        return Result.Success();
    }
}
