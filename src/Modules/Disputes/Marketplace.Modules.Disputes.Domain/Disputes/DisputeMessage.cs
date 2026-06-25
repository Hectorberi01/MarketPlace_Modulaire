using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Disputes.Domain.Disputes;

/// <summary>Message d'un litige, avec preuve photo éventuelle. Entité enfant de Dispute.</summary>
public sealed class DisputeMessage : Entity<Guid>
{
    private DisputeMessage()
    {
    }

    internal DisputeMessage(Guid id, Guid authorId, string body, string? photoUrl)
        : base(id)
    {
        AuthorId = authorId;
        Body = body;
        PhotoUrl = photoUrl;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid AuthorId { get; private set; }
    public string Body { get; private set; } = default!;
    public string? PhotoUrl { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
