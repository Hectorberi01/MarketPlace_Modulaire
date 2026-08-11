using System.Security.Cryptography;
using FluentValidation;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Partners;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Partners.Commands;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ADMINISTRATION DES PARTENAIRES.
///
/// Ces commandes manquaient entièrement. Le domaine savait enregistrer un
/// partenaire, émettre une clé et configurer un webhook ; rien ne l'appelait.
/// L'API publique était donc complète et INACCESSIBLE — le seul moyen d'obtenir
/// une clé était d'écrire des lignes à la main en base.
///
/// ⚠️ CE FICHIER MANIPULE DES SECRETS. TROIS RÈGLES.
///
///   1. Un secret n'est lisible QU'UNE FOIS, dans la réponse qui le crée. Il
///      n'est stocké nulle part en clair et ne pourra jamais être relu. Un
///      partenaire qui perd sa clé en obtient une nouvelle ; il ne la
///      « récupère » pas.
///   2. Aucun secret ne transite par une requête. Le secret de webhook est
///      ENGENDRÉ ici, jamais reçu : un secret choisi par un humain est un secret
///      faible, et un secret qui traverse un formulaire finit dans un journal.
///   3. Rien de tout cela ne doit être journalisé. Les journaux vivent bien plus
///      longtemps que les clés.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record RegisterPartnerCommand(string? Name, string? ContactEmail, int DailyQuota = 0)
    : ICommand<Guid>;

/// <summary>Active un partenaire : c'est le geste qui l'autorise à créer des courses.</summary>
public sealed record ActivatePartnerCommand(Guid PartnerId) : ICommand;

/// <summary>Suspend un partenaire — impayé, abus, ou à sa demande.</summary>
public sealed record SuspendPartnerCommand(Guid PartnerId) : ICommand;

/// <summary>
/// Clé émise, telle qu'on la rend à l'appelant.
/// </summary>
/// <param name="ApiKey">La clé complète. VISIBLE UNE SEULE FOIS.</param>
/// <param name="Prefix">Partie publique : c'est elle qu'on affiche et qu'on journalise ensuite.</param>
public sealed record IssuedApiKeyResponse(string ApiKey, string Prefix);

/// <summary>
/// Émet une clé d'API.
///
/// <c>Environment</c> vaut « live » ou « test », et rien d'autre. Le libellé est
/// purement cosmétique pour le code — mais c'est ce que l'intégrateur LIT pour
/// savoir s'il tient une clé de production. Laisser passer un texte libre
/// permettrait de fabriquer une clé « hba_test_… » parfaitement vivante.
/// </summary>
public sealed record IssuePartnerApiKeyCommand(Guid PartnerId, string? Environment, string? Label = null)
    : ICommand<IssuedApiKeyResponse>;

public sealed record RevokePartnerApiKeyCommand(Guid PartnerId, Guid ApiKeyId) : ICommand;

/// <summary>
/// Résultat de la configuration du rappel.
///
/// <c>Enabled</c> plutôt qu'un type entier nullable : une réponse « nulle » aurait
/// obligé chaque appelant à distinguer « effacé » de « erreur silencieuse », et
/// aurait fait porter au générique un type de référence nullable — la source
/// habituelle d'avertissements de nullabilité en cascade.
/// </summary>
public sealed record ConfiguredWebhookResponse(bool Enabled, string? Url, string? Secret)
{
    public static ConfiguredWebhookResponse Disabled { get; } = new(false, null, null);
}

/// <summary>
/// Configure — ou efface — le rappel des webhooks.
///
/// Le secret n'est PAS un paramètre : il est engendré. Voir la règle 2 en tête de
/// fichier.
/// </summary>
public sealed record ConfigurePartnerWebhookCommand(Guid PartnerId, string? Url)
    : ICommand<ConfiguredWebhookResponse>;

internal sealed class RegisterPartnerCommandValidator : AbstractValidator<RegisterPartnerCommand>
{
    public RegisterPartnerCommandValidator()
    {
        // Validation de FORME. Le domaine refait les contrôles métier, quel que
        // soit le chemin d'entrée.
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ContactEmail).NotEmpty().MaximumLength(320);
        RuleFor(c => c.DailyQuota).GreaterThanOrEqualTo(0);
    }
}

internal sealed class PartnerCommandHandler
    : ICommandHandler<RegisterPartnerCommand, Guid>,
      ICommandHandler<ActivatePartnerCommand>,
      ICommandHandler<SuspendPartnerCommand>,
      ICommandHandler<IssuePartnerApiKeyCommand, IssuedApiKeyResponse>,
      ICommandHandler<RevokePartnerApiKeyCommand>,
      ICommandHandler<ConfigurePartnerWebhookCommand, ConfiguredWebhookResponse>
{
    /// <summary>Les seuls libellés d'environnement admis.</summary>
    private static readonly string[] AllowedEnvironments = ["live", "test"];

    /// <summary>
    /// Octets d'aléa du secret de webhook. 32 octets ≈ 43 caractères une fois
    /// encodés — au-delà du minimum de 32 exigé par le domaine, et sans commune
    /// mesure avec ce qu'un humain choisirait.
    /// </summary>
    private const int WebhookSecretBytes = 32;

    private readonly IPartnerRepository _partners;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public PartnerCommandHandler(IPartnerRepository partners, IDeliveryUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RegisterPartnerCommand command, CancellationToken ct)
    {
        var partner = Partner.Register(command.Name, command.ContactEmail, command.DailyQuota);
        if (partner.IsFailure)
        {
            return Result.Failure<Guid>(partner.Error);
        }

        await _partners.AddAsync(partner.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // Le partenaire naît « Pending » : l'enregistrer ne l'autorise à rien.
        // Il faudra un geste explicite d'activation — sinon une faute de frappe
        // dans un formulaire ouvrirait un accès.
        return partner.Value.Id.Value;
    }

    public Task<Result> Handle(ActivatePartnerCommand c, CancellationToken ct)
        => MutateAsync(c.PartnerId, p => p.Activate(), ct);

    public Task<Result> Handle(SuspendPartnerCommand c, CancellationToken ct)
        => MutateAsync(c.PartnerId, p => p.Suspend(), ct);

    public async Task<Result<IssuedApiKeyResponse>> Handle(
        IssuePartnerApiKeyCommand command, CancellationToken ct)
    {
        var environment = command.Environment?.Trim().ToLowerInvariant();
        if (environment is null || !AllowedEnvironments.Contains(environment))
        {
            return Result.Failure<IssuedApiKeyResponse>(
                Error.Validation("partner.environment_invalid",
                    "L'environnement doit valoir « live » ou « test » : c'est ce que l'intégrateur lit "
                    + "dans la clé pour savoir s'il travaille en production."));
        }

        var partner = await _partners.GetByIdAsync(new PartnerId(command.PartnerId), ct);
        if (partner is null)
        {
            return Result.Failure<IssuedApiKeyResponse>(NotFound);
        }

        var issued = partner.IssueApiKey(environment, command.Label);
        if (issued.IsFailure)
        {
            return Result.Failure<IssuedApiKeyResponse>(issued.Error);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // ⚠️ SEUL ENDROIT DE TOUT LE SYSTÈME OÙ CETTE VALEUR EXISTE EN CLAIR.
        // Après cette réponse, elle est irrécupérable — y compris pour nous.
        return new IssuedApiKeyResponse(issued.Value.Key, issued.Value.Prefix);
    }

    public Task<Result> Handle(RevokePartnerApiKeyCommand c, CancellationToken ct)
        => MutateAsync(c.PartnerId, p => p.RevokeApiKey(c.ApiKeyId), ct);

    public async Task<Result<ConfiguredWebhookResponse>> Handle(
        ConfigurePartnerWebhookCommand command, CancellationToken ct)
    {
        var partner = await _partners.GetByIdAsync(new PartnerId(command.PartnerId), ct);
        if (partner is null)
        {
            return Result.Failure<ConfiguredWebhookResponse>(NotFound);
        }

        // URL vide = on efface. Le domaine efface alors le secret avec elle : un
        // secret orphelin survivrait sans usage et sans expiration.
        if (string.IsNullOrWhiteSpace(command.Url))
        {
            var cleared = partner.ConfigureWebhook(null, null);
            if (cleared.IsFailure)
            {
                return Result.Failure<ConfiguredWebhookResponse>(cleared.Error);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            return ConfiguredWebhookResponse.Disabled;
        }

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(WebhookSecretBytes));

        var configured = partner.ConfigureWebhook(command.Url, secret);
        if (configured.IsFailure)
        {
            return Result.Failure<ConfiguredWebhookResponse>(configured.Error);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        // Reconfigurer engendre un NOUVEAU secret et invalide l'ancien. C'est
        // volontaire : c'est ainsi qu'on fait tourner un secret compromis. Le
        // partenaire doit donc redéployer sa vérification de signature — et le
        // savoir, d'où le fait que la valeur soit rendue ici.
        return new ConfiguredWebhookResponse(true, partner.WebhookUrl, secret);
    }

    private async Task<Result> MutateAsync(
        Guid partnerId, Func<Partner, Result> mutate, CancellationToken ct)
    {
        var partner = await _partners.GetByIdAsync(new PartnerId(partnerId), ct);
        if (partner is null)
        {
            return Result.Failure(NotFound);
        }

        var result = mutate(partner);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Error NotFound => Error.NotFound("partner.not_found", "Partenaire introuvable.");
}
