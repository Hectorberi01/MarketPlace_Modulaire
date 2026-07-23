namespace Marketplace.Modules.Messaging.Domain.Conversations;

/// <summary>
/// Type d'une pièce jointe de message. Déduit de l'extension de l'URL à la création,
/// il permet à l'app d'adapter le rendu (image plein cadre, lecteur vidéo/audio, icône
/// de document…) sans avoir à re-télécharger le fichier pour deviner sa nature.
/// </summary>
public enum AttachmentType
{
    Image = 1,
    Video = 2,
    Audio = 3,
    Document = 4,
    Archive = 5,
    Other = 6,
}
