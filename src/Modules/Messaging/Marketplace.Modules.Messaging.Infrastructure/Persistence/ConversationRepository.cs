using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Messaging.Domain.Conversations;

namespace Marketplace.Modules.Messaging.Infrastructure.Persistence;

internal sealed class ConversationRepository : IConversationRepository
{
    private readonly MessagingDbContext _dbContext;

    public ConversationRepository(MessagingDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations.AddAsync(conversation, cancellationToken);

    public async Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations
            .Include(c => c.Participants)
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListByParticipantAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations
            .Include(c => c.Participants)
            .Include(c => c.Messages)
            .Where(c => c.Participants.Any(p => p.UserId == userId))
            .OrderByDescending(c => c.LastMessageAtUtc)
            .ToListAsync(cancellationToken);
}
