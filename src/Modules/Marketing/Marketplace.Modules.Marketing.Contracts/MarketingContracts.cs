namespace Marketplace.Modules.Marketing.Contracts;

/// <summary>Vue publique d'une campagne.</summary>
public sealed record CampaignSummary(
    Guid Id, string Name, string Type, DateTime StartUtc, DateTime EndUtc, string? TargetSegment, string Status, IReadOnlyList<Guid> PromotionIds);

/// <summary>Vue publique d'un bloc de contenu.</summary>
public sealed record ContentBlockSummary(
    Guid Id, string Type, string Slot, int Position, string Content, DateTime? StartUtc, DateTime? EndUtc, string Status);

/// <summary>
/// Un document légal PUBLIÉ, avec sa version.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA VERSION VOYAGE AVEC LE TEXTE, ET C'EST TOUTE LA RAISON DE CE TYPE.
///
/// La version est ce que le serveur enregistre quand quelqu'un accepte. Tant que
/// chaque application embarquait son texte ET décidait de la version qu'elle
/// faisait consigner, l'enregistrement ne désignait aucune rédaction précise :
/// deux binaires porteurs de textes différents produisaient la même ligne.
///
/// Ici les deux sont le MÊME enregistrement : la version ne peut pas désigner un
/// autre texte que celui qui l'accompagne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record LegalDocumentView(
    string Version,
    string? LastUpdated,
    string? Company,
    IReadOnlyList<LegalSectionView> Sections);

/// <summary>Une section titrée d'un document légal. <paramref name="Body"/> : ses paragraphes.</summary>
public sealed record LegalSectionView(string Title, IReadOnlyList<string> Body);

/// <summary>Une version d'un document légal, sans son texte (listes et historique).</summary>
public sealed record LegalVersionSummary(
    Guid Id,
    string Version,
    string Status,
    DateTime? EffectiveFromUtc,
    DateTime? PublishedOnUtc,
    Guid? PublishedBy,
    DateTime CreatedOnUtc);

/// <summary>
/// Un document légal et son historique, pour la console d'administration.
///
/// Le TEXTE n'y figure pas : la liste des documents en afficherait sept d'un coup, et
/// un contrat pèse plusieurs dizaines de milliers de caractères. Il se lit version par
/// version, par <c>GetLegalVersionQuery</c>.
/// </summary>
public sealed record LegalDocumentSummary(
    Guid Id,
    string Kind,
    string Slug,
    string Title,
    string Status,
    Guid? CurrentVersionId,
    string? CurrentVersion,
    DateTime? CurrentPublishedOnUtc,
    string? DraftVersion,
    IReadOnlyList<LegalVersionSummary> Versions);

/// <summary>Une version AVEC son texte : ce que charge l'éditeur.</summary>
public sealed record LegalVersionDetail(
    Guid DocumentId,
    string Slug,
    string Title,
    Guid VersionId,
    string Version,
    string Status,
    DateTime? EffectiveFromUtc,
    DateTime? PublishedOnUtc,
    Guid? PublishedBy,
    IReadOnlyList<LegalSectionView> Sections,
    string? LastUpdated,
    string? Company);

/// <summary>
/// Une acceptation recueillie : qui, quel document, QUELLE VERSION, quand, par quel geste.
/// C'est la ligne qu'on produit le jour d'un litige.
/// </summary>
public sealed record LegalAcceptanceView(
    Guid UserId,
    Guid DocumentId,
    string DocumentSlug,
    string DocumentTitle,
    Guid VersionId,
    string Version,
    DateTime AcceptedOnUtc,
    string Context);
