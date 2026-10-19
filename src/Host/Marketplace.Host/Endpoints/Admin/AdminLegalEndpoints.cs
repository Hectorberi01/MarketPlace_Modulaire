using System.Security.Claims;
using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Marketing.Application.Legal;
using Marketplace.Modules.Marketing.Contracts;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// BFF Admin — « Contenus &amp; juridique ».
///
/// ═════════════════════════════════════════════════════════════════════════════════════
/// CE GROUPE EST SÉPARÉ DE `/admin/content`, ET CE N'EST PAS UN DÉTAIL DE RANGEMENT.
///
/// `/admin/content` pilote des blocs éditoriaux : une bannière, une mise en avant, une
/// FAQ. On les crée, on les modifie, on les supprime — et c'est très bien, parce que
/// personne ne signe une bannière.
///
/// Un document juridique n'a PAS ces gestes-là. Il n'a ni « modifier » ni « supprimer » :
/// il a « écrire un brouillon », « publier une version », « archiver ». La différence
/// n'est pas de vocabulaire, elle est de nature — une modification en place efface la
/// rédaction que des gens ont acceptée. Deux jeux de routes séparés rendent l'erreur
/// impossible à commettre par inadvertance.
/// ═════════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class AdminLegalEndpoints
{
    public static IEndpointRouteBuilder MapAdminLegalEndpoints(this IEndpointRouteBuilder app)
    {
        var legal = app.MapAdminGroup("/api/admin/legal", "Admin · Juridique");

        legal.MapGet("/", ListAsync);
        legal.MapPost("/", CreateAsync);
        legal.MapPut("/{id:guid}/title", RenameAsync);

        // La version : lue par son identifiant, ou à défaut celle qu'il faut ouvrir —
        // le brouillon s'il existe, sinon le texte en vigueur.
        legal.MapGet("/{id:guid}/version", GetVersionAsync);
        legal.MapGet("/{id:guid}/versions/{versionId:guid}", GetSpecificVersionAsync);

        legal.MapPut("/{id:guid}/draft", SaveDraftAsync);
        legal.MapPost("/{id:guid}/publish", PublishAsync);
        legal.MapPost("/{id:guid}/archive", ArchiveAsync);

        // Ce qu'une personne a accepté. Route d'ADMINISTRATION, jamais exposée ailleurs :
        // c'est une pièce de dossier, pas une donnée d'affichage.
        legal.MapGet("/acceptances/{userId:guid}", ListAcceptancesAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListLegalDocumentsQuery(), ct)).Match(d => Results.Ok(d));

    private static async Task<IResult> CreateAsync(
        CreateLegalDocumentRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new CreateLegalDocumentCommand(request.Kind, request.Slug, request.Title), ct))
            .Match(id => Results.Ok(new { documentId = id }));

    private static async Task<IResult> RenameAsync(
        Guid id, RenameLegalDocumentRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RenameLegalDocumentCommand(id, request.Title), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> GetVersionAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetLegalVersionQuery(id, null), ct)).Match(v => Results.Ok(v));

    private static async Task<IResult> GetSpecificVersionAsync(
        Guid id, Guid versionId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetLegalVersionQuery(id, versionId), ct)).Match(v => Results.Ok(v));

    private static async Task<IResult> SaveDraftAsync(
        Guid id, SaveLegalDraftRequest request, ISender sender, CancellationToken ct)
    {
        // `??` EXIGE UNE CONVERSION ENTRE LES DEUX OPÉRANDES, et il n'en existe aucune
        // entre `List<T>` et `T[]` : écrire `request.Sections ?? Array.Empty<...>()` ne
        // compile pas. On reste donc dans le même type des deux côtés.
        IReadOnlyList<LegalSectionView> sections = (request.Sections ?? new List<LegalSectionRequest>())
            .Select(s => new LegalSectionView(
                s.Title ?? string.Empty,
                (IReadOnlyList<string>)(s.Body ?? new List<string>())))
            .ToList();

        var resultat = await sender.Send(
            new SaveLegalDraftCommand(id, request.Version, request.LastUpdated, request.Company, sections), ct);

        return resultat.Match(versionId => Results.Ok(new { versionId }));
    }

    /// <summary>
    /// Publie le brouillon.
    ///
    /// L'AUTEUR VIENT DU JETON, JAMAIS DU CORPS DE LA REQUÊTE. `publishedBy` est une
    /// pièce du dossier : si le client pouvait l'écrire, il pourrait attribuer une
    /// publication à un collègue, et la colonne cesserait de valoir quoi que ce soit.
    /// </summary>
    private static async Task<IResult> PublishAsync(
        Guid id,
        PublishLegalRequest? request,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct)
    {
        if (AdminBff.CurrentUserId(user) is not { } adminId)
        {
            return Results.Unauthorized();
        }

        var resultat = await sender.Send(
            new PublishLegalDraftCommand(id, adminId, request?.EffectiveFromUtc), ct);

        return resultat.Match(versionId => Results.Ok(new { versionId }));
    }

    private static async Task<IResult> ArchiveAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ArchiveLegalDocumentCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ListAcceptancesAsync(
        Guid userId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListUserLegalAcceptancesQuery(userId), ct)).Match(a => Results.Ok(a));

    // ═════════════════════════════════════════════════════════════════════════════════
    // CORPS DE REQUÊTE
    // ═════════════════════════════════════════════════════════════════════════════════

    public sealed record CreateLegalDocumentRequest(string Kind, string Slug, string Title);

    public sealed record RenameLegalDocumentRequest(string Title);

    public sealed record SaveLegalDraftRequest(
        string Version,
        string? LastUpdated,
        string? Company,
        List<LegalSectionRequest>? Sections);

    public sealed record LegalSectionRequest(string? Title, List<string>? Body);

    /// <summary>
    /// Corps FACULTATIF. Sans date d'effet, le texte fait foi dès la publication — le cas
    /// courant, et celui qu'un administrateur pressé obtiendra sans rien saisir.
    /// </summary>
    public sealed record PublishLegalRequest(DateTime? EffectiveFromUtc);
}
