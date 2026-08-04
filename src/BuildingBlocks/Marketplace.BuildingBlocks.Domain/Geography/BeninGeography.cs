using System.Globalization;
using System.Text;

namespace Marketplace.BuildingBlocks.Domain.Geography;

/// <summary>Un des douze départements du Bénin.</summary>
public sealed record BeninDepartment(string Code, string Name);

/// <summary>
/// Une des 77 communes du Bénin, rattachée à son département.
/// </summary>
/// <param name="Code">
/// Identifiant STABLE, en minuscules sans accent (« abomey-calavi », « seme-podji »).
/// C'est lui qui est stocké en base, jamais le libellé.
/// </param>
/// <param name="Name">Libellé d'affichage, accentué (« Abomey-Calavi », « Sèmè-Podji »).</param>
/// <param name="DepartmentCode">Code du département de rattachement.</param>
public sealed record BeninCommune(string Code, string Name, string DepartmentCode)
{
    /// <summary>Département de rattachement, résolu depuis <see cref="DepartmentCode"/>.</summary>
    public BeninDepartment Department => BeninGeography.Department(DepartmentCode)!;
}


/// <summary>Une commune telle qu'elle est servie aux applications.</summary>
public sealed record BeninCommuneView(string Code, string Name, string DepartmentCode, string DepartmentName);

/// <summary>Un département tel qu'il est servi aux applications.</summary>
public sealed record BeninDepartmentView(string Code, string Name);

/// <summary>
/// Charge utile complète du référentiel, prête à sérialiser.
///
/// Elle vit ICI et pas dans un BFF : deux BFF la servent (Mobile et Vendeur), et une
/// charge utile recopiée dans deux fichiers diverge au premier ajustement. Le
/// référentiel a déjà une autorité unique — sa représentation aussi.
/// </summary>
public sealed record BeninGeographyReference(
    string CountryCode,
    string DialingCode,
    int PhoneLength,
    IReadOnlyList<BeninDepartmentView> Departments,
    IReadOnlyList<BeninCommuneView> Communes);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// DÉCOUPAGE ADMINISTRATIF DU BÉNIN — 12 DÉPARTEMENTS, 77 COMMUNES.
///
/// POURQUOI CE RÉFÉRENTIEL EXISTE
///
/// Le Bénin n'a pas de système de codes postaux opérationnel, et une grande partie des
/// rues n'ont ni nom ni numéro. Une adresse s'y donne par commune, quartier et point de
/// repère. Tant que la ville restait un champ texte libre, « Cotonou », « cotonou » et
/// « COTONOU » cohabitaient dans la même colonne : impossible d'en tirer un routage, une
/// zone de livraison ou la moindre statistique. La commune est donc désormais choisie
/// dans une liste fermée, et c'est le CODE qui est stocké.
///
/// POURQUOI UN CODE ET PAS LE LIBELLÉ
///
/// Les libellés bougent — accents, tirets, « Sèmè-Podji » ou « Sèmè-Kpodji ». Le code ne
/// bouge pas : c'est le contrat de ce fichier. On peut corriger un <c>Name</c> sans
/// migrer une seule ligne. On ne renomme JAMAIS un <c>Code</c> ; si une commune est
/// dissoute ou fusionnée, on garde son entrée (les adresses et les commandes historiques
/// la référencent) et on ajoute la nouvelle.
///
/// POURQUOI CODÉ EN DUR PLUTÔT QU'EN BASE
///
/// 77 lignes qui n'ont pas changé depuis la réforme de 1999. Une table exigerait une
/// migration de données, un écran d'administration et une synchronisation entre les
/// quatre surfaces, pour un référentiel que personne n'éditera. Le jour où le
/// gouvernement redécoupe, une livraison de code suffit — et c'est justement une
/// occasion de relire le reste.
///
/// SOURCE : Ministère de la Décentralisation et de la Gouvernance Locale,
/// https://www.decentralisation.gouv.bj/communes-benin/ (ordre et rattachements repris
/// tels quels ; libellés accentués selon l'orthographe courante).
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class BeninGeography
{
    /// <summary>Indicatif téléphonique du Bénin.</summary>
    public const string DialingCode = "+229";

    /// <summary>Longueur du numéro national, hors indicatif, depuis la migration de 2024.</summary>
    public const int LocalPhoneLength = 10;

    /// <summary>Code pays ISO 3166-1 alpha-2. Seul pays desservi à ce jour.</summary>
    public const string CountryCode = "BJ";

    public static IReadOnlyList<BeninDepartment> Departments { get; } =
    [
        new("alibori", "Alibori"),
        new("atacora", "Atacora"),
        new("atlantique", "Atlantique"),
        new("borgou", "Borgou"),
        new("collines", "Collines"),
        new("couffo", "Couffo"),
        new("donga", "Donga"),
        new("littoral", "Littoral"),
        new("mono", "Mono"),
        new("oueme", "Ouémé"),
        new("plateau", "Plateau"),
        new("zou", "Zou"),
    ];

    public static IReadOnlyList<BeninCommune> Communes { get; } =
    [
        // ── Alibori (6) ──────────────────────────────────────────────────────────
        new("banikoara", "Banikoara", "alibori"),
        new("gogounou", "Gogounou", "alibori"),
        new("kandi", "Kandi", "alibori"),
        new("karimama", "Karimama", "alibori"),
        new("malanville", "Malanville", "alibori"),
        new("segbana", "Ségbana", "alibori"),

        // ── Atacora (9) ──────────────────────────────────────────────────────────
        new("boukoumbe", "Boukoumbé", "atacora"),
        new("cobly", "Cobly", "atacora"),
        new("kerou", "Kérou", "atacora"),
        new("kouande", "Kouandé", "atacora"),
        new("materi", "Matéri", "atacora"),
        new("natitingou", "Natitingou", "atacora"),
        new("ouassa-pehunco", "Ouassa-Péhunco", "atacora"),
        new("tanguieta", "Tanguiéta", "atacora"),
        new("toucountouna", "Toucountouna", "atacora"),

        // ── Atlantique (8) ───────────────────────────────────────────────────────
        new("abomey-calavi", "Abomey-Calavi", "atlantique"),
        new("allada", "Allada", "atlantique"),
        new("kpomasse", "Kpomassè", "atlantique"),
        new("ouidah", "Ouidah", "atlantique"),
        new("so-ava", "Sô-Ava", "atlantique"),
        new("toffo", "Toffo", "atlantique"),
        new("tori-bossito", "Tori-Bossito", "atlantique"),
        new("ze", "Zè", "atlantique"),

        // ── Borgou (8) ───────────────────────────────────────────────────────────
        new("bembereke", "Bembéréké", "borgou"),
        new("kalale", "Kalalé", "borgou"),
        new("n-dali", "N'Dali", "borgou"),
        new("nikki", "Nikki", "borgou"),
        new("parakou", "Parakou", "borgou"),
        new("perere", "Pèrèrè", "borgou"),
        new("sinende", "Sinendé", "borgou"),
        new("tchaourou", "Tchaourou", "borgou"),

        // ── Collines (6) ─────────────────────────────────────────────────────────
        new("bante", "Bantè", "collines"),
        new("dassa-zoume", "Dassa-Zoumè", "collines"),
        new("glazoue", "Glazoué", "collines"),
        new("ouesse", "Ouèssè", "collines"),
        new("savalou", "Savalou", "collines"),
        new("save", "Savè", "collines"),

        // ── Couffo (6) ───────────────────────────────────────────────────────────
        new("aplahoue", "Aplahoué", "couffo"),
        new("djakotomey", "Djakotomey", "couffo"),
        new("dogbo", "Dogbo", "couffo"),
        new("klouekanme", "Klouékanmè", "couffo"),
        new("lalo", "Lalo", "couffo"),
        new("toviklin", "Toviklin", "couffo"),

        // ── Donga (4) ────────────────────────────────────────────────────────────
        new("bassila", "Bassila", "donga"),
        new("copargo", "Copargo", "donga"),
        new("djougou", "Djougou", "donga"),
        new("ouake", "Ouaké", "donga"),

        // ── Littoral (1) ─────────────────────────────────────────────────────────
        new("cotonou", "Cotonou", "littoral"),

        // ── Mono (6) ─────────────────────────────────────────────────────────────
        new("athieme", "Athiémé", "mono"),
        new("bopa", "Bopa", "mono"),
        new("come", "Comè", "mono"),
        new("grand-popo", "Grand-Popo", "mono"),
        new("houeyogbe", "Houéyogbé", "mono"),
        new("lokossa", "Lokossa", "mono"),

        // ── Ouémé (9) ────────────────────────────────────────────────────────────
        new("adjarra", "Adjarra", "oueme"),
        new("adjohoun", "Adjohoun", "oueme"),
        new("aguegues", "Aguégués", "oueme"),
        new("akpro-misserete", "Akpro-Missérété", "oueme"),
        new("avrankou", "Avrankou", "oueme"),
        new("bonou", "Bonou", "oueme"),
        new("dangbo", "Dangbo", "oueme"),
        new("porto-novo", "Porto-Novo", "oueme"),
        new("seme-podji", "Sèmè-Podji", "oueme"),

        // ── Plateau (5) ──────────────────────────────────────────────────────────
        new("adja-ouere", "Adja-Ouèrè", "plateau"),
        new("ifangni", "Ifangni", "plateau"),
        new("ketou", "Kétou", "plateau"),
        new("pobe", "Pobè", "plateau"),
        new("sakete", "Sakété", "plateau"),

        // ── Zou (9) ──────────────────────────────────────────────────────────────
        new("abomey", "Abomey", "zou"),
        new("agbangnizoun", "Agbangnizoun", "zou"),
        new("bohicon", "Bohicon", "zou"),
        new("cove", "Covè", "zou"),
        new("djidja", "Djidja", "zou"),
        new("ouinhi", "Ouinhi", "zou"),
        new("zagnanado", "Zagnanado", "zou"),
        new("za-kpota", "Za-Kpota", "zou"),
        new("zogbodomey", "Zogbodomey", "zou"),
    ];

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE RÉFÉRENTIEL PRÊT À SERVIR — CONSTRUIT PARESSEUSEMENT, ET C'EST OBLIGATOIRE.
    ///
    /// Les initialiseurs de champs statiques s'exécutent DANS L'ORDRE DE DÉCLARATION,
    /// avant tout accès au type. La première version construisait la valeur directement
    /// ici, or elle lit <c>c.Department</c>, qui interroge <c>DepartmentsByCode</c> —
    /// déclaré PLUS BAS, donc encore <c>null</c> à cet instant.
    ///
    /// Le résultat n'était pas une simple valeur fausse : le constructeur de type levait
    /// une <c>NullReferenceException</c>, enveloppée en <c>TypeInitializationException</c>,
    /// et TOUTE utilisation de <c>BeninGeography</c> devenait impossible — y compris la
    /// simple résolution d'une commune, sans rapport avec le référentiel servi.
    ///
    /// <c>Lazy</c> supprime la classe de bogue entière plutôt que ce cas précis : la
    /// fabrique n'est appelée qu'au PREMIER ACCÈS, quand le constructeur de type est
    /// achevé et que tous les dictionnaires existent. Réordonner les déclarations ne peut
    /// plus rien casser.
    ///
    /// Communes triées par CODE, qui est déjà désaccentué. Trier par libellé avec
    /// <c>StringComparer.CurrentCulture</c> serait un autre piège : la solution active
    /// <c>InvariantGlobalization</c>, ce qui ramène la comparaison à un tri ordinal —
    /// « Ségbana » et « Sèmè-Podji » se retrouveraient après « Zogbodomey ».
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static BeninGeographyReference Reference => LazyReference.Value;

    private static readonly Lazy<BeninGeographyReference> LazyReference = new(() => new(
        CountryCode,
        DialingCode,
        LocalPhoneLength,
        [.. Departments.Select(d => new BeninDepartmentView(d.Code, d.Name))],
        [.. Communes
            .OrderBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => new BeninCommuneView(c.Code, c.Name, c.DepartmentCode, c.Department.Name))]));

    private static readonly Dictionary<string, BeninCommune> ByCode =
        Communes.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, BeninDepartment> DepartmentsByCode =
        Departments.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Index de RATTRAPAGE : libellé normalisé → commune.
    ///
    /// Il existe pour une seule raison — les données déjà en base ont été saisies en texte
    /// libre. « Cotonou », « cotonou », « COTONOU » et « Abomey Calavi » doivent pouvoir
    /// retomber sur la bonne commune lors de la reprise. Ce n'est PAS un chemin de saisie :
    /// les formulaires imposent une liste fermée.
    /// </summary>
    private static readonly Dictionary<string, BeninCommune> ByNormalizedName = BuildNameIndex();

    public static BeninDepartment? Department(string? code)
        => string.IsNullOrWhiteSpace(code) ? null
            : DepartmentsByCode.TryGetValue(code.Trim(), out var d) ? d : null;

    /// <summary>Commune correspondant à un code stocké. <c>null</c> si le code est inconnu.</summary>
    public static BeninCommune? Commune(string? code)
        => string.IsNullOrWhiteSpace(code) ? null
            : ByCode.TryGetValue(code.Trim(), out var c) ? c : null;

    /// <summary>Le code est-il celui d'une commune connue ?</summary>
    public static bool IsKnownCommune(string? code) => Commune(code) is not null;

    /// <summary>
    /// Libellé d'affichage d'un code stocké.
    ///
    /// Renvoie le code lui-même si la commune est inconnue, plutôt qu'une chaîne vide :
    /// sur une commande passée il y a six mois, mieux vaut afficher « commune-disparue »
    /// qu'un blanc que personne ne saura interpréter.
    /// </summary>
    public static string CommuneName(string? code) => Commune(code)?.Name ?? (code ?? string.Empty).Trim();

    /// <summary>
    /// Retrouve une commune à partir d'un LIBELLÉ saisi librement (reprise de données,
    /// import CSV). Tolère la casse, les accents, les tirets et les espaces multiples :
    /// « ABOMEY CALAVI », « abomey-calavi » et « Abomey–Calavi » donnent la même commune.
    /// </summary>
    public static BeninCommune? MatchByName(string? name)
    {
        var key = Normalize(name);
        return key.Length == 0 ? null : ByNormalizedName.GetValueOrDefault(key);
    }

    /// <summary>
    /// Résout une valeur d'entrée en CODE de commune, qu'elle soit déjà un code ou un
    /// libellé. Renvoie <c>null</c> si rien ne correspond — jamais une valeur inventée.
    /// </summary>
    public static string? ResolveCommuneCode(string? codeOrName)
        => Commune(codeOrName)?.Code ?? MatchByName(codeOrName)?.Code;

    // ── Téléphone ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalise un numéro béninois en <c>+229XXXXXXXXXX</c>, ou <c>null</c> s'il ne peut
    /// pas l'être.
    ///
    /// Accepte les formes réellement saisies par les gens : avec ou sans « + », avec ou
    /// sans indicatif, avec espaces, points ou tirets. Refuse tout le reste — un numéro à
    /// 8 chiffres est un ancien numéro d'avant la migration de 2024, il n'aboutira pas, et
    /// l'accepter en silence revient à livrer un colis que personne ne pourra annoncer.
    /// </summary>
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());

        // « 00229… » (préfixe international à l'ancienne) puis « 229… ».
        if (digits.StartsWith("00229", StringComparison.Ordinal))
        {
            digits = digits[5..];
        }
        else if (digits.StartsWith("229", StringComparison.Ordinal) && digits.Length > LocalPhoneLength)
        {
            digits = digits[3..];
        }

        return digits.Length == LocalPhoneLength ? DialingCode + digits : null;
    }

    /// <summary>Le numéro est-il un numéro béninois valide ?</summary>
    public static bool IsValidPhone(string? input) => NormalizePhone(input) is not null;

    // ── Interne ─────────────────────────────────────────────────────────────────

    private static Dictionary<string, BeninCommune> BuildNameIndex()
    {
        var index = new Dictionary<string, BeninCommune>(StringComparer.Ordinal);

        foreach (var commune in Communes)
        {
            // Le libellé accentué ET le code : « Sèmè-Podji » comme « seme-podji »
            // se normalisent vers la même clé, mais on ajoute les deux par prudence
            // (une orthographe future pourrait diverger du code).
            index.TryAdd(Normalize(commune.Name), commune);
            index.TryAdd(Normalize(commune.Code), commune);
        }

        // Variantes réellement rencontrées, qu'aucune normalisation mécanique ne rattrape.
        AddAlias(index, "seme kpodji", "seme-podji");
        AddAlias(index, "semekpodji", "seme-podji");
        AddAlias(index, "pehunco", "ouassa-pehunco");
        AddAlias(index, "dogbo tota", "dogbo");
        AddAlias(index, "calavi", "abomey-calavi");
        AddAlias(index, "porto novo", "porto-novo");

        return index;
    }

    private static void AddAlias(Dictionary<string, BeninCommune> index, string alias, string communeCode)
    {
        if (ByCode.TryGetValue(communeCode, out var commune))
        {
            index.TryAdd(Normalize(alias), commune);
        }
    }

    /// <summary>
    /// Minuscules, sans accent, sans ponctuation, espaces repliés. « Sô-Ava »,
    /// « SO AVA » et « so_ava » donnent tous « so ava ».
    ///
    /// PUBLIQUE parce que d'autres modules doivent fabriquer des clés SELON LA MÊME
    /// RÈGLE — les codes de zones de livraison, par exemple. Deux normalisations
    /// voisines mais différentes finissent toujours par diverger sur un cas limite.
    /// </summary>
    public static string FoldForKey(string? value) => Normalize(value);

    /// <summary>
    /// Minuscules, sans accent, sans ponctuation, espaces repliés. « Sô-Ava »,
    /// « SO AVA » et « so_ava » donnent tous « so ava ».
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSeparator = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSeparator = false;
                continue;
            }

            // Tirets, apostrophes, espaces, soulignés : un seul séparateur logique.
            if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append(' ');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}
