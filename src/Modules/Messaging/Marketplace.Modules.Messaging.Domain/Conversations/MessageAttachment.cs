using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Messaging.Domain.Conversations;

/// <summary>
/// Pièce jointe d'un message (URL object storage + type). Entité ENFANT de Message,
/// exactement comme <see cref="MessageReaction"/>.
///
/// ⚠️ Pourquoi une table enfant plutôt qu'une colonne tableau/JSON : EF Core 8 traite
/// un <c>List&lt;string&gt;</c> mappé comme une « collection primitive » qu'il persiste
/// mais relit VIDE (bug observé ici). Le pattern « entité enfant » est, lui, éprouvé —
/// c'est celui des réactions, qui fonctionnent.
/// </summary>
public sealed class MessageAttachment : Entity<Guid>
{
    private MessageAttachment()
    {
    }

    internal MessageAttachment(Guid id, string url, AttachmentType type)
        : base(id)
    {
        Url = url;
        Type = type;
    }

    public string Url { get; private set; } = default!;
    public AttachmentType Type { get; private set; }

    /// <summary>Déduit le type d'une pièce jointe à partir de l'extension de son URL.</summary>
    public static AttachmentType InferType(string url)
    {
        var u = url.ToLowerInvariant();
        var query = u.IndexOf('?');
        if (query >= 0)
        {
            u = u[..query];
        }

        if (EndsWithAny(u, ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".bmp"))
        {
            return AttachmentType.Image;
        }
        if (EndsWithAny(u, ".mp4", ".mov", ".avi", ".webm", ".mkv"))
        {
            return AttachmentType.Video;
        }
        if (EndsWithAny(u, ".mp3", ".wav", ".ogg", ".m4a", ".aac"))
        {
            return AttachmentType.Audio;
        }
        if (EndsWithAny(u, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv"))
        {
            return AttachmentType.Document;
        }
        if (EndsWithAny(u, ".zip", ".rar", ".7z", ".tar", ".gz"))
        {
            return AttachmentType.Archive;
        }
        return AttachmentType.Other;
    }

    private static bool EndsWithAny(string value, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
