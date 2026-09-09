using System.Globalization;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Montant monétaire (Value Object du shared kernel) : un montant et sa devise.
/// Partagé entre Offers, Pricing, Ordering, Payments — c'est un concept ubiquitaire
/// et stable. XOF par défaut sur le marché visé.
/// </summary>
public sealed class Money : ValueObject
{
    public const string DefaultCurrency = "XOF";

    /// <summary>
    /// ══════════════════════════════════════════════════════════════════════════════
    /// LE NOMBRE DE DÉCIMALES EST UNE PROPRIÉTÉ DE LA DEVISE, PAS UNE CONSTANTE.
    ///
    /// <c>Money</c> arrondissait TOUT à deux décimales. Sur le marché visé — le franc CFA,
    /// qui n'a AUCUNE subdivision — cela fabriquait des centimes qui n'existent pas :
    /// une remise de 10 % sur 89 991 produisait 8 999,10 XOF, ce chiffre remontait
    /// intact jusqu'au total de commande, puis la passerelle tronquait à l'entier et le
    /// prestataire encaissait 8 999. La commande restait enregistrée — et marquée
    /// PAYÉE — pour un montant que personne n'a jamais versé.
    ///
    /// La règle vit ICI, à l'unique endroit qui fabrique de l'argent, et nulle part
    /// ailleurs. Des tables par module auraient divergé : c'est déjà ce qui se passait,
    /// trois listes « zéro décimale » coexistaient (Stripe, PayPal, les métriques) sans
    /// que rien ne les tienne d'accord. Elles délèguent désormais toutes à cette table.
    ///
    /// Contenu : la liste ISO 4217 des devises d'exposant 0, complétée des devises à
    /// deux décimales que la place manipule réellement. Elle n'a pas à être exhaustive
    /// — voir le défaut retenu juste en dessous.
    /// ══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static readonly Dictionary<string, int> DecimalsByCurrency = new(StringComparer.OrdinalIgnoreCase)
    {
        // Sans subdivision (ISO 4217, exposant 0). XOF est la devise de la place.
        ["XOF"] = 0, ["XAF"] = 0, ["XPF"] = 0, ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0,
        ["GNF"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0, ["MGA"] = 0, ["PYG"] = 0,
        ["RWF"] = 0, ["UGX"] = 0, ["VND"] = 0, ["VUV"] = 0,

        // Deux décimales : les devises que les passerelles savent effectivement traiter.
        ["EUR"] = 2, ["USD"] = 2, ["GBP"] = 2, ["CHF"] = 2, ["CAD"] = 2,
        ["NGN"] = 2, ["GHS"] = 2, ["MAD"] = 2, ["ZAR"] = 2
    };

    /// <summary>
    /// ──────────────────────────────────────────────────────────────────────────────
    /// DEVISE INCONNUE : DEUX DÉCIMALES. LE CHOIX EST ASYMÉTRIQUE, PAS ARBITRAIRE.
    ///
    /// Se tromper dans un sens et dans l'autre ne coûte pas la même chose.
    ///
    /// SOUS-ARRONDIR (0 décimale sur une devise qui a des centimes) DÉTRUIT de la
    /// valeur réelle : jusqu'à une demi-unité par montant, sur une somme parfaitement
    /// légitime, et sur une ligne que l'acheteur a sous les yeux. C'est la
    /// falsification silencieuse d'un chiffre qui était juste.
    ///
    /// SUR-ARRONDIR (2 décimales sur une devise sans subdivision) garde des centimes
    /// que le prestataire ne saura pas encaisser — exactement le défaut décrit
    /// ci-dessus. Mais il ne peut plus produire d'encaissement faux : les passerelles
    /// REFUSENT désormais une devise qu'elles ne savent pas traiter au lieu de
    /// tronquer en silence. Le pire cas est donc un refus lisible, pas une commande
    /// enregistrée payée pour une somme jamais perçue.
    ///
    /// S'y ajoute un argument de volume : les devises d'exposant 0 forment une liste
    /// FERMÉE et courte, entièrement recensée au-dessus. Une devise absente de la table
    /// est donc, presque à coup sûr, une devise à deux décimales.
    /// ──────────────────────────────────────────────────────────────────────────────
    /// </summary>
    public const int UnknownCurrencyDecimals = 2;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Result<Money> Create(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0m)
        {
            return Error.Validation("money.amount_negative", "Le montant ne peut pas être négatif.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Error.Validation("money.currency_invalid", "La devise doit être un code ISO à 3 lettres.");
        }

        // On normalise AVANT d'arrondir : la précision se lit dans la table par le code
        // ISO, pas par la casse que l'appelant a bien voulu écrire.
        var iso = currency.Trim().ToUpperInvariant();
        return new Money(Round(amount, iso), iso);
    }

    /// <summary>Nombre de décimales significatives de la devise (0 pour XOF, 2 pour EUR).</summary>
    public static int DecimalsFor(string? currency)
        => currency is not null && DecimalsByCurrency.TryGetValue(currency.Trim(), out var decimals)
            ? decimals
            : UnknownCurrencyDecimals;

    /// <summary>Vrai si la devise n'admet AUCUNE subdivision (XOF, XAF, JPY…).</summary>
    public static bool IsZeroDecimal(string? currency) => DecimalsFor(currency) == 0;

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// LA DEVISE EST-ELLE UNE DEVISE QUE LA PLACE CONNAÎT ?
    ///
    /// <see cref="DecimalsFor"/> ne peut pas répondre à cette question, et c'est
    /// délibéré : il RETOURNE TOUJOURS un nombre de décimales, y compris pour un code
    /// absent de la table, parce qu'un calcul déjà engagé doit arrondir de la manière
    /// la moins destructrice plutôt que d'échouer au milieu d'une addition.
    ///
    /// Ce repli est le bon comportement EN AVAL, une fois le montant accepté. Il est le
    /// mauvais comportement EN AMONT, au moment où un appelant extérieur choisit la
    /// devise : là, un code inconnu ne doit pas être deviné à deux décimales, il doit
    /// être refusé. Sans quoi « XYZ » franchit la validation, traverse toute la chaîne
    /// de prix, et n'échoue qu'au moment de l'encaissement, quand la passerelle
    /// refusera une devise dont elle n'a jamais entendu parler.
    ///
    /// D'où ce prédicat, distinct et explicite : il dit ce que la table CONTIENT, là où
    /// <see cref="DecimalsFor"/> dit ce qu'il faut faire quand elle ne contient rien.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static bool IsSupported(string? currency)
        => !string.IsNullOrWhiteSpace(currency)
           && DecimalsByCurrency.ContainsKey(currency.Trim());

    /// <summary>
    /// Les devises que la place sait tarifer, triées. Sert aux messages d'erreur et aux
    /// écrans d'administration : refuser une devise sans dire lesquelles sont admises
    /// oblige l'appelant à deviner.
    /// </summary>
    public static IReadOnlyCollection<string> SupportedCurrencies { get; } =
        DecimalsByCurrency.Keys.OrderBy(static c => c, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Arrondit un montant à la précision de SA devise. C'est la primitive que doivent
    /// appeler tous les calculs monétaires du dépôt — remises, taxes, commissions,
    /// répartitions — à la place d'un <c>decimal.Round(x, 2) </c> écrit en dur.
    ///
    /// LE MODE DE DÉPARTAGE NE CHANGE PAS. On garde <c>ToEven</c>, le défaut de
    /// <see cref="decimal.Round(decimal, int)"/> qu'appliquait déjà <see cref="Create"/> :
    /// ce correctif porte sur le NOMBRE de décimales, rien d'autre. Y greffer un
    /// passage à <c>AwayFromZero</c> déplacerait en prime tous les montants à mi-chemin,
    /// sans rapport avec le défaut traité et sans qu'aucun test ne dise pourquoi.
    /// </summary>
    public static decimal Round(decimal amount, string? currency) => decimal.Round(amount, DecimalsFor(currency));

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public Result<Money> Add(Money other)
    {
        if (other.Currency != Currency)
        {
            return Error.Validation("money.currency_mismatch", "Devises incompatibles.");
        }

        return new Money(Amount + other.Amount, Currency);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Amount;
        yield return Currency;
    }

    /// <summary>
    /// Affiche le montant avec le nombre de décimales de sa devise. Le format était
    /// figé à « 0.00 » : un total en francs CFA se lisait « 8999.00 XOF », deux zéros
    /// qui n'ont aucun sens dans une devise sans subdivision et qui laissaient croire
    /// à une précision que la caisse ne connaît pas. Culture invariante : ces chaînes
    /// partent dans des journaux et des messages d'erreur, jamais dans une interface.
    /// </summary>
    public override string ToString()
        => $"{Amount.ToString("F" + DecimalsFor(Currency), CultureInfo.InvariantCulture)} {Currency}";
}
