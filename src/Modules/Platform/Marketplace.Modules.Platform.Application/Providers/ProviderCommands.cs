using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Platform.Application.Abstractions;
using Marketplace.Modules.Platform.Domain.Providers;

namespace Marketplace.Modules.Platform.Application.Providers;

/// <summary>
/// Enregistre un fournisseur. Il est créé DÉSACTIVÉ : l'activation est une commande
/// distincte.
/// </summary>
public sealed record RegisterProviderCommand(
    string Channel,
    string Code,
    string Mode,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets,
    Guid ActorUserId) : ICommand<Guid>;

/// <summary>
/// Met à jour réglages, secrets et mode.
///
/// UN SECRET LAISSÉ VIDE N'EST PAS EFFACÉ, il est conservé. La console ne relit
/// jamais une valeur secrète, donc son formulaire renvoie des champs vides pour tout
/// ce qu'on ne touche pas : les écraser supprimerait la clé d'API à chaque correction
/// d'URL.
/// </summary>
public sealed record UpdateProviderCommand(
    Guid ProviderId,
    string Mode,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Secrets,
    Guid ActorUserId) : ICommand;

public sealed record EnableProviderCommand(Guid ProviderId, Guid ActorUserId) : ICommand;

public sealed record DisableProviderCommand(Guid ProviderId, Guid ActorUserId) : ICommand;

public sealed record SetProviderPriorityCommand(Guid ProviderId, int Priority, Guid ActorUserId) : ICommand;

public sealed record DeleteProviderCommand(Guid ProviderId, Guid ActorUserId) : ICommand;

public sealed class RegisterProviderCommandValidator : AbstractValidator<RegisterProviderCommand>
{
    public RegisterProviderCommandValidator()
    {
        // `IsDefined` en plus de `TryParse` : `TryParse` réussit sur une valeur
        // NUMÉRIQUE, membre de l'énumération ou non. « 9 » passerait et se
        // persisterait, pour se relire ensuite sans erreur.
        RuleFor(c => c.Channel)
            .Must(v => Enum.TryParse<ProviderChannel>(v, ignoreCase: true, out var c) && Enum.IsDefined(c))
            .WithMessage("Canal inconnu : attendu « Payment », « Email », « Sms », « WhatsApp » ou « Push ».");

        RuleFor(c => c.Mode)
            .Must(v => Enum.TryParse<ProviderMode>(v, ignoreCase: true, out var m) && Enum.IsDefined(m))
            .WithMessage("Mode inconnu : attendu « Sandbox » ou « Live ».");

        RuleFor(c => c.Code).NotEmpty().MaximumLength(50);
        RuleFor(c => c.ActorUserId).NotEmpty();
    }
}

public sealed class UpdateProviderCommandValidator : AbstractValidator<UpdateProviderCommand>
{
    public UpdateProviderCommandValidator()
    {
        RuleFor(c => c.ProviderId).NotEmpty();
        RuleFor(c => c.ActorUserId).NotEmpty();
        RuleFor(c => c.Mode)
            .Must(v => Enum.TryParse<ProviderMode>(v, ignoreCase: true, out var m) && Enum.IsDefined(m))
            .WithMessage("Mode inconnu : attendu « Sandbox » ou « Live ».");
    }
}

internal sealed class RegisterProviderCommandHandler : ICommandHandler<RegisterProviderCommand, Guid>
{
    private readonly IProviderRepository _repository;
    private readonly IPlatformUnitOfWork _unitOfWork;
    private readonly ISecretProtector _protector;
    private readonly IProviderCacheInvalidator _cache;

    public RegisterProviderCommandHandler(
        IProviderRepository repository, IPlatformUnitOfWork unitOfWork,
        ISecretProtector protector, IProviderCacheInvalidator cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _protector = protector;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(RegisterProviderCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ProviderChannel>(command.Channel, ignoreCase: true, out var channel) || !Enum.IsDefined(channel))
        {
            return Result.Failure<Guid>(Error.Validation("platform.channel_invalid", "Canal inconnu."));
        }

        if (!Enum.TryParse<ProviderMode>(command.Mode, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
        {
            return Result.Failure<Guid>(Error.Validation("platform.mode_invalid", "Mode inconnu."));
        }

        // Un seul enregistrement par (canal, code) : deux lignes « Email / Resend »
        // rendraient le fournisseur actif indéterminé.
        var existant = await _repository.FindAsync(channel, command.Code, cancellationToken);
        if (existant is not null)
        {
            return Result.Failure<Guid>(Error.Conflict("platform.provider_exists",
                "Ce fournisseur est déjà enregistré sur ce canal."));
        }

        var secrets = Protect(command.Secrets, _protector);
        var now = DateTime.UtcNow;

        var result = Provider.Create(channel, command.Code, mode, command.Settings, secrets, command.ActorUserId, now);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        _repository.Add(result.Value);
        _repository.AppendLog(ProviderChangeLog.For(result.Value, ProviderChangeAction.Created,
            Describe(command.Settings.Keys, command.Secrets.Keys, mode), command.ActorUserId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.InvalidateAsync(channel, cancellationToken);
        return result.Value.Id.Value;
    }

    /// <summary>Un secret vide n'est pas chiffré : vide veut dire « inchangé ».</summary>
    internal static Dictionary<string, string> Protect(
        IReadOnlyDictionary<string, string> secrets, ISecretProtector protector)
        => secrets
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => protector.Protect(kv.Value), StringComparer.Ordinal);

    /// <summary>
    /// Description journalisée : les NOMS des clés touchées, jamais leurs valeurs.
    /// </summary>
    internal static string Describe(IEnumerable<string> settingKeys, IEnumerable<string> secretKeys, ProviderMode mode)
    {
        var reglages = string.Join(", ", settingKeys.OrderBy(k => k, StringComparer.Ordinal));
        var secrets = string.Join(", ", secretKeys.OrderBy(k => k, StringComparer.Ordinal));
        return $"mode={mode}; réglages=[{reglages}]; secrets=[{secrets}]";
    }
}

internal sealed class UpdateProviderCommandHandler : ICommandHandler<UpdateProviderCommand>
{
    private readonly IProviderRepository _repository;
    private readonly IPlatformUnitOfWork _unitOfWork;
    private readonly ISecretProtector _protector;
    private readonly IProviderCacheInvalidator _cache;

    public UpdateProviderCommandHandler(
        IProviderRepository repository, IPlatformUnitOfWork unitOfWork,
        ISecretProtector protector, IProviderCacheInvalidator cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _protector = protector;
        _cache = cache;
    }

    public async Task<Result> Handle(UpdateProviderCommand command, CancellationToken cancellationToken)
    {
        var provider = await _repository.GetAsync(new ProviderId(command.ProviderId), cancellationToken);
        if (provider is null)
        {
            return Result.Failure(Error.NotFound("platform.provider.not_found", "Fournisseur introuvable."));
        }

        if (!Enum.TryParse<ProviderMode>(command.Mode, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
        {
            return Result.Failure(Error.Validation("platform.mode_invalid", "Mode inconnu."));
        }

        var aRemplacer = RegisterProviderCommandHandler.Protect(command.Secrets, _protector);
        var now = DateTime.UtcNow;

        var result = provider.UpdateConfiguration(command.Settings, aRemplacer, mode, command.ActorUserId, now);

        // L'agrégat peut avoir DÉSACTIVÉ le fournisseur tout en renvoyant un échec
        // (configuration devenue incomplète). On enregistre donc dans les deux cas,
        // sinon la désactivation serait perdue et l'écran continuerait d'afficher
        // « actif ».
        _repository.AppendLog(ProviderChangeLog.For(provider, ProviderChangeAction.Updated,
            RegisterProviderCommandHandler.Describe(command.Settings.Keys, aRemplacer.Keys, mode)
            + (result.IsFailure ? $"; refus={result.Error.Code}" : string.Empty),
            command.ActorUserId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.InvalidateAsync(provider.Channel, cancellationToken);
        return result;
    }
}

internal sealed class EnableProviderCommandHandler : ICommandHandler<EnableProviderCommand>
{
    private readonly IProviderRepository _repository;
    private readonly IPlatformUnitOfWork _unitOfWork;
    private readonly IProviderCacheInvalidator _cache;

    public EnableProviderCommandHandler(
        IProviderRepository repository, IPlatformUnitOfWork unitOfWork, IProviderCacheInvalidator cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result> Handle(EnableProviderCommand command, CancellationToken cancellationToken)
    {
        var provider = await _repository.GetAsync(new ProviderId(command.ProviderId), cancellationToken);
        if (provider is null)
        {
            return Result.Failure(Error.NotFound("platform.provider.not_found", "Fournisseur introuvable."));
        }

        var now = DateTime.UtcNow;
        var result = provider.Enable(command.ActorUserId, now);
        if (result.IsFailure)
        {
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // HORS PAIEMENT, ACTIVER C'EST REMPLACER, et le basculement est automatique.
        //
        // Deux fournisseurs d'e-mail actifs enverraient chaque message DEUX FOIS.
        // Refuser l'activation jusqu'à désactivation manuelle de l'autre laisserait
        // la plateforme SANS e-mail entre les deux gestes — un vendeur qui change de
        // fournisseur à midi perdrait les messages de l'intervalle.
        //
        // Le remplacement est journalisé pour chaque fournisseur écarté : personne ne
        // doit découvrir par hasard que son SMS est passé chez un autre opérateur.
        // ─────────────────────────────────────────────────────────────────────────
        if (!ChannelRules.AllowsMultipleEnabled(provider.Channel))
        {
            var autres = await _repository.ListEnabledAsync(provider.Channel, cancellationToken);
            foreach (var autre in autres.Where(p => p.Id != provider.Id))
            {
                autre.Disable(command.ActorUserId, now);
                _repository.AppendLog(ProviderChangeLog.For(autre, ProviderChangeAction.Disabled,
                    $"remplacé par {provider.Code}", command.ActorUserId, now));
            }
        }

        _repository.AppendLog(ProviderChangeLog.For(provider, ProviderChangeAction.Enabled,
            $"mode={provider.Mode}", command.ActorUserId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.InvalidateAsync(provider.Channel, cancellationToken);
        return Result.Success();
    }
}

internal sealed class DisableProviderCommandHandler : ICommandHandler<DisableProviderCommand>
{
    private readonly IProviderRepository _repository;
    private readonly IPlatformUnitOfWork _unitOfWork;
    private readonly IProviderCacheInvalidator _cache;

    public DisableProviderCommandHandler(
        IProviderRepository repository, IPlatformUnitOfWork unitOfWork, IProviderCacheInvalidator cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result> Handle(DisableProviderCommand command, CancellationToken cancellationToken)
    {
        var provider = await _repository.GetAsync(new ProviderId(command.ProviderId), cancellationToken);
        if (provider is null)
        {
            return Result.Failure(Error.NotFound("platform.provider.not_found", "Fournisseur introuvable."));
        }

        var now = DateTime.UtcNow;
        provider.Disable(command.ActorUserId, now);
        _repository.AppendLog(ProviderChangeLog.For(provider, ProviderChangeAction.Disabled,
            "désactivé depuis la console", command.ActorUserId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.InvalidateAsync(provider.Channel, cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetProviderPriorityCommandHandler : ICommandHandler<SetProviderPriorityCommand>
{
    private readonly IProviderRepository _repository;
    private readonly IPlatformUnitOfWork _unitOfWork;
    private readonly IProviderCacheInvalidator _cache;

    public SetProviderPriorityCommandHandler(
        IProviderRepository repository, IPlatformUnitOfWork unitOfWork, IProviderCacheInvalidator cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result> Handle(SetProviderPriorityCommand command, CancellationToken cancellationToken)
    {
        var provider = await _repository.GetAsync(new ProviderId(command.ProviderId), cancellationToken);
        if (provider is null)
        {
            return Result.Failure(Error.NotFound("platform.provider.not_found", "Fournisseur introuvable."));
        }

        var now = DateTime.UtcNow;
        var result = provider.SetPriority(command.Priority, command.ActorUserId, now);
        if (result.IsFailure)
        {
            return result;
        }

        _repository.AppendLog(ProviderChangeLog.For(provider, ProviderChangeAction.PriorityChanged,
            $"priorité={command.Priority}", command.ActorUserId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.InvalidateAsync(provider.Channel, cancellationToken);
        return Result.Success();
    }
}

internal sealed class DeleteProviderCommandHandler : ICommandHandler<DeleteProviderCommand>
{
    private readonly IProviderRepository _repository;
    private readonly IPlatformUnitOfWork _unitOfWork;
    private readonly IProviderCacheInvalidator _cache;

    public DeleteProviderCommandHandler(
        IProviderRepository repository, IPlatformUnitOfWork unitOfWork, IProviderCacheInvalidator cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result> Handle(DeleteProviderCommand command, CancellationToken cancellationToken)
    {
        var provider = await _repository.GetAsync(new ProviderId(command.ProviderId), cancellationToken);
        if (provider is null)
        {
            return Result.Failure(Error.NotFound("platform.provider.not_found", "Fournisseur introuvable."));
        }

        // Supprimer un fournisseur ACTIF couperait le canal en un clic, sans que
        // l'écran ait jamais dit qu'il était le seul en service. On exige la
        // désactivation d'abord : deux gestes, dont le premier est réversible.
        if (provider.IsEnabled)
        {
            return Result.Failure(Error.Conflict("platform.provider_enabled",
                "Désactivez le fournisseur avant de le supprimer."));
        }

        var now = DateTime.UtcNow;
        _repository.AppendLog(ProviderChangeLog.For(provider, ProviderChangeAction.Deleted,
            "supprimé depuis la console", command.ActorUserId, now));
        _repository.Remove(provider);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.InvalidateAsync(provider.Channel, cancellationToken);
        return Result.Success();
    }
}
