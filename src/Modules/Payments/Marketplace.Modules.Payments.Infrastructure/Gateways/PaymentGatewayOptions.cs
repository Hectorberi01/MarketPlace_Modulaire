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

    /// <summary>
    /// Active les VERSEMENTS (payouts vendeur) réels via FedaPay. Désactivé par
    /// défaut : en sandbox les payouts Mobile Money ne s'exécutent pas et font
    /// échouer le retrait. Tant que ce flag est faux, on simule le versement
    /// (le retrait aboutit) même si FedaPay est configuré pour l'encaissement.
    /// </summary>
    public bool EnablePayouts { get; set; }

    /// <summary>Vrai si l'on parle à l'API bac à sable de FedaPay.</summary>
    public bool IsSandbox =>
        BaseUrl.Contains("sandbox", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Vrai si la clé et l'URL désignent le MÊME monde.
    ///
    /// Une clé <c>sk_live_…</c> envoyée à <c>sandbox-api.fedapay.com</c> est
    /// inconnue du bac à sable : FedaPay répond « 403 Opération non autorisée ».
    /// L'inverse est pire — une clé sandbox pointée sur l'API live échoue aussi,
    /// mais la même erreur de configuration appliquée à une VRAIE clé enverrait
    /// de l'argent réel là où on croyait faire un test.
    /// </summary>
    public bool KeyMatchesEnvironment
    {
        get
        {
            var keyIsSandbox = ApiKey.StartsWith("sk_sandbox", StringComparison.OrdinalIgnoreCase);
            var keyIsLive = ApiKey.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase);

            // Clé d'un format inconnu : on ne peut rien affirmer, on laisse passer.
            if (!keyIsSandbox && !keyIsLive) return true;

            return keyIsSandbox == IsSandbox;
        }
    }

    /// <summary>
    /// Les VERSEMENTS réels ne sont possibles qu'en LIVE.
    ///
    /// Le bac à sable FedaPay n'exécute pas les dépôts Mobile Money : il refuse
    /// la création (« 403 Opération non autorisée »). Activer les payouts en
    /// sandbox ne produit donc qu'une chose — une file de retraits en échec,
    /// remboursés, et un vendeur qui croit que la plateforme lui doit de l'argent.
    /// </summary>
    public bool CanPayout => IsConfigured && EnablePayouts && !IsSandbox && KeyMatchesEnvironment;

    /// <summary>
    /// Méthode de transfert FedaPay pour les payouts (champ « mode »). Valeurs
    /// valides : mtn_open (MTN Bénin), moov (Moov Bénin), mtn_ci, moov_tg,
    /// togocel, sbin, moov_ci, wave_sn, orange_sn… Défaut : mtn_open (le compte
    /// de versement de la boutique est un numéro MTN Mobile Money Bénin).
    /// </summary>
    public string PayoutMode { get; set; } = "mtn_open";

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

/// <summary>
/// Réglages KkiaPay (section « Payments:Kkiapay »). KkiaPay ne publie PAS d'API
/// REST : l'acheteur paie dans un widget JavaScript, et le serveur n'apprend le
/// résultat que par rappel. D'où l'absence de BaseUrl — il n'y a rien à appeler.
/// </summary>
public sealed class KkiapayOptions
{
    /// <summary>Clé publique, transmise au navigateur dans les paramètres du widget.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Secret du tableau de bord KkiaPay, renvoyé TEL QUEL dans l'en-tête
    /// x-kkiapay-secret. Ce n'est pas une signature HMAC du corps : on le compare.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Devise du compte marchand (UEMOA : XOF).</summary>
    public string Currency { get; set; } = "XOF";

    /// <summary>
    /// Widget en bac à sable. VRAI PAR DÉFAUT : en sandbox aucun argent ne bouge,
    /// alors que le rappel de succès arrive quand même. Le défaut inverse ferait
    /// d'un oubli de configuration un encaissement fictif réputé réussi.
    /// </summary>
    public bool Sandbox { get; set; } = true;

    /// <summary>URL où renvoyer l'acheteur après le widget, optionnelle.</summary>
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>Vrai si la clé publique est renseignée (sinon on reste en stub).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey);

    /// <summary>Vrai si cette configuration peut encaisser de l'argent RÉEL.</summary>
    public bool CanCollectRealMoney => IsConfigured && !Sandbox;
}
