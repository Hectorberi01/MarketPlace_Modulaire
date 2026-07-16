using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Marketplace.Modules.Messaging.Contracts;

namespace Marketplace.Api.RealTime;

/// <summary>
/// Hub de messagerie temps réel partagé par tous les hôtes (BFF Mobile &amp; Seller).
/// Type unique → le backplane Redis route les groupes « conversation:{id} » entre
/// les processus, donc un message envoyé via un BFF atteint les clients connectés
/// à l'autre BFF. Le client rejoint le groupe d'une conversation puis reçoit
/// l'événement « message » à chaque nouveau message.
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
    /// À la connexion, on rattache automatiquement la connexion au groupe de
    /// l'utilisateur (extrait du JWT) — il reçoit ainsi l'événement « inbox »
    /// pour rafraîchir son badge de messages non lus.
    ///
    /// Aucun contrôle nécessaire : l'identifiant vient du JETON, pas du client. On ne peut
    /// donc rejoindre que son propre groupe.
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
    /// Rejoint le flux temps réel d'une conversation — <b>si et seulement si</b> l'appelant
    /// en est participant.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CETTE VÉRIFICATION N'EXISTAIT PAS. N'IMPORTE QUI POUVAIT ÉCOUTER N'IMPORTE QUI.
    ///
    /// L'ancienne implémentation tenait en une ligne :
    ///
    ///     public Task JoinConversation(string conversationId)
    ///         =&gt; Groups.AddToGroupAsync(Context.ConnectionId, $"conversation:{conversationId}");
    ///
    /// Aucun contrôle. Tout utilisateur AUTHENTIFIÉ — donc n'importe quel acheteur — pouvait
    /// rejoindre le groupe SignalR de N'IMPORTE QUELLE conversation, pourvu qu'il en obtienne
    /// l'identifiant. L'identifiant n'était même pas parsé : il était concaténé tel quel dans
    /// le nom du groupe.
    /// ═════════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// <b>Ce que cela fuyait, exactement — et il faut le dire tel quel.</b> Le message
    /// diffusé au groupe ne contient que <c>{ conversationId }</c> : un SIGNAL, pas le
    /// contenu. L'intrus apprenait donc « un message vient d'être échangé dans cette
    /// conversation », et rien de plus — le corps des messages reste protégé par l'API, qui
    /// vérifie la participation. C'est une fuite de MÉTADONNÉES, pas de contenu.
    /// </para>
    ///
    /// <para>
    /// <b>Pourquoi la corriger quand même.</b> Parce que ce payload minimal est une décision
    /// d'aujourd'hui, pas une propriété du système. Le jour où quelqu'un se dira — très
    /// raisonnablement — « pourquoi refaire un appel API alors qu'on pousse déjà
    /// l'événement ? » et enrichira le message avec son corps, cette absence de contrôle
    /// deviendra une fuite de CONTENU. Sans que personne ne touche à cette ligne, et sans
    /// que rien ne le signale. <b>L'autorisation se pose à la porte, elle ne dépend pas de
    /// ce qu'on décide d'y faire passer.</b>
    /// </para>
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
    /// Quitte le flux d'une conversation.
    ///
    /// Aucun contrôle de participation ici, et c'est délibéré : quitter un groupe qu'on
    /// n'aurait pas dû rejoindre n'est pas une attaque, c'est une bonne nouvelle. Exiger une
    /// autorisation pour se RETIRER serait un moyen d'empêcher quelqu'un de partir.
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
