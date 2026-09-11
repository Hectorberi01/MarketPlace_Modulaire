using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE MOTIF QUI A EFFACÉ DES DONNÉES TROIS FOIS EN UNE SEMAINE.
///
/// Trois fois, le même enchaînement, sur trois modules sans rapport :
///
///   `Category.AttributeSchema` ....... renommer une catégorie effaçait son schéma
///                                      d'attributs, et donc les contraintes de saisie
///                                      de tout son rayon.
///   `Campaign.TargetSegment` ......... renommer une campagne effaçait son ciblage.
///   `ContentBlock.StartUtc/EndUtc` ... corriger le titre d'une bannière effaçait sa
///                                      programmation.
///
/// À chaque fois : le contrat de lecture porte le champ, la commande de mise à jour
/// l'accepte en OPTIONNEL, l'écran ne le recharge pas, l'agrégat l'écrase par null.
/// Aucune erreur, aucune trace, un « Modification enregistrée. » — et la donnée est
/// partie. C'est la pire forme de bogue : celle qui rend un succès.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// CE QUE CE TEST FAIT, ET CE QU'IL NE FAIT PAS.
///
/// Il NE VÉRIFIE PAS le comportement : deviner par réflexion si un gestionnaire
/// préserve ou écrase demanderait de lire des corps de méthodes, et un tel test serait
/// faux le jour où quelqu'un écrit la même chose autrement.
///
/// Il force une DÉCISION ÉCRITE. Toute commande `Update*` portant au moins un champ
/// annulable doit figurer dans le registre ci-dessous, avec la réponse à une seule
/// question : quand le client n'envoie pas ce champ, que devient-il ?
///
/// Le jour où quelqu'un ajoute une commande de mise à jour, ce test casse. Il ne lui
/// dit pas quoi faire — il lui demande d'y avoir pensé, et de l'écrire pour le suivant.
/// C'est exactement ce qui a manqué trois fois.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// ET IL A FAILLI NE RIEN REGARDER DU TOUT. VOICI POURQUOI.
///
/// La première version balayait `AppDomain.CurrentDomain.GetAssemblies()` après avoir
/// « amorcé » le chargement avec une suite de `_ = typeof(X);`. Le CLR charge un
/// assemblage à la demande, l'intention était donc correcte — mais `_ = expression;`
/// est une AFFECTATION À UN REJET, et Roslyn supprime purement et simplement une
/// expression rejetée qui n'a pas d'effet de bord. En Release, le corps de la méthode
/// d'amorçage ne compilait vers RIEN.
///
/// Le résultat est le pire des trois possibles : en CI, seuls deux assemblages étaient
/// chargés au moment où ces tests ont tourné, le balayage n'a vu que cinq commandes sur
/// dix-neuf, et le premier test est passé AU VERT en ne regardant presque rien. Sans le
/// second, qui lui a échoué en trouvant quatorze entrées de registre « disparues », rien
/// n'aurait signalé que le garde-fou ne gardait rien.
///
/// La leçon tient en une ligne, et elle vaut au-delà d'ici : ne jamais fonder un
/// balayage sur ce qui SE TROUVE chargé. On énumère donc les assemblages applicatifs
/// depuis le disque, à côté de cet assemblage de test, et l'on vérifie que chacun s'est
/// bien chargé — voir <c>ChargerLesAssemblagesApplicatifs</c>.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class PreservationDesMisesAJourTests
{
    /// <summary>
    /// Ce que devient un champ annulable que le client n'a pas envoyé.
    /// </summary>
    private enum Verdict
    {
        /// <summary>
        /// La valeur absente ÉCRASE : null en base. C'est le comportement par défaut
        /// d'un agrégat, et il est légitime — un PUT remplace — À CONDITION que tous
        /// les clients rechargent le champ avant d'éditer.
        /// </summary>
        Remplace,

        /// <summary>
        /// Le gestionnaire coalesce : `?? entite.Champ`. La valeur absente n'écrase
        /// rien, et un client partiel est sans danger.
        /// </summary>
        Preserve,

        /// <summary>
        /// Commande dont les clients n'ont pas été audités. Ce n'est PAS une excuse :
        /// c'est une dette nommée. La lever demande de lire chaque écran qui l'appelle
        /// et de vérifier qu'il recharge les champs annulables.
        /// </summary>
        NonAudite,
    }

    /// <summary>
    /// Le registre. Une entrée par commande, une phrase qui dit pourquoi.
    ///
    /// NE PAS AJOUTER UNE LIGNE POUR FAIRE PASSER LE TEST. Une entrée « Remplace » est
    /// une PROMESSE : celle que tout écran appelant cette commande recharge ses champs
    /// annulables avant d'enregistrer. Si vous ne pouvez pas la tenir, c'est
    /// « NonAudite » — et il reste du travail.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (Verdict Verdict, string Motif)> Registre =
        new Dictionary<string, (Verdict, string)>
        {
            // ── Corrigés après un effacement réel ────────────────────────────────
            ["UpdateCategoryCommand"] = (Verdict.Remplace,
                "AttributeSchema : la console le recharge et le renvoie TOUJOURS. "
                + "Avant correction, tout renommage vidait le schéma du rayon."),

            ["UpdateCampaignCommand"] = (Verdict.Remplace,
                "TargetSegment : la console le recharge depuis le contrat de lecture. "
                + "Avant correction, tout renommage effaçait le ciblage."),

            ["UpdateContentBlockCommand"] = (Verdict.Remplace,
                "StartUtc/EndUtc : la console les recharge, les expose en saisie et les "
                + "renvoie toujours. Avant correction, toute retouche effaçait la "
                + "programmation d'affichage."),

            // ── Préservent explicitement, et c'est écrit dans leur gestionnaire ──
            ["UpdateTaxRuleCommand"] = (Verdict.Preserve,
                "EffectiveFromUtc : le gestionnaire fait `?? rule.EffectiveFromUtc`. "
                + "Sans cela, corriger un taux remettait en vigueur immédiate une règle programmée."),

            ["UpdateCommissionRuleCommand"] = (Verdict.Preserve,
                "EffectiveFromUtc : même coalescence, même raison. Scope et TargetId ne "
                + "sont pas portés par la commande — le périmètre n'est pas modifiable."),

            // ── Audités, sans champ à perdre ─────────────────────────────────────
            ["UpdateRoleCommand"] = (Verdict.Remplace,
                "Description seule. La console la recharge ; et depuis le garde système, "
                + "un rôle système n'est plus éditable du tout."),

            ["UpdateShippingZoneCommand"] = (Verdict.Remplace,
                "CommuneCodes : la console recharge la liste complète et la renvoie "
                + "intégralement. Vérifié — le piège avait été identifié et traité à l'écrit."),

            ["UpdateCarrierCommand"] = (Verdict.Remplace,
                "TrackingUrlTemplate et LogoUrl : la console les recharge à l'édition."),

            ["UpdateSellerProfileCommand"] = (Verdict.Remplace,
                "LogoUrl et Description. Aucun écran d'administration n'appelle cette "
                + "commande aujourd'hui ; l'espace vendeur, si."),

            // ── Dette nommée : clients non audités ───────────────────────────────
            ["UpdateAddressCommand"] = (Verdict.NonAudite,
                "Huit champs annulables, dont Latitude et Longitude. Appelée depuis "
                + "l'espace client et l'espace vendeur, non audités."),

            ["UpdatePaymentMethodCommand"] = (Verdict.NonAudite,
                "Six champs annulables. Appelée depuis l'espace client, non audité."),

            ["UpdateProductCommand"] = (Verdict.NonAudite,
                "BrandId, Gtin, Ean, ProductGroupId, Attributes, Tags. Appelée depuis "
                + "l'espace vendeur, non audité. La console ne l'appelle pas."),

            ["UpdateProductVariantCommand"] = (Verdict.NonAudite,
                "Attributes et Barcode. Espace vendeur, non audité."),

            ["UpdateLocationAddressCommand"] = (Verdict.NonAudite,
                "Six champs annulables, dont les coordonnées — celles-là mêmes qui "
                + "manquent déjà à la carte de la flotte. Espace vendeur, non audité."),

            ["UpdateSellerMetadataCommand"] = (Verdict.NonAudite,
                "Metadata entier, annulable. Non audité."),

            ["UpdateBrandCommand"] = (Verdict.Remplace,
                "LogoUrl et Description : la console les recharge à l'édition. Vérifié."),

            // ── Sans champ annulable : listées pour que l'absence soit un choix ──
            ["UpdateCartItemQuantityCommand"] = (Verdict.Remplace, "Aucun champ annulable."),
            ["UpdateNotificationPreferencesCommand"] = (Verdict.Remplace,
                "MutedCategories est une liste NON annulable : une liste vide est une "
                + "valeur, pas une absence."),
            ["UpdateUserProfileCommand"] = (Verdict.Remplace, "Aucun champ annulable."),
        };

    /// <summary>
    /// Fichiers trouvés que le CLR a refusé de charger, avec la raison.
    ///
    /// DÉCLARÉE AVANT `Assemblages`, et ce n'est pas cosmétique : les initialiseurs
    /// statiques s'exécutent dans l'ordre d'écriture. Déclarée après, elle vaudrait
    /// encore `null` pendant le chargement, et le `catch` qui note une panne planterait
    /// lui-même — le seul chemin qui sert à signaler un problème serait le seul cassé.
    /// </summary>
    private static readonly List<string> EchecsDeChargement = new();

    /// <summary>
    /// Les assemblages applicatifs, chargés DEPUIS LE DISQUE et non depuis ce qui se
    /// trouve déjà en mémoire.
    ///
    /// Le projet de test référence les vingt-six modules : leurs DLL sont donc toutes
    /// déposées à côté de celle-ci. Les énumérer par leur nom de fichier donne une liste
    /// complète sans registre d'ancres à tenir à jour — et un module ajouté demain est
    /// balayé sans que personne ait à y penser, ce qui est exactement la propriété qu'on
    /// veut d'un garde-fou.
    /// </summary>
    private static readonly IReadOnlyList<Assembly> Assemblages = ChargerLesAssemblagesApplicatifs();

    private static IReadOnlyList<Assembly> ChargerLesAssemblagesApplicatifs()
    {
        var dossier = Path.GetDirectoryName(typeof(PreservationDesMisesAJourTests).Assembly.Location);
        if (string.IsNullOrEmpty(dossier))
        {
            return Array.Empty<Assembly>();
        }

        var charges = new List<Assembly>();
        foreach (var fichier in Directory.GetFiles(dossier, "Marketplace.Modules.*.Application.dll"))
        {
            try
            {
                charges.Add(Assembly.LoadFrom(fichier));
            }
            catch (Exception ex)
            {
                // On NOTE l'échec au lieu de l'avaler : un assemblage qu'on n'a pas pu
                // lire est un module entier qui échappe au balayage, et c'est précisément
                // le silence qui a rendu la première version inutile.
                EchecsDeChargement.Add($"{Path.GetFileName(fichier)} : {ex.Message}");
            }
        }

        return charges;
    }

    /// <summary>
    /// Tous les types d'un assemblage, y compris lorsque certains refusent de se charger.
    ///
    /// `GetTypes()` lève dès qu'UN type est illisible et ne rend alors rien du tout.
    /// `ReflectionTypeLoadException.Types` contient ceux qui ont abouti : on préfère
    /// balayer ce qu'on peut plutôt que de perdre un module sur un type annexe.
    /// </summary>
    private static IEnumerable<Type> TypesDe(Assembly assemblage)
    {
        try
        {
            return assemblage.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE TEST QUI SURVEILLE LES DEUX AUTRES.
    ///
    /// Les deux faits ci-dessous ne valent que si le balayage voit réellement tous les
    /// modules. Sans ce garde, un balayage devenu partiel les rendrait VERTS — c'est
    /// exactement ce qui s'est produit, et c'est le seul mode de défaillance d'un test
    /// d'architecture qu'on ne remarque jamais.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Le_balayage_voit_bien_tous_les_modules()
    {
        EchecsDeChargement.Should().BeEmpty(
            "un assemblage applicatif illisible est un module entier qui échappe au balayage, "
            + "et les deux tests suivants passeraient au vert sans l'avoir regardé");

        // La borne basse est délibérément lâche : elle n'a pas à suivre le nombre exact
        // de modules — ce serait une ligne à corriger à chaque ajout, donc une ligne
        // qu'on finit par corriger sans réfléchir. Elle n'existe que pour distinguer
        // « le dépôt » de « deux ou trois assemblages qui traînaient en mémoire », qui
        // est le cas réellement rencontré.
        Assemblages.Should().HaveCountGreaterThan(15,
            "le monolithe compte vingt-six modules et ce projet de test les référence tous ; "
            + "en voir une poignée signifie que la découverte des assemblages est cassée");
    }

    [Fact]
    public void Toute_commande_de_mise_a_jour_declare_ce_qu_elle_fait_d_un_champ_absent()
    {
        var commandes = Assemblages
            .SelectMany(TypesDe)
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.StartsWith("Update", StringComparison.Ordinal)
                && t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        commandes.Should().NotBeEmpty(
            "le balayage doit trouver des commandes — s'il n'en trouve aucune, c'est "
            + "la découverte des assemblages qui a cessé de fonctionner, pas le dépôt qui s'est vidé");

        var absentes = commandes.Where(n => !Registre.ContainsKey(n)).ToList();

        absentes.Should().BeEmpty(
            "toute commande de mise à jour doit déclarer ce qu'elle fait d'un champ que le "
            + "client n'envoie pas. Trois effacements silencieux de données sont nés de "
            + "cette question non posée — voir l'en-tête de ce fichier. Ajoutez une entrée "
            + "dans `Registre` après avoir vérifié que CHAQUE écran appelant recharge les "
            + "champs annulables avant d'enregistrer ; à défaut, déclarez-la `NonAudite`.");
    }

    [Fact]
    public void Le_registre_ne_decrit_aucune_commande_disparue()
    {
        var existantes = Assemblages
            .SelectMany(TypesDe)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Une entrée orpheline n'est pas un drame, mais elle ment : elle laisse croire
        // qu'une garantie est tenue sur du code qui n'existe plus.
        var orphelines = Registre.Keys.Where(n => !existantes.Contains(n)).ToList();

        orphelines.Should().BeEmpty(
            "ces commandes n'existent plus : retirez leur entrée du registre plutôt que de "
            + "laisser croire qu'une décision les couvre encore");
    }
}
