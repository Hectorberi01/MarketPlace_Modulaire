namespace Marketplace.Modules.Marketing.Domain.Content;

public interface IContentBlockRepository
{
    Task AddAsync(ContentBlock block, CancellationToken cancellationToken = default);

    Task<ContentBlock?> GetByIdAsync(ContentBlockId id, CancellationToken cancellationToken = default);

    /// <summary>Blocs publiés d'un slot (rendu vitrine), triés par position.</summary>
    Task<IReadOnlyList<ContentBlock>> ListPublishedBySlotAsync(string slot, CancellationToken cancellationToken = default);
}
