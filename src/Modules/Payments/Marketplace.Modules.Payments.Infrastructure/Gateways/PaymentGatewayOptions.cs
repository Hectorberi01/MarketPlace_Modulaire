namespace Marketplace.Modules.Payments.Infrastructure.Gateways;

/// <summary>
/// Réglages Stripe (section de config « Payments:Stripe »). Les clés réelles
/// sont des placeholders en sandbox : à remplacer par des secrets (env / coffre).
/// </summary>
public sealed class StripeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Base de l'API Stripe (réel).</summary>
    public string BaseUrl { get; set; } = "https://api.stripe.com";

    /// <summary>URL de retour après paiement (Checkout) — succès / annulation.</summary>
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;

    /// <summary>Base d'URL simulant la page de paiement hébergée (stub).</summary>
    public string CheckoutBaseUrl { get; set; } = "https://checkout.stripe.com/pay";

    /// <summary>Vrai si la clé secrète est renseignée (sinon on reste en stub).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Réglages PayPal (section de config « Payments:PayPal »). Idem : placeholders
/// en sandbox, à remplacer par des secrets réels avant tout déploiement.
/// </summary>
public sealed class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string WebhookId { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Base de l'API PayPal (réel ; sandbox par défaut).</summary>
    public string BaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";

    /// <summary>URL de retour après approbation / annulation.</summary>
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;

    /// <summary>Base d'URL simulant la page d'approbation PayPal (stub).</summary>
    public string CheckoutBaseUrl { get; set; } = "https://www.sandbox.paypal.com/checkoutnow";

    /// <summary>Vrai si l'identifiant client et le secret sont renseignés (sinon stub).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Secret);
}

/// <summary>
/// Réglages MTN Mobile Money (section « Payments:MtnMomo »). Le flux Collection
/// repose sur RequestToPay : l'acheteur approuve sur son téléphone, puis le PSP
/// notifie par callback (ou on interroge le statut). Placeholders en sandbox.
/// </summary>
public sealed class MtnMomoOptions
{
    public string SubscriptionKey { get; set; } = string.Empty;
    public string ApiUser { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Base de l'API Collection (sandbox : https://sandbox.momodeveloper.mtn.com).</summary>
    public string BaseUrl { get; set; } = "https://sandbox.momodeveloper.mtn.com";

    /// <summary>Environnement cible MoMo (« sandbox », ou « mtnbenin »… en prod).</summary>
    public string TargetEnvironment { get; set; } = "sandbox";

    /// <summary>URL de callback où MoMo notifie le statut (X-Callback-Url), optionnelle.</summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>Devise envoyée au PSP (UEMOA : XOF ; le sandbox MoMo accepte aussi EUR).</summary>
    public string Currency { get; set; } = "XOF";

    /// <summary>Vrai si les identifiants requis sont renseignés (sinon on reste en stub).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SubscriptionKey)
        && !string.IsNullOrWhiteSpace(ApiUser)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Réglages FedaPay (section « Payments:FedaPay »). FedaPay agrège Mobile Money
/// (MTN, Moov…) et carte derrière une page de paiement hébergée : on crée une
/// transaction puis on génère un lien (token) vers lequel rediriger l'acheteur.
/// La confirmation arrive par webhook signé (x-fedapay-signature) ou par
/// interrogation du statut. Placeholders en sandbox.
/// </summary>
public sealed class FedaPayOptions
{
    /// <summary>Clé secrète FedaPay (sk_sandbox_… / sk_live_…), envoyée en Bearer.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Secret de signature des webhooks FedaPay (vérif. HMAC du corps brut).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Base de l'API FedaPay (sandbox par défaut ; live : https://api.fedapay.com/v1).</summary>
    public string BaseUrl { get; set; } = "https://sandbox-api.fedapay.com/v1";

    /// <summary>URL de callback où FedaPay notifie / renvoie l'acheteur après paiement.</summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>Devise envoyée (UEMOA : XOF).</summary>
    public string Currency { get; set; } = "XOF";

    /// <summary>Vrai si la clé secrète est renseignée (sinon on reste en stub).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Réglages Moov Money (section « Payments:Moov »). Même logique RequestToPay /
/// callback. Placeholders en sandbox, à remplacer par des secrets réels.
/// </summary>
public sealed class MoovOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string Currency { get; set; } = "XOF";

    /// <summary>Base de l'API Moov (à aligner sur ton contrat / pays).</summary>
    public string BaseUrl { get; set; } = "https://api.moov-africa.com";

    /// <summary>Chemin du jeton OAuth (client_credentials), relatif à BaseUrl.</summary>
    public string TokenPath { get; set; } = "oauth/token";

    /// <summary>Chemin de création de paiement (RequestToPay), relatif à BaseUrl.</summary>
    public string PaymentPath { get; set; } = "v1/payments";

    /// <summary>URL de callback où Moov notifie le statut, optionnelle.</summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>Vrai si les identifiants requis sont renseignés (sinon on reste en stub).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(MerchantId) && !string.IsNullOrWhiteSpace(ApiKey);
}
