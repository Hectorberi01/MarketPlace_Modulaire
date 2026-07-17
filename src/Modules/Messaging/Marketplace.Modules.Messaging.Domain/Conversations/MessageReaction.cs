using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Messaging.Domain.Conversations;

/// <summary>
/// Réaction d'un participant à un message (emoji). Entité enfant de Message :
/// une seule réaction par personne et par message (re-cliquer le même emoji la
/// retire, en cliquer un autre la remplace) — modèle WhatsApp.
/// </summary>
public sealed class MessageReaction : Entity<Guid>
{
    private MessageReaction()
    {
    }

    internal MessageReaction(Guid id, Guid userId, string emoji)
        : base(id)
    {
        UserId = userId;
        Emoji = emoji;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Emoji { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    internal void ChangeEmoji(string emoji)
    {
        Emoji = emoji;
        CreatedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>
/// Palette d'emojis autorisés. Le jeu est FERMÉ et validé côté serveur : accepter
/// une chaîne libre ouvrirait la porte à du contenu arbitraire stocké et réaffiché
/// (risque d'injection), et ferait exploser la cardinalité côté statistiques.
/// </summary>
public static class MessageReactions
{
    public static readonly IReadOnlyList<string> Allowed = new[] { "👍", "❤️", "😂", "😮", "😢", "🙏" };

    public static bool IsAllowed(string? emoji)
        => !string.IsNullOrWhiteSpace(emoji) && Allowed.Contains(emoji);
}
