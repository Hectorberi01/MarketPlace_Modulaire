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
