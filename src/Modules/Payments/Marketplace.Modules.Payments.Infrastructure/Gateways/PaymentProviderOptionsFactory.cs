using Microsoft.Extensions.Configuration;
using Marketplace.Modules.Platform.Contracts;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways;

/// <summary>
/// Fabrique les options typées d'un PSP, depuis le registre des fournisseurs ou, à
/// défaut, depuis la configuration.
///
/// POURQUOI UN REPLI SUR LA CONFIGURATION. Le jour où ce code part en production, le
/// registre est VIDE : personne n'a encore saisi les clés dans la console. Sans
/// repli, le déploiement couperait les encaissements jusqu'à ce qu'un administrateur
/// retape cinq jeux de secrets — une panne de paiement provoquée par une migration
/// de confort. Le repli rend la bascule progressive : un PSP saisi dans la console
/// prime, les autres continuent de lire le vault.
///
/// Les noms de clés du registre sont EXACTEMENT ceux des options
/// (« Payments:Stripe:ApiKey » → secret « ApiKey ») : voir ProviderCatalog.
///
/// Les fonctions de liaison depuis la configuration restent dans l'installer, qui en
/// a besoin pour ses gardes de démarrage. On les appelle plutôt que de les recopier.
/// </summary>
internal static class PaymentProviderOptionsFactory
{
    public static StripeOptions Stripe(ProviderConfiguration? registre, IConfiguration configuration)
    {
        if (registre is null)
        {
            return PaymentsModuleInstaller.BindStripeOptions(configuration);
        }

        var o = new StripeOptions();
        Appliquer(registre.Secret("ApiKey"), v => o.ApiKey = v);
        Appliquer(registre.Secret("WebhookSecret"), v => o.WebhookSecret = v);
        Appliquer(registre.Setting("BaseUrl"), v => o.BaseUrl = v);
        Appliquer(registre.Setting("SuccessUrl"), v => o.SuccessUrl = v);
        Appliquer(registre.Setting("CancelUrl"), v => o.CancelUrl = v);
        Appliquer(registre.Setting("CheckoutBaseUrl"), v => o.CheckoutBaseUrl = v);
        return o;
    }

    public static PayPalOptions PayPal(ProviderConfiguration? registre, IConfiguration configuration)
    {
        if (registre is null)
        {
            return PaymentsModuleInstaller.BindPayPalOptions(configuration);
        }

        var o = new PayPalOptions();
        Appliquer(registre.Setting("ClientId"), v => o.ClientId = v);
        Appliquer(registre.Secret("Secret"), v => o.Secret = v);
        Appliquer(registre.Setting("WebhookId"), v => o.WebhookId = v);
        Appliquer(registre.Secret("WebhookSecret"), v => o.WebhookSecret = v);
        Appliquer(registre.Setting("BaseUrl"), v => o.BaseUrl = v);
        Appliquer(registre.Setting("ReturnUrl"), v => o.ReturnUrl = v);
        Appliquer(registre.Setting("CancelUrl"), v => o.CancelUrl = v);
        Appliquer(registre.Setting("CheckoutBaseUrl"), v => o.CheckoutBaseUrl = v);
        return o;
    }

    public static MtnMomoOptions MtnMomo(ProviderConfiguration? registre, IConfiguration configuration)
    {
        if (registre is null)
        {
            return PaymentsModuleInstaller.BindMtnMomoOptions(configuration);
        }

        var o = new MtnMomoOptions();
        Appliquer(registre.Secret("SubscriptionKey"), v => o.SubscriptionKey = v);
        Appliquer(registre.Setting("ApiUser"), v => o.ApiUser = v);
        Appliquer(registre.Secret("ApiKey"), v => o.ApiKey = v);
        Appliquer(registre.Secret("WebhookSecret"), v => o.WebhookSecret = v);
        Appliquer(registre.Setting("BaseUrl"), v => o.BaseUrl = v);
        Appliquer(registre.Setting("TargetEnvironment"), v => o.TargetEnvironment = v);
        Appliquer(registre.Setting("CallbackUrl"), v => o.CallbackUrl = v);
        Appliquer(registre.Setting("Currency"), v => o.Currency = v);
        return o;
    }

    public static MoovOptions Moov(ProviderConfiguration? registre, IConfiguration configuration)
    {
        if (registre is null)
        {
            return PaymentsModuleInstaller.BindMoovOptions(configuration);
        }

        var o = new MoovOptions();
        Appliquer(registre.Setting("MerchantId"), v => o.MerchantId = v);
        Appliquer(registre.Secret("ApiKey"), v => o.ApiKey = v);
        Appliquer(registre.Secret("WebhookSecret"), v => o.WebhookSecret = v);
        Appliquer(registre.Setting("BaseUrl"), v => o.BaseUrl = v);
        Appliquer(registre.Setting("TokenPath"), v => o.TokenPath = v);
        Appliquer(registre.Setting("PaymentPath"), v => o.PaymentPath = v);
        Appliquer(registre.Setting("CallbackUrl"), v => o.CallbackUrl = v);
        Appliquer(registre.Setting("Currency"), v => o.Currency = v);
        return o;
    }

    public static FedaPayOptions FedaPay(ProviderConfiguration? registre, IConfiguration configuration)
    {
        if (registre is null)
        {
            return PaymentsModuleInstaller.BindFedaPayOptions(configuration);
        }

        var o = new FedaPayOptions();
        Appliquer(registre.Secret("ApiKey"), v => o.ApiKey = v);
        Appliquer(registre.Secret("WebhookSecret"), v => o.WebhookSecret = v);
        Appliquer(registre.Setting("BaseUrl"), v => o.BaseUrl = v);
        Appliquer(registre.Setting("CallbackUrl"), v => o.CallbackUrl = v);
        Appliquer(registre.Setting("Currency"), v => o.Currency = v);
        Appliquer(registre.Setting("PayoutMode"), v => o.PayoutMode = v);

        // Les versements réels restent un OPT-IN explicite, même depuis la console :
        // en bac à sable FedaPay refuse les dépôts Mobile Money, et une file de
        // retraits en échec laisse un vendeur persuadé qu'on lui doit de l'argent.
        o.EnablePayouts = string.Equals(registre.Setting("EnablePayouts"), "true", StringComparison.OrdinalIgnoreCase);

        return o;
    }

    public static KkiapayOptions Kkiapay(ProviderConfiguration? registre, IConfiguration configuration)
    {
        if (registre is null)
        {
            return PaymentsModuleInstaller.BindKkiapayOptions(configuration);
        }

        var o = new KkiapayOptions();
        Appliquer(registre.Setting("PublicKey"), v => o.PublicKey = v);
        Appliquer(registre.Secret("WebhookSecret"), v => o.WebhookSecret = v);
        Appliquer(registre.Setting("Currency"), v => o.Currency = v);
        Appliquer(registre.Setting("CallbackUrl"), v => o.CallbackUrl = v);

        // Le bac à sable reste ACTIF tant que la console n'a pas écrit « false » :
        // un réglage absent ne doit pas faire basculer en encaissement réel.
        o.Sandbox = !string.Equals(registre.Setting("Sandbox"), "false", StringComparison.OrdinalIgnoreCase);

        return o;
    }

    /// <summary>
    /// N'écrase une valeur par défaut que si le registre en propose une. Une URL de
    /// base vide en base ne doit pas effacer « https://api.stripe.com » : elle
    /// signifie « non renseigné », pas « vide ».
    /// </summary>
    private static void Appliquer(string valeur, Action<string> affectation)
    {
        if (!string.IsNullOrWhiteSpace(valeur))
        {
            affectation(valeur);
        }
    }
}
