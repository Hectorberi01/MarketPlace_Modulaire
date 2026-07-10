namespace Marketplace.Modules.Notifications.Application.Abstractions;

/// <summary>Contenu d'une notification push.</summary>
public sealed record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string>? Data = null);

/// <summary>Résultat d'un envoi : jetons devenus invalides (à purger).</summary>
public sealed record PushSendResult(IReadOnlyList<string> InvalidTokens);

/// <summary>
/// Port d'envoi de notifications push. Implémenté en Infrastructure via FCM
/// (Firebase). Un adaptateur « no-op » est utilisé quand FCM n'est pas configuré.
/// </summary>
public interface IPushSender
{
    Task<PushSendResult> SendAsync(
        IReadOnlyCollection<string> tokens, PushMessage message, CancellationToken cancellationToken = default);
}
