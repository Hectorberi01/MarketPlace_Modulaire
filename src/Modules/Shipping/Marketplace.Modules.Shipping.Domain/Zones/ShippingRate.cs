using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Shipping.Domain.Zones;

/// <summary>
/// Tarif d'un service de livraison au sein d'une zone. Value object : un tarif n'a pas
/// d'identité propre, il n'existe que rattaché à sa zone.
///
/// <para>
/// <b>Le montant est un forfait, pas un calcul.</b> Il ne dépend ni du poids, ni du
/// volume, ni du nombre d'articles — seulement de la zone de destination et du service
/// choisi. C'est ce que faisait déjà le tarif codé en dur ; la différence est qu'il est
/// désormais modifiable sans livraison de code, et qu'il varie selon la destination.
/// </para>
/// </summary>
public sealed class ShippingRate : ValueObject
{
    public const int MaxServiceCode = 40;
    public const int MaxLabel = 80;
    public const int MaxEta = 120;

    private ShippingRate(string serviceCode, string label, string eta, decimal amount, string currency)
    {
        ServiceCode = serviceCode;
        Label = label;
        Eta = eta;
        Amount = amount;
        Currency = currency;
    }

    /// <summary>« standard », « express »… Ce que le client renvoie au moment de payer.</summary>
    public string ServiceCode { get; }

    /// <summary>Libellé affiché (« Standard »).</summary>
    public string Label { get; }

    /// <summary>Délai annoncé, en toutes lettres (« Livraison sous 24h à 48h »).</summary>
    public string Eta { get; }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Result<ShippingRate> Create(string? serviceCode, string? label, string? eta, decimal amount, string? currency)
    {
        var code = NormalizeServiceCode(serviceCode);
        if (code is null)
        {
            return Error.Validation("shipping.rate.service_required", "Le code du service est obligatoire.");
        }

        if (amount < 0m)
        {
            return Error.Validation("shipping.rate.amount_negative", "Le tarif ne peut pas être négatif.");
        }

        // Une devise de trois lettres, majuscules. XOF aujourd'hui ; la contrainte est
        // là pour que le jour où une autre apparaît, elle soit visible plutôt que subie.
        var cur = string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();
        if (cur.Length != 3)
        {
            return Error.Validation("shipping.rate.currency_invalid", "La devise doit tenir en trois lettres (XOF).");
        }

        return new ShippingRate(
            code,
            Cap(string.IsNullOrWhiteSpace(label) ? code : label.Trim(), MaxLabel),
            Cap(eta?.Trim() ?? string.Empty, MaxEta),
            amount,
            cur);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// UNE SEULE RÈGLE POUR ÉCRIRE ET POUR SUPPRIMER.
    ///
    /// La création se contentait d'un `Trim().ToLowerInvariant()` pendant que
    /// <c>ShippingZone.RemoveRate</c> cherchait le tarif par un SLUG — accents repliés,
    /// espaces changés en tirets. Un code saisi « Express Soir » était donc stocké
    /// « express soir » et recherché « express-soir » : le tarif s'affichait à l'écran
    /// et refusait définitivement d'être supprimé, avec un « Tarif introuvable » que
    /// rien ne permettait de comprendre.
    ///
    /// Les deux chemins passent désormais ici. La règle vit à un seul endroit, sur le
    /// type qui porte le code — pas dans chacun de ses appelants.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <returns>Le code normalisé, ou <c>null</c> s'il ne reste rien d'exploitable.</returns>
    public static string? NormalizeServiceCode(string? serviceCode)
    {
        if (string.IsNullOrWhiteSpace(serviceCode))
        {
            return null;
        }

        var folded = BeninGeography.FoldForKey(serviceCode.Trim()).Replace(' ', '-');
        return folded.Length == 0 ? null : Cap(folded, MaxServiceCode);
    }

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return ServiceCode;
        yield return Label;
        yield return Eta;
        yield return Amount;
        yield return Currency;
    }
}
