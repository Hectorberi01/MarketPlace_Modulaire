using Marketplace.Modules.Notifications.Application.Abstractions;

namespace Marketplace.Modules.Notifications.Infrastructure.Push;

/// <summary>Adaptateur « no-op » quand FCM n'est pas configuré : aucun push envoyé.</summary>
public sealed class NullPushSender : IPushSender
{
    public Task<PushSendResult> SendAsync(
        IReadOnlyCollection<string> tokens, PushMessage message, CancellationToken cancellationToken = default)
        => Task.FromResult(new PushSendResult(Array.Empty<string>()));
}
