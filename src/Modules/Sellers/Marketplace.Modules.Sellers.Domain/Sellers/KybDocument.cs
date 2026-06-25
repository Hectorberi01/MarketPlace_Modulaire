using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Sellers.Domain.Sellers;

/// <summary>
/// Pièce justificative d'entreprise (registre de commerce, identité). Conformité
/// béninoise (cf. dossier, KybDocument). Le binaire est en object storage ; on ne
/// stocke ici que l'URL. Entité enfant de l'agrégat Seller.
/// </summary>
public sealed class KybDocument : Entity<Guid>
{
    private KybDocument()
    {
    }

    internal KybDocument(Guid id, KybDocumentType type, string fileUrl)
        : base(id)
    {
        Type = type;
        FileUrl = fileUrl;
        UploadedOnUtc = DateTime.UtcNow;
    }

    public KybDocumentType Type { get; private set; }
    public string FileUrl { get; private set; } = default!;
    public DateTime UploadedOnUtc { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }

    internal void MarkVerified() => VerifiedAtUtc = DateTime.UtcNow;
}
