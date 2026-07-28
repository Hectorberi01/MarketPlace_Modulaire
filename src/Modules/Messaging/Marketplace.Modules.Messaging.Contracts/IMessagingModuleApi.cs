namespace Marketplace.Modules.Messaging.Contracts;

/// <summary>
/// API in-process publique du module Messaging.
///
/// N'expose QUE ce dont les autres surfaces ont besoin — ici, une seule question :
/// « cet utilisateur a-t-il le droit d'écouter cette conversation ? ». Le ChatHub n'a pas
/// à savoir ce qu'est une conversation, ni à accéder à sa base.
/// </summary>
public interface IMessagingModuleApi
{
    /// <summary>
    /// Cet utilisateur est-il participant de cette conversation ?
    ///
    /// EXISTS sur index, sans charger la conversation ni ses messages : appelé à chaque
    /// ouverture d'un fil de discussion, il doit rester à quelques microsecondes.
    /// </summary>
    Task<bool> IsParticipantAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Participants d'une conversation. Sert à notifier le DESTINATAIRE d'un message :
    /// l'événement « message envoyé » ne porte que l'expéditeur, il faut donc retrouver
    /// l'autre partie pour lui envoyer le push.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListParticipantsAsync(Guid conversationId, CancellationToken cancellationToken = default);
}
