using FirebaseAdmin.Messaging;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Marketplace.Modules.Notifications.Infrastructure.Push;

/// <summary>
/// Envoi de notifications push via Firebase Cloud Messaging (SDK FirebaseAdmin,
/// API HTTP v1). Envoie par lots (max 500 jetons) et remonte les jetons devenus
/// invalides (app désinstallée / jeton périmé) pour purge.
/// </summary>
public sealed class FcmPushSender : IPushSender
{
    private const int BatchSize = 500;

    private readonly FirebaseMessaging _messaging;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(FirebaseMessaging messaging, ILogger<FcmPushSender> logger)
    {
        _messaging = messaging;
        _logger = logger;
    }

    public async Task<PushSendResult> SendAsync(
        IReadOnlyCollection<string> tokens, PushMessage message, CancellationToken cancellationToken = default)
    {
        var all = tokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        if (all.Count == 0)
        {
            return new PushSendResult(Array.Empty<string>());
        }

        var invalid = new List<string>();
        var data = message.Data?.ToDictionary(kv => kv.Key, kv => kv.Value);

        for (var i = 0; i < all.Count; i += BatchSize)
        {
            var batch = all.Skip(i).Take(BatchSize).ToList();
            var multicast = new MulticastMessage
            {
                Tokens = batch,
                Notification = new Notification { Title = message.Title, Body = message.Body },
                Data = data,
            };

            try
            {
                var response = await _messaging.SendEachForMulticastAsync(multicast, cancellationToken);
                _logger.LogInformation(
                    "FCM : lot de {Count} jetons — {Success} succès, {Failure} échecs.",
                    batch.Count, response.SuccessCount, response.FailureCount);

                for (var j = 0; j < response.Responses.Count; j++)
                {
                    var r = response.Responses[j];
                    if (r.IsSuccess)
                    {
                        continue;
                    }

                    // DIAGNOSTIC : code d'erreur exact par jeton. Les plus fréquents :
                    //  - ThirdPartyAuthError : clé APNs (.p8) absente/incorrecte dans Firebase.
                    //  - Unregistered / InvalidArgument : jeton périmé ou invalide (purge).
                    //  - SenderIdMismatch : le jeton appartient à un autre projet Firebase.
                    var code = (r.Exception as FirebaseMessagingException)?.MessagingErrorCode;
                    _logger.LogWarning(
                        "FCM : échec jeton #{Index} — code={Code} : {Message}",
                        j, code, r.Exception?.Message);

                    if (r.Exception is FirebaseMessagingException fme &&
                        (fme.MessagingErrorCode == MessagingErrorCode.Unregistered
                         || fme.MessagingErrorCode == MessagingErrorCode.InvalidArgument
                         || fme.MessagingErrorCode == MessagingErrorCode.SenderIdMismatch))
                    {
                        invalid.Add(batch[j]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM : échec d'envoi d'un lot de {Count} jetons.", batch.Count);
            }
        }

        return new PushSendResult(invalid);
    }
}
