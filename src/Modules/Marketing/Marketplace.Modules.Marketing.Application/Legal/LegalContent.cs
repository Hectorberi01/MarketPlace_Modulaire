using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Marketing.Contracts;

namespace Marketplace.Modules.Marketing.Application.Legal;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// LE CORPS D'UN DOCUMENT LÉGAL : SA FORME, SA VALIDATION, SA SÉRIALISATION.
///
/// UN SEUL ENDROIT, ET C'EST LE POINT. La forme était auparavant analysée À LA LECTURE,
/// dans le handler qui sert le document. Conséquence : un JSON mal formé saisi dans la
/// console s'enregistrait sans broncher, et n'était découvert que par un acheteur au
/// moment d'accepter — qui voyait alors l'écran retomber sur la copie embarquée, sans
/// que personne côté plateforme n'apprenne quoi que ce soit.
///
/// La validation a donc été déplacée À L'ÉCRITURE. Un texte refusé au moment où
/// l'administrateur clique « Enregistrer » est un incident qu'il corrige en dix
/// secondes ; le même découvert en production ne se corrige qu'après l'avoir compris.
///
/// POURQUOI DES SECTIONS ET NON DU HTML. Le rendu mobile est un widget Flutter qui
/// parcourt { titre, paragraphes } ; un éditeur WYSIWYG produirait du HTML qu'il ne sait
/// pas afficher, et qu'il faudrait assainir côté web sous peine d'injection. Les
/// paragraphes acceptent du MARKDOWN SIMPLE — gras, italique, listes, liens — que
/// chaque surface rend avec son propre moteur. La structure reste vérifiable, et aucune
/// balise n'est jamais interprétée telle quelle.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class LegalContent
{
    /// <summary>Web : camelCase, comme la sérialisation des surfaces qui écrivent et lisent.</summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Bornes du raisonnable. Elles n'existent que pour empêcher l'absurde.</summary>
    private const int MaxSections = 200;

    private const int MaxTitre = 300;

    private const int MaxParagraphe = 20_000;

    /// <summary>
    /// La charge stockée dans <c>LegalDocumentVersion.Content</c>.
    ///
    /// TOUT EST NULLABLE ICI, et c'est délibéré : cette forme décrit du JSON qui peut
    /// venir d'une écriture faite à la main en base ou d'une ligne écrite par l'ancien
    /// modèle. Se déclarer non-nullable ne rendrait pas la donnée présente — cela
    /// rendrait seulement l'absence invisible au compilateur.
    /// </summary>
    public sealed record Charge(
        string? LastUpdated,
        string? Company,
        IReadOnlyList<ChargeSection>? Sections);

    public sealed record ChargeSection(string? Title, IReadOnlyList<string>? Body);

    /// <summary>
    /// Valide la forme rédigée dans la console et renvoie le JSON à stocker.
    ///
    /// Le NUMÉRO DE VERSION n'en fait plus partie : il vit dans sa propre colonne, où il
    /// est ordonné et vérifié. S'il traîne encore dans une charge reprise de l'ancien
    /// modèle, il est simplement ignoré — deux versions qui ne s'accordent pas seraient
    /// pires qu'une seule.
    /// </summary>
    public static Result<string> Serialiser(Charge charge)
    {
        var sections = charge.Sections;
        if (sections is not { Count: > 0 })
        {
            return Result.Failure<string>(Error.Validation(
                "marketing.legal.sections_required",
                "Le document doit comporter au moins une section."));
        }

        if (sections.Count > MaxSections)
        {
            return Result.Failure<string>(Error.Validation(
                "marketing.legal.too_many_sections",
                $"Le document dépasse {MaxSections} sections."));
        }

        var propres = new List<ChargeSection>(sections.Count);
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var titre = (section.Title ?? string.Empty).Trim();
            if (titre.Length == 0)
            {
                return Result.Failure<string>(Error.Validation(
                    "marketing.legal.section_title_required",
                    $"La section n° {i + 1} n'a pas de titre."));
            }

            if (titre.Length > MaxTitre)
            {
                return Result.Failure<string>(Error.Validation(
                    "marketing.legal.section_title_too_long",
                    $"Le titre de la section n° {i + 1} dépasse {MaxTitre} caractères."));
            }

            // Les paragraphes vides sont RETIRÉS, pas refusés : un éditeur en laisse
            // toujours traîner un à la fin, et faire échouer l'enregistrement pour cela
            // pousserait l'administrateur à se battre contre l'outil au lieu d'écrire.
            var paragraphes = (section.Body ?? Array.Empty<string>())
                .Select(p => (p ?? string.Empty).Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (paragraphes.Count == 0)
            {
                return Result.Failure<string>(Error.Validation(
                    "marketing.legal.section_body_required",
                    $"La section « {titre} » n'a aucun contenu."));
            }

            var trop = paragraphes.FirstOrDefault(p => p.Length > MaxParagraphe);
            if (trop is not null)
            {
                return Result.Failure<string>(Error.Validation(
                    "marketing.legal.paragraph_too_long",
                    $"Un paragraphe de la section « {titre} » dépasse {MaxParagraphe} caractères."));
            }

            propres.Add(new ChargeSection(titre, paragraphes));
        }

        var normalisee = new Charge(
            string.IsNullOrWhiteSpace(charge.LastUpdated) ? null : charge.LastUpdated!.Trim(),
            string.IsNullOrWhiteSpace(charge.Company) ? null : charge.Company!.Trim(),
            propres);

        return Result.Success(JsonSerializer.Serialize(normalisee, Options));
    }

    /// <summary>
    /// Relit une charge stockée.
    ///
    /// Un échec ici veut dire qu'une ligne a été écrite AVANT que la validation ne passe
    /// à l'écriture — ou à la main, en base. On ne sert alors RIEN : un demi-contrat est
    /// pire que pas de contrat du tout, parce qu'il se lit sans qu'on s'aperçoive de ce
    /// qui manque.
    /// </summary>
    public static Result<Charge> Analyser(string? contenu)
    {
        if (string.IsNullOrWhiteSpace(contenu))
        {
            return Result.Failure<Charge>(Error.Validation(
                "marketing.legal.malformed",
                "Le document publié est vide."));
        }

        Charge? charge;
        try
        {
            charge = JsonSerializer.Deserialize<Charge>(contenu, Options);
        }
        catch (JsonException)
        {
            return Result.Failure<Charge>(Error.Validation(
                "marketing.legal.malformed",
                "Le document publié n'est pas lisible."));
        }

        if (charge is null || charge.Sections is not { Count: > 0 })
        {
            return Result.Failure<Charge>(Error.Validation(
                "marketing.legal.malformed",
                "Le document publié n'a aucun contenu."));
        }

        return Result.Success(charge);
    }

    /// <summary>Traduit une charge en sections d'affichage.</summary>
    public static IReadOnlyList<LegalSectionView> EnSections(Charge charge)
        => (charge.Sections ?? Array.Empty<ChargeSection>())
            .Select(s => new LegalSectionView(
                s.Title ?? string.Empty,
                (IReadOnlyList<string>)(s.Body ?? Array.Empty<string>())))
            .ToList();
}
