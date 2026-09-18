using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Marketplace.Modules.Notifications.Application.Abstractions;

namespace Marketplace.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Envoi d'e-mails via l'API HTTP de Resend.
///
/// Resend plutôt qu'un SMTP : même fournisseur que le site vitrine (un seul domaine à
/// authentifier en SPF/DKIM, une seule facture), une simple requête HTTPS, et aucune
/// dépendance SMTP à maintenir.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    public const string HttpClientName = "resend";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        IHttpClientFactory httpClientFactory,
        EmailOptions options,
        ILogger<ResendEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var response = await client.PostAsJsonAsync(
            "https://api.resend.com/emails",
            new
            {
                from = _options.From,
                to = new[] { message.To },
                subject = message.Subject,
                html = message.HtmlBody,
                text = message.TextBody,
            },
            cancellationToken);

        // PostAsJsonAsync NE LÈVE PAS sur 4xx/5xx. Sans ce contrôle explicite, un 403
        // « domaine non vérifié » ou un 401 « clé invalide » passerait pour un succès :
        // l'outbox marquerait le message traité, et l'e-mail serait perdu SANS TRACE.
        // L'utilisateur, lui, attendrait indéfiniment un lien qui n'est jamais parti.
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // On journalise le DESTINATAIRE et le sujet, jamais le corps : il contient le
            // jeton de réinitialisation en clair. Un jeton dans les logs, c'est un jeton
            // lisible par quiconque a accès aux logs.
            _logger.LogError(
                "Échec d'envoi d'e-mail à {To} (sujet « {Subject} ») : Resend a répondu {Status}. Réponse : {Body}",
                message.To, message.Subject, (int)response.StatusCode, body);

            // On lève : l'OutboxProcessor laissera le message non traité et le rejouera.
            // Un e-mail de réinitialisation perdu, c'est un utilisateur enfermé dehors.
            throw new InvalidOperationException(
                $"Resend a refusé l'envoi ({(int)response.StatusCode}). L'e-mail sera rejoué par l'outbox.");
        }

        _logger.LogInformation("E-mail « {Subject} » envoyé à {To}.", message.Subject, message.To);
    }
}
