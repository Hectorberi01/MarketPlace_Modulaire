using System.Text.Json;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Marketing.Contracts;
using Marketplace.Modules.Marketing.Domain.Content;

namespace Marketplace.Modules.Marketing.Application.Content;

/// <summary>
/// Le document légal publié dans un emplacement, analysé et typé.
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

    private readonly IContentBlockRepository _repository;

    public GetLegalDocumentQueryHandler(IContentBlockRepository repository)
        => _repository = repository;

    public async Task<Result<LegalDocumentView>> Handle(
        GetLegalDocumentQuery query, CancellationToken cancellationToken)
    {
        var blocs = await _repository.ListPublishedBySlotAsync(query.Slot, cancellationToken);

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
    /// La forme attendue dans <c>ContentBlock.Content</c>.
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
