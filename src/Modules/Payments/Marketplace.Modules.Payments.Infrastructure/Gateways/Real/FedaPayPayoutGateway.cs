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
        // FedaPay attend le code pays en minuscules (ex. « bj » pour le Bénin).
        var country = (string.IsNullOrWhiteSpace(instruction.Beneficiary.CountryCode)
            ? "bj" : instruction.Beneficiary.CountryCode).ToLowerInvariant();
        // FedaPay exige le numéro au format international E.164 avec « + »
        // (ex. « +22997808080 »). Un numéro sans « + » empêche la détection de
        // l'opérateur (MTN/Moov) et fait échouer la création en 500 côté FedaPay.
        var number = ToE164(instruction.Beneficiary.Msisdn, country);
        // Méthode de transfert FedaPay dérivée de l'opérateur RÉEL du vendeur
        // (tous n'utilisent pas MTN). Repli sur le mode configuré si inconnu.
        var mode = ResolveMode(instruction.Beneficiary.Provider);

        // 1. Création du payout. On envoie « mode » (méthode de transfert, ex.
        // mtn_open) tel que documenté par FedaPay v1 : le sandbox l'attend pour
        // router le versement Mobile Money. (Attention : « mobile_money » n'est PAS
        // une valeur valide — c'est ce qui faisait échouer la création en 404.)
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "payouts")
        {
            Content = JsonContent.Create(new
            {
                amount = (long)Math.Round(instruction.Amount),
                currency = new { iso = instruction.Currency },
                // Méthode de transfert Mobile Money, propre à l'opérateur du vendeur
                // (mtn_open / moov / sbin…). Le sandbox FedaPay v1 attend ce champ.
                mode,
                description = "Reversement marchand HbaExpress",
                customer = new
                {
                    firstname = firstName,
                    lastname = lastName,
                    // Email stable par bénéficiaire (basé sur le numéro) : FedaPay
                    // identifie/retrouve le client par email. Domaine que nous
                    // contrôlons pour éviter tout email tiers réel.
                    email = BuildEmail(number),
                    phone_number = new { number, country }
                },
                merchant_reference = instruction.Reference
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

        // 2. Démarrage du payout : corps = { "payouts": [ { id } ] }.
        // Un simple { id } déclenche l'envoi immédiat vers le numéro enregistré sur
        // le payout à la création — inutile de re-transmettre le numéro (et d'exposer
        // un second point d'échec de format).
        using var startRequest = new HttpRequestMessage(HttpMethod.Put, "payouts/start")
        {
            Content = JsonContent.Create(new
            {
                payouts = new object[]
                {
                    new { id = payoutId }
                }
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

    /// <summary>
    /// Met le numéro au format international E.164 attendu par FedaPay (« +229XXXXXXXX »).
    /// Retire tout caractère non numérique, puis préfixe l'indicatif pays s'il manque.
    /// </summary>
    private static string ToE164(string? msisdn, string country)
    {
        var digits = new string((msisdn ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return string.Empty;
        }

        var callingCode = country switch
        {
            "bj" => "229", // Bénin
            "ci" => "225", // Côte d'Ivoire
            "tg" => "228", // Togo
            "sn" => "221", // Sénégal
            "ne" => "227", // Niger
            "bf" => "226", // Burkina Faso
            "ml" => "223", // Mali
            _ => "229"
        };

        return digits.StartsWith(callingCode) ? "+" + digits : "+" + callingCode + digits;
    }

    /// <summary>
    /// Traduit l'opérateur Mobile Money du vendeur en « mode » de transfert FedaPay.
    /// Bénin : MTN → mtn_open, Moov → moov, Celtis → sbin. Pour tout opérateur non
    /// mappé (ex. Wave), on retombe sur le mode configuré (<see cref="FedaPayOptions.PayoutMode"/>).
    /// </summary>
    private string ResolveMode(string? provider) => (provider ?? string.Empty).ToLowerInvariant() switch
    {
        "mtnmomo" or "mtn" or "mtn_open" => "mtn_open", // MTN Bénin
        "moovmoney" or "moov" => "moov",                // Moov Bénin
        "celtis" or "sbin" => "sbin",                   // Celtis Bénin
        _ => _options.PayoutMode
    };

    /// <summary>
    /// Email de bénéficiaire stable, dérivé du numéro E.164. FedaPay se sert de
    /// l'email comme clé d'unicité du client ; un email stable par numéro évite
    /// de créer un nouveau client à chaque versement. Domaine maîtrisé (pas d'email tiers).
    /// </summary>
    private static string BuildEmail(string e164)
    {
        var digits = new string(e164.Where(char.IsDigit).ToArray());
        return $"versement-{digits}@payouts.hba-marketplace.fr";
    }

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
