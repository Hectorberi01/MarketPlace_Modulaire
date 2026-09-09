using HbaExpress.Dispatch.Api.Security;
using HbaExpress.Dispatch.Application.Couriers;
using HbaExpress.Dispatch.Domain.Couriers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HbaExpress.Dispatch.Api.Realtime;

/// <summary>
/// Le canal temps réel vers le téléphone du coursier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HUB ACCÉLÈRE LE DISPATCH, IL NE LE CONDITIONNE PAS.
///
/// Une offre existe en base dès sa création, et un coursier peut l'accepter par simple
/// appel HTTP après l'avoir lue sur GET /api/courier/offers. Le hub sert à ne pas
/// attendre ce sondage : vingt-cinq secondes de fenêtre d'offre s'accommodent mal d'un
/// rafraîchissement toutes les dix.
///
/// C'est pourquoi il arrive EN DERNIER, et non en premier. Construit avant l'hôte et les
/// routes, il aurait donné un système dont on ne peut rien affirmer tant que tout est
/// branché ; construit maintenant, il se pose sur quelque chose qui marche déjà sans lui.
///
/// LE GROUPE VIENT DU JETON, ET DE LUI SEUL. Aucune méthode appelable par le client ne
/// permet de rejoindre un groupe : le rattachement se fait à la connexion, à partir de la
/// claim « sub ». C'est la leçon de ChatHub côté monolithe, où un `JoinConversation`
/// naïvement écrit laissait n'importe qui écouter la conversation de n'importe qui. Ici,
/// la faute équivalente serait d'entendre — et de pouvoir accepter — les courses d'un
/// autre coursier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Authorize]
public sealed class CourierHub : Hub
{
    /// <summary>Le groupe d'un coursier. Une connexion par appareil, un groupe par personne.</summary>
    public static string Group(CourierId courierId) => $"courier:{courierId.Value}";

    /// <summary>Événement poussé à l'arrivée d'une offre.</summary>
    public const string OfferEvent = "offer";

    /// <summary>Événement poussé quand une offre n'est plus valable.</summary>
    public const string OfferWithdrawnEvent = "offerWithdrawn";

    private readonly CourierService _couriers;
    private readonly ILogger<CourierHub> _logger;

    public CourierHub(CourierService couriers, ILogger<CourierHub> logger)
    {
        _couriers = couriers;
        _logger = logger;
    }

    /// <summary>
    /// À la connexion : du COMPTE au COURSIER, puis rattachement à son groupe.
    ///
    /// Un jeton d'acheteur ou de vendeur est cryptographiquement valable ici — les hôtes
    /// partagent la clé de signature. Il ne correspond simplement à aucun coursier, et sa
    /// connexion est REFUSÉE plutôt que laissée ouverte à ne rien recevoir : une
    /// connexion persistante inutile consomme une ressource et masque un vrai problème
    /// d'intégration côté client.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (CourierPrincipal.ResolveUserId(Context.User!) is not { } userId)
        {
            Context.Abort();
            return;
        }

        var courierId = await _couriers.ResolveByUserAsync(userId, Context.ConnectionAborted);
        if (courierId is null)
        {
            _logger.LogWarning(
                "Connexion au hub refusée : le compte {UserId} n'est rattaché à aucun coursier.", userId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(courierId.Value), Context.ConnectionAborted);

        _logger.LogInformation(
            "Coursier {CourierId} connecté au hub ({ConnectionId}).", courierId.Value, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Aucune sortie de groupe explicite : SignalR retire la connexion de ses groupes
    /// quand elle se ferme. Le journal, lui, sert à voir les coupures — sur un réseau
    /// mobile béninois, elles sont la norme et non l'exception.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Connexion {ConnectionId} fermée.", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
