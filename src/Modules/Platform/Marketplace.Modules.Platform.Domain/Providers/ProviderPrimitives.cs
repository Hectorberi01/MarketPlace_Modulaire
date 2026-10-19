namespace Marketplace.Modules.Platform.Domain.Providers;

/// <summary>Identité forte d'un fournisseur enregistré.</summary>
public readonly record struct ProviderId(Guid Value)
{
    public static ProviderId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Canal servi par un fournisseur.</summary>
public enum ProviderChannel
{
    Payment = 0,
    Email = 1,
    Sms = 2,
    WhatsApp = 3,
    Push = 4
}

/// <summary>
/// Mode d'exécution. En Sandbox, les appels partent vers le bac à sable du
/// fournisseur : rien n'est encaissé, rien n'est envoyé à un vrai destinataire.
/// </summary>
public enum ProviderMode
{
    Sandbox = 0,
    Live = 1
}

/// <summary>Règles propres à chaque canal.</summary>
public static class ChannelRules
{
    /// <summary>
    /// Plusieurs fournisseurs actifs en même temps : vrai pour le PAIEMENT seulement.
    /// L'acheteur choisit son moyen de paiement, donc Stripe, MTN MoMo et FedaPay
    /// coexistent. Pour un e-mail ou un SMS, personne ne choisit : deux fournisseurs
    /// actifs enverraient le message DEUX FOIS.
    /// </summary>
    public static bool AllowsMultipleEnabled(ProviderChannel channel) => channel == ProviderChannel.Payment;
}
