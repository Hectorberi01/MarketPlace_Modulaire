using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

/// <summary>
/// Reversement (dépôt) RÉEL via FedaPay. Flux en deux temps :
///  1. POST /payouts → crée le payout (montant, devise, bénéficiaire Mobile Money).
///  2. PUT /payouts/start → déclenche l'envoi (statut « sent »).
/// Réutilise le client HTTP et la clé configurés pour FedaPay.
/// </summary>
public sealed class FedaPayPayoutGateway : IPayoutGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FedaPayOptions _options;

    public FedaPayPayoutGateway(IHttpClientFactory httpClientFactory, FedaPayOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<PayoutResult> SendAsync(PayoutInstruction instruction, CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendInternalAsync(instruction, cancellationToken);
        }
        catch (Exception ex)
        {
            // On ne propage pas : un échec laisse les gains « Released » (réessai
            // par le lot), évitant qu'un retry d'event ne provoque un double-paiement.
            return new PayoutResult(false, null, $"FedaPay payout : {ex.Message}");
        }
    }

    private async Task<PayoutResult> SendInternalAsync(PayoutInstruction instruction, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(FedaPayHttpGateway.ClientName);
        var (firstName, lastName) = SplitName(instruction.Beneficiary.Name);
        var number = NormalizeMsisdn(instruction.Beneficiary.Msisdn);
        var country = string.IsNullOrWhiteSpace(instruction.Beneficiary.CountryCode) ? "BJ" : instruction.Beneficiary.CountryCode;

        // 1. Création du payout.
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "payouts")
        {
            Content = JsonContent.Create(new
            {
                amount = (long)Math.Round(instruction.Amount),
                currency = new { iso = instruction.Currency },
                mode = "mobile_money",
                customer = new
                {
                    firstname = firstName,
                    lastname = lastName,
                    phone_number = new { number, country }
                },
                custom_metadata = new { reference = instruction.Reference }
            })
        };
        Authorize(createRequest);

        var createResponse = await client.SendAsync(createRequest, cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            return Fail("création", createResponse, await createResponse.Content.ReadAsStringAsync(cancellationToken));
        }

        var payoutId = ExtractPayoutId(await createResponse.Content.ReadAsStringAsync(cancellationToken));
        if (payoutId is null)
        {
            return new PayoutResult(false, null, "FedaPay : identifiant de payout absent de la réponse.");
        }

        // 2. Démarrage du payout (corps = tableau d'objets { id, phone_number }).
        using var startRequest = new HttpRequestMessage(HttpMethod.Put, "payouts/start")
        {
            Content = JsonContent.Create(new object[]
            {
                new { id = payoutId, phone_number = new { number, country } }
            })
        };
        Authorize(startRequest);

        var startResponse = await client.SendAsync(startRequest, cancellationToken);
        if (!startResponse.IsSuccessStatusCode)
        {
            return Fail("démarrage", startResponse, await startResponse.Content.ReadAsStringAsync(cancellationToken));
        }

        return new PayoutResult(true, payoutId.ToString(), null);
    }

    private void Authorize(HttpRequestMessage request)
        => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

    private static PayoutResult Fail(string step, HttpResponseMessage response, string body)
        => new(false, null, $"FedaPay — échec {step} payout ({(int)response.StatusCode}) : {body}");

    private static (string First, string Last) SplitName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return ("Vendeur", "Marketplace");
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], parts[0]);
    }

    private static string NormalizeMsisdn(string? msisdn)
        => new string((msisdn ?? string.Empty).Where(char.IsDigit).ToArray());

    private static long? ExtractPayoutId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var payout = root.TryGetProperty("v1/payout", out var wrapped) ? wrapped : root;
        if (payout.TryGetProperty("id", out var id))
        {
            return id.ValueKind == JsonValueKind.Number ? id.GetInt64() : long.TryParse(id.GetString(), out var n) ? n : null;
        }

        return null;
    }
}
