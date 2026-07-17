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
            // Les collections enfants des messages DOIVENT être chargées : sans elles,
            // la projection verrait zéro réaction et aucun masquage (« supprimer pour
            // moi » n'aurait plus aucun effet visible).
            .Include(c => c.Messages).ThenInclude(m => m.Reactions)
            .Include(c => c.Messages).ThenInclude(m => m.HiddenFor)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListByParticipantAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Conversations
            .Include(c => c.Participants)
            // Les collections enfants des messages DOIVENT être chargées : sans elles,
            // la projection verrait zéro réaction et aucun masquage (« supprimer pour
            // moi » n'aurait plus aucun effet visible).
            .Include(c => c.Messages).ThenInclude(m => m.Reactions)
            .Include(c => c.Messages).ThenInclude(m => m.HiddenFor)
            .Where(c => c.Participants.Any(p => p.UserId == userId))
            .OrderByDescending(c => c.LastMessageAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> IsParticipantAsync(ConversationId id, Guid userId, CancellationToken cancellationToken = default)
        // Pas d'Include, pas de ToList : un EXISTS. Cette question est posée à chaque
        // ouverture de conversation dans l'app — elle ne doit rien coûter.
        => await _dbContext.Conversations
            .AnyAsync(c => c.Id == id && c.Participants.Any(p => p.UserId == userId), cancellationToken);
}
