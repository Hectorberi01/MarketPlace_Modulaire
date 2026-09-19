using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Marketing.Domain.Legal;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// LE NUMÉRO DE VERSION EST UNE VALEUR, PAS UNE CHAÎNE LIBRE.
///
/// Auparavant il était saisi à la main À L'INTÉRIEUR du JSON du document. Rien
/// n'empêchait donc de publier un texte nouveau sous l'ancien numéro — et c'est le pire
/// des accidents possibles ici : personne n'est invité à réaccepter, l'acceptation
/// enregistrée désigne une rédaction qui n'existe plus, et RIEN dans le système ne le
/// signale. L'erreur ne se découvre que le jour du litige.
///
/// En faire une valeur ordonnée permet deux garanties que le texte libre interdit :
/// refuser un numéro déjà employé, et refuser une publication qui REVIENT EN ARRIÈRE.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public readonly record struct LegalVersionNumber(int Major, int Minor) : IComparable<LegalVersionNumber>
{
    public override string ToString() => $"{Major}.{Minor}";

    /// <summary>
    /// Analyse « 1.2 ». Tolère « 1 » (compris comme « 1.0 ») parce qu'un administrateur
    /// l'écrira, mais refuse tout le reste : ni « v1.2 », ni « 1.2.3 », ni « 1.2-bis ».
    /// Accepter des formes voisines reviendrait à recréer la chaîne libre qu'on quitte.
    /// </summary>
    public static Result<LegalVersionNumber> Parse(string? value)
    {
        var brut = (value ?? string.Empty).Trim();
        if (brut.Length == 0)
        {
            return Result.Failure<LegalVersionNumber>(Error.Validation(
                "marketing.legal.version_required",
                "Le numéro de version est obligatoire."));
        }

        var morceaux = brut.Split('.');
        if (morceaux.Length > 2)
        {
            return Invalide();
        }

        if (!int.TryParse(morceaux[0], out var majeur) || majeur < 0)
        {
            return Invalide();
        }

        var mineur = 0;
        if (morceaux.Length == 2 && (!int.TryParse(morceaux[1], out mineur) || mineur < 0))
        {
            return Invalide();
        }

        return Result.Success(new LegalVersionNumber(majeur, mineur));
    }

    private static Result<LegalVersionNumber> Invalide() => Result.Failure<LegalVersionNumber>(
        Error.Validation(
            "marketing.legal.version_invalid",
            "Le numéro de version doit s'écrire « majeur.mineur », par exemple 1.2."));

    /// <summary>
    /// Relecture depuis la base. Une valeur illisible LÈVE, et c'est voulu : elle ne peut
    /// venir que d'une écriture faite à la main en base, et servir « 0.0 » à la place
    /// donnerait un numéro qui ne désigne aucune rédaction — l'erreur silencieuse que
    /// tout ce type existe pour interdire.
    /// </summary>
    public static LegalVersionNumber Relire(string value)
    {
        var lu = Parse(value);
        if (lu.IsFailure)
        {
            throw new InvalidOperationException(
                $"Numéro de version illisible en base : « {value} ». Attendu « majeur.mineur ».");
        }

        return lu.Value;
    }

    public int CompareTo(LegalVersionNumber other)
    {
        var parMajeur = Major.CompareTo(other.Major);
        return parMajeur != 0 ? parMajeur : Minor.CompareTo(other.Minor);
    }

    public static bool operator <(LegalVersionNumber a, LegalVersionNumber b) => a.CompareTo(b) < 0;

    public static bool operator >(LegalVersionNumber a, LegalVersionNumber b) => a.CompareTo(b) > 0;

    public static bool operator <=(LegalVersionNumber a, LegalVersionNumber b) => a.CompareTo(b) <= 0;

    public static bool operator >=(LegalVersionNumber a, LegalVersionNumber b) => a.CompareTo(b) >= 0;
}
