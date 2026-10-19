namespace Marketplace.Modules.Platform.Domain.Providers;

/// <summary>
/// Description d'un fournisseur connu : son canal, et les clés qu'il exige.
/// </summary>
/// <param name="RequiredSecrets">
/// Clés CHIFFRÉES en base et jamais relues par la console. Ce qui atterrit ici
/// détermine ce qui est protégé : une clé oubliée serait stockée en clair comme un
/// simple réglage.
/// </param>
public sealed record ProviderDescriptor(
    string Code,
    ProviderChannel Channel,
    string DisplayName,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<string> RequiredSecrets,
    IReadOnlyList<string> OptionalSettings,
    bool SupportsSandbox);

/// <summary>
/// Catalogue des fournisseurs que le code sait piloter.
///
/// IL EST STATIQUE, ET LE REGISTRE EN BASE EST DYNAMIQUE. Le catalogue dit ce qu'un
/// adaptateur existant SAIT faire — donc quelles clés il lui faut ; le registre dit
/// ce qui est configuré et actif. La console construit son formulaire à partir du
/// catalogue : ajouter un champ à un fournisseur ne demande aucun écran nouveau.
///
/// Un code absent d'ici n'est pas enregistrable : sans adaptateur, un fournisseur
/// « configuré » resterait une ligne en base que rien n'appelle.
/// </summary>
public static class ProviderCatalog
{
    public const string Stripe = "Stripe";
    public const string PayPal = "PayPal";
    public const string MtnMomo = "MtnMomo";
    public const string Moov = "Moov";
    public const string FedaPay = "FedaPay";
    public const string Kkiapay = "Kkiapay";
    public const string Resend = "Resend";
    public const string Fcm = "Fcm";
    public const string Twilio = "Twilio";
    public const string MetaCloud = "MetaCloud";
    public const string GenericHttpSms = "GenericHttpSms";

    private static readonly ProviderDescriptor[] All =
    {
        // ── Paiement. Les noms de clés reprennent EXACTEMENT ceux des options
        //    existantes (Payments:<Psp>:<Clé>) : la bascule du fichier vers la base
        //    n'est alors qu'un changement de source, pas de contrat.
        new(Stripe, ProviderChannel.Payment, "Stripe",
            RequiredSettings: new[] { "BaseUrl" },
            RequiredSecrets: new[] { "ApiKey", "WebhookSecret" },
            OptionalSettings: new[] { "SuccessUrl", "CancelUrl", "CheckoutBaseUrl" },
            SupportsSandbox: true),

        new(PayPal, ProviderChannel.Payment, "PayPal",
            RequiredSettings: new[] { "ClientId", "BaseUrl" },
            RequiredSecrets: new[] { "Secret", "WebhookSecret" },
            OptionalSettings: new[] { "WebhookId", "ReturnUrl", "CancelUrl", "CheckoutBaseUrl" },
            SupportsSandbox: true),

        new(MtnMomo, ProviderChannel.Payment, "MTN Mobile Money",
            RequiredSettings: new[] { "ApiUser", "BaseUrl", "TargetEnvironment", "Currency" },
            RequiredSecrets: new[] { "SubscriptionKey", "ApiKey", "WebhookSecret" },
            OptionalSettings: new[] { "CallbackUrl" },
            SupportsSandbox: true),

        new(Moov, ProviderChannel.Payment, "Moov Africa",
            RequiredSettings: new[] { "MerchantId", "BaseUrl", "Currency" },
            RequiredSecrets: new[] { "ApiKey", "WebhookSecret" },
            OptionalSettings: new[] { "TokenPath", "PaymentPath", "CallbackUrl" },
            SupportsSandbox: true),

        // EnablePayouts et PayoutMode pilotent les VERSEMENTS vendeur, pas les
        // encaissements. Ils restent un opt-in : en bac à sable, FedaPay refuse les
        // dépôts Mobile Money (403), et une file de retraits en échec laisse un
        // vendeur persuadé que la plateforme lui doit de l'argent.
        new(FedaPay, ProviderChannel.Payment, "FedaPay",
            RequiredSettings: new[] { "BaseUrl", "Currency" },
            RequiredSecrets: new[] { "ApiKey", "WebhookSecret" },
            OptionalSettings: new[] { "CallbackUrl", "EnablePayouts", "PayoutMode" },
            SupportsSandbox: true),

        // KkiaPay n'a pas d'API REST : l'encaissement passe par son widget, d'où
        // l'absence de BaseUrl et de clé secrète d'appel. La clé publique n'est PAS
        // un secret — elle part dans le navigateur.
        //
        // « Sandbox » décide si l'argent est réel : laissé vide ou « true », le widget
        // accepte des paiements fictifs et envoie quand même un rappel de succès. Le
        // résolveur refuse donc KkiaPay en bac à sable en production.
        new(Kkiapay, ProviderChannel.Payment, "KkiaPay",
            RequiredSettings: new[] { "PublicKey", "Currency" },
            RequiredSecrets: new[] { "WebhookSecret" },
            OptionalSettings: new[] { "Sandbox", "CallbackUrl" },
            SupportsSandbox: true),

        // ── E-mail.
        new(Resend, ProviderChannel.Email, "Resend",
            RequiredSettings: new[] { "From", "AppBaseUrl" },
            RequiredSecrets: new[] { "ApiKey" },
            OptionalSettings: Array.Empty<string>(),
            SupportsSandbox: false),

        // ── Push. Le compte de service peut venir d'un fichier monté
        //    (ServiceAccountPath) : dans ce cas la valeur n'est pas un secret en base.
        new(Fcm, ProviderChannel.Push, "Firebase Cloud Messaging",
            RequiredSettings: Array.Empty<string>(),
            RequiredSecrets: Array.Empty<string>(),
            OptionalSettings: new[] { "ServiceAccountPath" },
            SupportsSandbox: false),

        // ── SMS.
        new(Twilio, ProviderChannel.Sms, "Twilio SMS",
            RequiredSettings: new[] { "AccountSid", "FromNumber" },
            RequiredSecrets: new[] { "AuthToken" },
            OptionalSettings: new[] { "BaseUrl", "StatusCallbackUrl" },
            SupportsSandbox: false),

        // Agrégateur local (UEMOA) : son API n'est pas connue à l'avance, donc elle
        // se DÉCRIT ici au lieu d'être codée. Un gabarit de corps avec {to} et
        // {message} suffit à brancher un opérateur sans livrer une ligne de code.
        new(GenericHttpSms, ProviderChannel.Sms, "Agrégateur SMS (HTTP)",
            RequiredSettings: new[] { "Endpoint", "BodyTemplate" },
            RequiredSecrets: new[] { "ApiKey" },
            OptionalSettings: new[] { "Method", "AuthHeaderName", "AuthHeaderFormat", "ContentType", "SenderId", "SuccessJsonPath", "SuccessJsonValue" },
            SupportsSandbox: false),

        // ── WhatsApp.
        new(MetaCloud, ProviderChannel.WhatsApp, "WhatsApp Cloud API (Meta)",
            RequiredSettings: new[] { "PhoneNumberId", "GraphVersion" },
            RequiredSecrets: new[] { "AccessToken" },
            OptionalSettings: new[] { "BusinessAccountId", "DefaultTemplateName", "DefaultTemplateLanguage" },
            SupportsSandbox: false),

        new(Twilio, ProviderChannel.WhatsApp, "Twilio WhatsApp",
            RequiredSettings: new[] { "AccountSid", "FromNumber" },
            RequiredSecrets: new[] { "AuthToken" },
            OptionalSettings: new[] { "BaseUrl" },
            SupportsSandbox: false)
    };

    public static IReadOnlyList<ProviderDescriptor> Descriptors => All;

    public static IReadOnlyList<ProviderDescriptor> ForChannel(ProviderChannel channel)
        => All.Where(d => d.Channel == channel).ToList();

    /// <summary>
    /// La clé est le COUPLE (canal, code) : « Twilio » sert le SMS et WhatsApp avec
    /// des réglages différents.
    /// </summary>
    public static ProviderDescriptor? Find(ProviderChannel channel, string code)
        => All.FirstOrDefault(d => d.Channel == channel
            && string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));
}
