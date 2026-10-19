using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Platform.Domain.Providers;

/// <summary>Nature d'un changement journalisé.</summary>
public enum ProviderChangeAction
{
    Created = 0,
    Updated = 1,
    Enabled = 2,
    Disabled = 3,
    PriorityChanged = 4,
    Deleted = 5,
    Tested = 6
}

/// <summary>
/// Trace append-only d'un changement de configuration.
///
/// AUCUNE VALEUR DE SECRET N'ENTRE ICI, seulement les NOMS des clés touchées. Un
/// journal qui recopierait la clé d'API la rendrait lisible par quiconque consulte
/// l'historique — et la sortirait du périmètre chiffré pour de bon.
/// </summary>
public sealed class ProviderChangeLog : Entity<Guid>
{
    private ProviderChangeLog()
    {
    }

    private ProviderChangeLog(Guid id, ProviderId providerId, ProviderChannel channel, string code,
        ProviderChangeAction action, string detail, Guid actorId, DateTime atUtc)
        : base(id)
    {
        ProviderId = providerId;
        Channel = channel;
        Code = code;
        Action = action;
        Detail = detail;
        ActorUserId = actorId;
        AtUtc = atUtc;
    }

    public ProviderId ProviderId { get; private set; }
    public ProviderChannel Channel { get; private set; }
    public string Code { get; private set; } = default!;
    public ProviderChangeAction Action { get; private set; }

    /// <summary>Description lisible : clés modifiées, mode, résultat d'un test.</summary>
    public string Detail { get; private set; } = string.Empty;

    public Guid ActorUserId { get; private set; }
    public DateTime AtUtc { get; private set; }

    public static ProviderChangeLog For(Provider provider, ProviderChangeAction action, string detail,
        Guid actorId, DateTime nowUtc)
        => new(Guid.NewGuid(), provider.Id, provider.Channel, provider.Code, action,
            detail.Length > 500 ? detail[..500] : detail, actorId, nowUtc);
}
