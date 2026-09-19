using System.Text.Json;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Marketing.Application.Legal;
using Marketplace.Modules.Marketing.Contracts;
using Marketplace.Modules.Marketing.Domain.Content;
using Marketplace.Modules.Marketing.Domain.Legal;

namespace Marketplace.Modules.Marketing.Application.Content;

/// <summary>
/// Le document légal en vigueur à une adresse, analysé et typé.
///
/// ═════════════════════════════════════════════════════════════════════════════════════
/// POURQUOI CETTE LECTURE VIT ICI ET NON DANS UN BFF.
///
/// Elle a d'abord été écrite dans le BFF acheteur. Le jour où le vendeur a eu besoin du
/// même service, la recopier aurait créé DEUX analyses d'un même format de bloc — et la
/// question n'aurait pas été de savoir SI elles divergent, mais quand : on ajoute un
/// champ ici, une tolérance là, et le second exemplaire reste en arrière sans que la
/// lecture d'aucun des deux fichiers ne le révèle.
///
/// Le format d'un document légal est une affaire du module Contenu, pas d'une surface.
/// ═════════════════════════════════════════════════════════════════════════════════════
///
/// ═════════════════════════════════════════════════════════════════════════════════════
/// DEUX SOURCES, LE TEMPS DE LA BASCULE — ET UN ORDRE QUI N'EST PAS ARBITRAIRE.
///
/// 1. LE MODÈLE VERSIONNÉ (`LegalDocument`), s'il connaît cette adresse. Il sert la
///    version EN VIGUEUR à l'instant présent : publiée, et dont la date d'effet est
///    passée. Le numéro de version vient de sa colonne, où il est ordonné et vérifié.
///
/// 2. À DÉFAUT, l'ancien bloc de contenu, dont la version était une chaîne saisie à la
///    main À L'INTÉRIEUR du JSON. C'est exactement ce qu'on quitte ; le repli existe
///    pour que rien ne casse tant que les documents n'ont pas été repris, et il a
///    vocation à disparaître.
///
/// L'ordre compte : dès qu'un document versionné existe pour une adresse, c'est LUI qui
/// fait foi. Sinon un ancien bloc oublié continuerait de servir un texte abrogé,
/// silencieusement.
/// ═════════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record GetLegalDocumentQuery(string Slot) : IQuery<LegalDocumentView>;

internal sealed class GetLegalDocumentQueryHandler
    : IQueryHandler<GetLegalDocumentQuery, LegalDocumentView>
{
    /// <summary>
    /// Web : camelCase, comme la sérialisation des applications qui le rédigent. Les deux
    /// doivent s'accorder, sinon les champs arrivent à null sans la moindre erreur.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILegalDocumentRepository _legalDocuments;
    private readonly IContentBlockRepository _contentBlocks;

    public GetLegalDocumentQueryHandler(
        ILegalDocumentRepository legalDocuments,
        IContentBlockRepository contentBlocks)
    {
        _legalDocuments = legalDocuments;
        _contentBlocks = contentBlocks;
    }

    public async Task<Result<LegalDocumentView>> Handle(
        GetLegalDocumentQuery query, CancellationToken cancellationToken)
    {
        var adresse = (query.Slot ?? string.Empty).Trim();

        var document = await _legalDocuments.GetBySlugAsync(adresse, cancellationToken);
        if (document is not null)
        {
            return LireDocumentVersionne(document);
        }

        return await LireAncienBlocAsync(adresse, cancellationToken);
    }

    private static Result<LegalDocumentView> LireDocumentVersionne(LegalDocument document)
    {
        if (document.Status == LegalDocumentStatus.Archived)
        {
            return Result.Failure<LegalDocumentView>(Error.NotFound(
                "marketing.legal.not_published",
                "Ce document a été retiré."));
        }

        var version = document.EffectiveVersion(DateTime.UtcNow);
        if (version is null)
        {
            // Créé mais jamais publié, ou publié pour une date d'effet encore à venir.
            // Dans les deux cas l'appelant doit se rabattre sur sa copie embarquée.
            return Result.Failure<LegalDocumentView>(Error.NotFound(
                "marketing.legal.not_published",
                "Aucune version en vigueur pour ce document."));
        }

        var charge = LegalContent.Analyser(version.Content);
        if (charge.IsFailure)
        {
            return Result.Failure<LegalDocumentView>(charge.Error);
        }

        return Result.Success(new LegalDocumentView(
            version.Number.ToString(),
            charge.Value.LastUpdated,
            charge.Value.Company,
            LegalContent.EnSections(charge.Value)));
    }

    private async Task<Result<LegalDocumentView>> LireAncienBlocAsync(
        string slot, CancellationToken cancellationToken)
    {
        var blocs = await _contentBlocks.ListPublishedBySlotAsync(slot, cancellationToken);

        // UN SEUL BLOC PAR DOCUMENT, le premier par position. Un texte contractuel n'est
        // pas une liste de fragments qu'on assemble : le découper en blocs rendrait
        // possible d'en publier la moitié, et personne ne s'en apercevrait à la lecture
        // d'un écran qui affiche ce qu'il a reçu.
        var bloc = blocs.OrderBy(b => b.Position).FirstOrDefault();
        if (bloc is null || string.IsNullOrWhiteSpace(bloc.Content))
        {
            return Result.Failure<LegalDocumentView>(Error.NotFound(
                "marketing.legal.not_published",
                "Aucun document légal publié pour cet emplacement."));
        }

        ChargeDocument? charge;
        try
        {
            charge = JsonSerializer.Deserialize<ChargeDocument>(bloc.Content, JsonOptions);
        }
        catch (JsonException)
        {
            // Bloc mal formé : on ne sert PAS un document partiel. L'appelant retombe sur
            // sa copie embarquée, qui est au moins cohérente. Servir la moitié d'un
            // contrat serait pire que ne rien servir.
            return Result.Failure<LegalDocumentView>(Error.Validation(
                "marketing.legal.malformed",
                "Le document légal publié n'est pas lisible."));
        }

        if (charge is null
            || string.IsNullOrWhiteSpace(charge.Version)
            || charge.Sections is not { Count: > 0 })
        {
            return Result.Failure<LegalDocumentView>(Error.Validation(
                "marketing.legal.malformed",
                "Le document légal publié n'a ni version ni contenu."));
        }

        IReadOnlyList<LegalSectionView> sections = charge.Sections
            .Select(s => new LegalSectionView(
                s.Title ?? string.Empty,
                (IReadOnlyList<string>)(s.Body ?? new List<string>())))
            .ToList();

        return Result.Success(new LegalDocumentView(
            charge.Version!, charge.LastUpdated, charge.Company, sections));
    }

    /// <summary>
    /// La forme attendue dans <c>ContentBlock.Content</c> — L'ANCIEN MODÈLE.
    ///
    /// C'est du JSON saisi dans le back-office, donc NON VALIDÉ par un schéma : le
    /// handler refuse ce qui n'a ni version ni sections plutôt que de faire confiance.
    /// </summary>
    private sealed record ChargeDocument(
        string? Version,
        string? LastUpdated,
        string? Company,
        List<ChargeSection>? Sections);

    private sealed record ChargeSection(string? Title, List<string>? Body);
}
