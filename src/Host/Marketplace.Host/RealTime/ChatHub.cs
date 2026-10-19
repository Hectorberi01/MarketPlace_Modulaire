using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Marketplace.Modules.Messaging.Contracts;

namespace Marketplace.Host.RealTime;

/// <summary>
/// Hub de messagerie temps réel partagé par tous les hôtes (BFF Mobile et Seller). Type
/// unique : le backplane Redis route les groupes « conversation:{id} » entre processus,
/// donc un message envoyé via un BFF atteint les clients de l'autre. Le client rejoint le
/// groupe d'une conversation puis reçoit l'événement « message ».
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IMessagingModuleApi _messaging;

    public ChatHub(IMessagingModuleApi messaging) => _messaging = messaging;

    /// <summary>Nom de groupe SignalR pour une conversation.</summary>
    public static string Group(Guid conversationId) => $"conversation:{conversationId}";

    /// <summary>
    /// Groupe SignalR propre à un utilisateur. Sert à pousser les notifications
    /// « boîte de réception » (badge de non-lus) même quand l'utilisateur n'a
    /// aucune conversation ouverte.
    /// </summary>
    public static string UserGroup(Guid userId) => $"user:{userId}";

    /// <summary>
    /// À la connexion, rattache la connexion au groupe de l'utilisateur (extrait du JWT)
    /// pour l'événement « inbox ». Aucun contrôle nécessaire : l'identifiant vient du
    /// JETON, pas du client — on ne peut rejoindre que son propre groupe.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (CurrentUserId() is { } userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Rejoint le flux temps réel d'une conversation, <b>si et seulement si</b> l'appelant
    /// en est participant.
    ///
    /// Le message diffusé ne contient aujourd'hui que <c>{ conversationId }</c> : un signal,
    /// pas le contenu. Le contrôle vaut quand même, parce que ce payload minimal est une
    /// décision d'aujourd'hui — le jour où quelqu'un y ajoute le corps du message, l'absence
    /// de contrôle devient une fuite de contenu sans que cette ligne bouge.
    /// </summary>
    public async Task JoinConversation(string conversationId)
    {
        // L'identifiant arrive en `string` et était concaténé TEL QUEL dans le nom de groupe.
        // On le parse : ce qui n'est pas un GUID ne désigne aucune conversation, et n'a donc
        // rien à faire dans un nom de groupe.
        if (!Guid.TryParse(conversationId, out var id))
        {
            throw new HubException("Conversation introuvable.");
        }

        if (CurrentUserId() is not { } userId)
        {
            throw new HubException("Session invalide.");
        }

        if (!await _messaging.IsParticipantAsync(id, userId, Context.ConnectionAborted))
        {
            // Message VOLONTAIREMENT identique à celui du GUID invalide. Distinguer
            // « conversation inexistante » de « conversation dont vous n'êtes pas membre »
            // transformerait ce hub en ORACLE : on énumérerait les conversations existantes
            // en les essayant une à une, et on saurait lesquelles sont réelles.
            throw new HubException("Conversation introuvable.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(id));
    }

    /// <summary>
    /// Quitte le flux d'une conversation. Aucun contrôle de participation, délibérément :
    /// quitter un groupe qu'on n'aurait pas dû rejoindre n'est pas une attaque, et exiger
    /// une autorisation pour se retirer empêcherait quelqu'un de partir.
    /// </summary>
    public Task LeaveConversation(string conversationId)
        => Guid.TryParse(conversationId, out var id)
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(id))
            : Task.CompletedTask;

    /// <summary>Identifiant de l'appelant, lu dans le JWT (claim « sub »).</summary>
    private Guid? CurrentUserId()
    {
        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;

        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
