using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Marketplace.Api.RealTime;

/// <summary>
/// Hub de messagerie temps réel partagé par tous les hôtes (BFF Mobile & Seller).
/// Type unique → le backplane Redis route les groupes « conversation:{id} » entre
/// les processus, donc un message envoyé via un BFF atteint les clients connectés
/// à l'autre BFF. Le client rejoint le groupe d'une conversation puis reçoit
/// l'événement « message » à chaque nouveau message.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    /// <summary>Nom de groupe SignalR pour une conversation.</summary>
    public static string Group(Guid conversationId) => $"conversation:{conversationId}";

    /// <summary>
    /// Groupe SignalR propre à un utilisateur. Sert à pousser les notifications
    /// « boîte de réception » (badge de non-lus) même quand l'utilisateur n'a
    /// aucune conversation ouverte.
    /// </summary>
    public static string UserGroup(Guid userId) => $"user:{userId}";

    /// <summary>
    /// À la connexion, on rattache automatiquement la connexion au groupe de
    /// l'utilisateur (extrait du JWT) — il reçoit ainsi l'événement « inbox »
    /// pour rafraîchir son badge de messages non lus.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;
        if (Guid.TryParse(sub, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }

    public Task JoinConversation(string conversationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");

    public Task LeaveConversation(string conversationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
}
