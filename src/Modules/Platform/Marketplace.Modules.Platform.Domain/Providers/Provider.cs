using System.Text.Json;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Platform.Domain.Providers;

/// <summary>
/// Un fournisseur configuré : son canal, son code, son mode, ses réglages et ses
/// secrets. Les secrets arrivent ici DÉJÀ CHIFFRÉS — l'agrégat ne connaît ni la clé
/// maître ni l'algorithme, il ne fait que porter des valeurs opaques.
///
/// UNE RÈGLE PORTE AILLEURS : « un seul fournisseur actif par canal » (hors
/// paiement) traverse plusieurs agrégats, donc elle est vérifiée par le handler de
/// commande, qui seul voit les autres lignes du canal. Voir ChannelRules.
/// </summary>
public sealed class Provider : AggregateRoot<ProviderId>
{
    private Provider()
    {
    }

    private Provider(ProviderId id, ProviderChannel channel, string code, ProviderMode mode,
        string settingsJson, string secretsJson, Guid actorId, DateTime nowUtc)
        : base(id)
    {
        Channel = channel;
        Code = code;
        Mode = mode;
        SettingsJson = settingsJson;
        SecretsJson = secretsJson;
        IsEnabled = false;
        Priority = 0;
        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = actorId;
    }

    public ProviderChannel Channel { get; private set; }
    public string Code { get; private set; } = default!;
    public ProviderMode Mode { get; private set; }

    /// <summary>
    /// Un fournisseur est créé DÉSACTIVÉ. Activer est un geste distinct, et c'est
    /// voulu : enregistrer des clés et les mettre en service ne sont pas la même
    /// décision — on prépare un PSP avant de lui confier les encaissements.
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Ordre d'affichage et de préférence sur le canal. Le plus petit passe d'abord.</summary>
    public int Priority { get; private set; }

    /// <summary>Objet JSON des réglages NON secrets. Persisté en jsonb.</summary>
    public string SettingsJson { get; private set; } = "{}";

    /// <summary>Objet JSON des secrets, valeurs CHIFFRÉES. Persisté en jsonb.</summary>
    public string SecretsJson { get; private set; } = "{}";

    public DateTime UpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    /// <summary>Réglages non secrets, lus depuis le JSON.</summary>
    public IReadOnlyDictionary<string, string> ReadSettings() => Parse(SettingsJson);

    /// <summary>Secrets ENCORE CHIFFRÉS. Le déchiffrement appartient à l'infrastructure.</summary>
    public IReadOnlyDictionary<string, string> ReadProtectedSecrets() => Parse(SecretsJson);

    public static Result<Provider> Create(
        ProviderChannel channel, string code, ProviderMode mode,
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string> protectedSecrets,
        Guid actorId, DateTime nowUtc)
    {
        var descriptor = ProviderCatalog.Find(channel, code ?? string.Empty);
        if (descriptor is null)
        {
            return Error.Validation("platform.provider_unknown",
                "Fournisseur inconnu pour ce canal : aucun adaptateur ne sait le piloter.");
        }

        if (mode == ProviderMode.Sandbox && !descriptor.SupportsSandbox)
        {
            return Error.Validation("platform.provider_no_sandbox",
                $"{descriptor.DisplayName} n'a pas de bac à sable.");
        }

        var known = Allowed(descriptor);
        var inconnu = settings.Keys.FirstOrDefault(k => !known.Contains(k));
        if (inconnu is not null)
        {
            return Error.Validation("platform.setting_unknown", $"Réglage inconnu : « {inconnu} ».");
        }

        var secretInconnu = protectedSecrets.Keys.FirstOrDefault(k => !descriptor.RequiredSecrets.Contains(k));
        if (secretInconnu is not null)
        {
            return Error.Validation("platform.secret_unknown", $"Secret inconnu : « {secretInconnu} ».");
        }

        return new Provider(
            ProviderId.New(), channel, descriptor.Code, mode,
            Serialize(settings), Serialize(protectedSecrets), actorId, nowUtc);
    }

    /// <summary>
    /// Met à jour réglages et secrets.
    ///
    /// LES SECRETS ABSENTS SONT CONSERVÉS, et c'est l'invariant qui compte. La console
    /// ne relit jamais une valeur secrète : son formulaire renvoie donc des champs
    /// vides pour ceux qu'on ne touche pas. Les écraser effacerait la clé d'API à
    /// chaque fois qu'un admin corrige une URL.
    /// </summary>
    public Result UpdateConfiguration(
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string> protectedSecretsToReplace,
        ProviderMode mode, Guid actorId, DateTime nowUtc)
    {
        var descriptor = ProviderCatalog.Find(Channel, Code);
        if (descriptor is null)
        {
            return Result.Failure(Error.Failure("platform.provider_unknown",
                "Ce fournisseur n'a plus d'adaptateur dans cette version du code."));
        }

        if (mode == ProviderMode.Sandbox && !descriptor.SupportsSandbox)
        {
            return Result.Failure(Error.Validation("platform.provider_no_sandbox",
                $"{descriptor.DisplayName} n'a pas de bac à sable."));
        }

        var known = Allowed(descriptor);
        var inconnu = settings.Keys.FirstOrDefault(k => !known.Contains(k));
        if (inconnu is not null)
        {
            return Result.Failure(Error.Validation("platform.setting_unknown", $"Réglage inconnu : « {inconnu} »."));
        }

        var fusion = new Dictionary<string, string>(ReadProtectedSecrets(), StringComparer.Ordinal);
        foreach (var (cle, valeur) in protectedSecretsToReplace)
        {
            if (!descriptor.RequiredSecrets.Contains(cle))
            {
                return Result.Failure(Error.Validation("platform.secret_unknown", $"Secret inconnu : « {cle} »."));
            }

            fusion[cle] = valeur;
        }

        SettingsJson = Serialize(settings);
        SecretsJson = Serialize(fusion);
        Mode = mode;
        Touch(actorId, nowUtc);

        // Un fournisseur ACTIF dont la configuration devient incomplète doit s'arrêter
        // de lui-même. Le laisser actif produirait des appels qui échouent à chaque
        // fois, et l'écran continuerait d'afficher « actif ».
        if (IsEnabled && Missing(descriptor).Count > 0)
        {
            IsEnabled = false;
            return Result.Failure(Error.Validation("platform.provider_disabled_incomplete",
                "Configuration incomplète : le fournisseur a été désactivé."));
        }

        return Result.Success();
    }

    public Result Enable(Guid actorId, DateTime nowUtc)
    {
        var descriptor = ProviderCatalog.Find(Channel, Code);
        if (descriptor is null)
        {
            return Result.Failure(Error.Failure("platform.provider_unknown",
                "Ce fournisseur n'a plus d'adaptateur dans cette version du code."));
        }

        var manquantes = Missing(descriptor);
        if (manquantes.Count > 0)
        {
            return Result.Failure(Error.Validation("platform.provider_incomplete",
                $"Clés manquantes : {string.Join(", ", manquantes)}."));
        }

        IsEnabled = true;
        Touch(actorId, nowUtc);
        return Result.Success();
    }

    public Result Disable(Guid actorId, DateTime nowUtc)
    {
        IsEnabled = false;
        Touch(actorId, nowUtc);
        return Result.Success();
    }

    public Result SetPriority(int priority, Guid actorId, DateTime nowUtc)
    {
        if (priority is < 0 or > 1000)
        {
            return Result.Failure(Error.Validation("platform.priority_invalid", "La priorité doit être comprise entre 0 et 1000."));
        }

        Priority = priority;
        Touch(actorId, nowUtc);
        return Result.Success();
    }

    /// <summary>Clés exigées et toujours absentes ou vides.</summary>
    public IReadOnlyList<string> Missing(ProviderDescriptor descriptor)
    {
        var settings = ReadSettings();
        var secrets = ReadProtectedSecrets();

        var manquantes = descriptor.RequiredSettings
            .Where(k => !settings.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v))
            .ToList();

        manquantes.AddRange(descriptor.RequiredSecrets
            .Where(k => !secrets.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)));

        return manquantes;
    }

    private void Touch(Guid actorId, DateTime nowUtc)
    {
        UpdatedByUserId = actorId;
        UpdatedAtUtc = nowUtc;
    }

    private static HashSet<string> Allowed(ProviderDescriptor descriptor)
        => new(descriptor.RequiredSettings.Concat(descriptor.OptionalSettings), StringComparer.Ordinal);

    private static string Serialize(IReadOnlyDictionary<string, string> valeurs)
        => JsonSerializer.Serialize(valeurs);

    private static IReadOnlyDictionary<string, string> Parse(string json)
        => string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
}
