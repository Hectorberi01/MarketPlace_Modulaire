using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Webhooks;

/// <summary>Identité forte d'un envoi de webhook.</summary>
public readonly record struct WebhookDeliveryId(Guid Value)
{
    public static WebhookDeliveryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public enum WebhookStatus
{
    /// <summary>En attente d'un envoi ou d'un réessai.</summary>
    Pending = 0,

    /// <summary>Accepté par le partenaire (2xx).</summary>
    Delivered = 1,

    /// <summary>Abandonné après épuisement des tentatives.</summary>
    Abandoned = 2
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN APPEL SORTANT VERS UN PARTENAIRE — PERSISTÉ AVANT D'ÊTRE TENTÉ.
///
/// POURQUOI UNE LIGNE EN BASE PLUTÔT QU'UN APPEL HTTP DIRECT
///
/// Envoyer depuis le handler d'événement paraît plus simple, et c'est faux pour
/// une raison décisive : le serveur du partenaire sera indisponible. Pas
/// « peut-être » — il le sera, régulièrement, parce que c'est un site marchand
/// béninois hébergé sans redondance. Un appel direct qui échoue est un fait perdu :
/// le partenaire ne saura JAMAIS que sa commande a été livrée, et son client
/// attendra un colis déjà reçu.
///
/// La file rend l'indisponibilité normale au lieu d'être fatale.
///
/// L'URL ET LE SECRET NE SONT PAS FIGÉS ICI — C'EST DÉLIBÉRÉ.
///
/// La tentation est de recopier l'URL au moment de la mise en file, « pour que le
/// fait parte là où il devait partir ». Mais la cause la plus fréquente d'un
/// webhook en échec est justement une URL erronée, et sa correction est de la
/// CHANGER. Figer l'URL ferait donc rejouer tous les envois en attente vers
/// l'adresse cassée, jusqu'à épuisement des tentatives.
///
/// On ne garde donc que le partenaire, et l'on relit sa configuration au moment
/// d'envoyer. Un partenaire qui répare son endpoint voit sa file se vider.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class WebhookDelivery : AggregateRoot<WebhookDeliveryId>
{
    /// <summary>
    /// Nombre total de tentatives avant abandon. Six tentatives avec le recul
    /// ci-dessous couvrent un peu plus de huit heures : assez pour traverser une
    /// panne de nuit, trop peu pour marteler indéfiniment un endpoint mort.
    /// </summary>
    public const int MaxAttempts = 6;

    private WebhookDelivery(
        WebhookDeliveryId id, Guid partnerId, Guid eventId, string eventType, string payload)
        : base(id)
    {
        PartnerId = partnerId;
        EventId = eventId;
        EventType = eventType;
        Payload = payload;
        Status = WebhookStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        NextAttemptAtUtc = CreatedAtUtc;
    }

    // Requis par EF Core.
    private WebhookDelivery()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    public Guid PartnerId { get; private set; }

    /// <summary>
    /// Identifiant de l'événement d'origine, transmis au partenaire.
    ///
    /// C'est ce qui lui permet de DÉDUPLIQUER. Un webhook réessayé après un délai
    /// dépassé arrive deux fois alors que le premier avait bien été traité — sans
    /// identifiant stable, le partenaire expédierait deux fois la même commande.
    /// Il ne change JAMAIS entre les tentatives.
    /// </summary>
    public Guid EventId { get; private set; }

    public string EventType { get; private set; }

    /// <summary>Corps JSON, figé à la mise en file : c'est LUI qui est signé.</summary>
    public string Payload { get; private set; }

    public WebhookStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime NextAttemptAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    /// <summary>Dernier code HTTP obtenu. Nul si la connexion n'a même pas abouti.</summary>
    public int? LastStatusCode { get; private set; }

    public string? LastError { get; private set; }

    public static Result<WebhookDelivery> Enqueue(
        Guid partnerId, Guid eventId, string? eventType, string? payload)
    {
        if (partnerId == Guid.Empty)
        {
            return Result.Failure<WebhookDelivery>(
                Error.Validation("webhook.partner_required", "Un webhook doit désigner son partenaire."));
        }

        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure<WebhookDelivery>(
                Error.Validation("webhook.payload_required", "Type et corps de l'événement sont requis."));
        }

        return new WebhookDelivery(WebhookDeliveryId.New(), partnerId, eventId, eventType.Trim(), payload);
    }

    public Result MarkDelivered(int statusCode)
    {
        if (Status is not WebhookStatus.Pending)
        {
            return Result.Failure(Error.Conflict("webhook.not_pending", "Cet envoi n'est plus en attente."));
        }

        Attempts++;
        Status = WebhookStatus.Delivered;
        DeliveredAtUtc = DateTime.UtcNow;
        LastStatusCode = statusCode;
        LastError = null;
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN ÉCHEC REPROGRAMME, OU ABANDONNE.
    ///
    /// Le recul est EXPONENTIEL : 1, 2, 4, 8, 16, 32 minutes… soit un peu plus de
    /// huit heures au total. Un intervalle fixe court transformerait une panne de
    /// deux heures chez le partenaire en plusieurs milliers de requêtes inutiles —
    /// c'est nous qui l'empêcherions de redémarrer.
    ///
    /// ⚠️ LA GIGUE N'EST PAS UN DÉTAIL.
    ///
    /// Sans elle, tous les webhooks mis en file pendant la panne réessaient à la
    /// MÊME seconde, indéfiniment. Le partenaire revient en ligne, reçoit d'un
    /// coup toute la file, retombe — et le troupeau se reforme au réessai suivant.
    /// Quelques secondes d'écart aléatoire suffisent à étaler la reprise.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result MarkFailed(int? statusCode, string? error, int jitterSeconds = 0)
    {
        if (Status is not WebhookStatus.Pending)
        {
            return Result.Failure(Error.Conflict("webhook.not_pending", "Cet envoi n'est plus en attente."));
        }

        Attempts++;
        LastStatusCode = statusCode;
        LastError = string.IsNullOrWhiteSpace(error) ? null : error.Trim()[..Math.Min(error.Trim().Length, 500)];

        if (Attempts >= MaxAttempts)
        {
            // ABANDONNÉ, et non « échoué » : le mot compte. La ligne reste en base
            // avec sa dernière erreur, et c'est le seul moyen de répondre à
            // « pourquoi n'ai-je jamais reçu la notification de cette commande ? ».
            Status = WebhookStatus.Abandoned;
            return Result.Success();
        }

        var backoffMinutes = Math.Pow(2, Attempts - 1);
        NextAttemptAtUtc = DateTime.UtcNow
            .AddMinutes(backoffMinutes)
            .AddSeconds(Math.Max(0, jitterSeconds));

        return Result.Success();
    }
}

/// <summary>Accès à la file des webhooks.</summary>
public interface IWebhookDeliveryRepository
{
    /// <summary>
    /// Envois dus, du plus ancien au plus récent.
    ///
    /// L'ORDRE COMPTE : un partenaire qui reçoit « livrée » avant « acceptée »
    /// verrait son suivi partir à l'envers. L'ordre n'est pas garanti de bout en
    /// bout — un réessai décale forcément — mais lire dans l'ordre de création
    /// évite de l'inverser gratuitement.
    /// </summary>
    Task<IReadOnlyList<WebhookDelivery>> ListDueAsync(
        DateTime nowUtc, int take = 50, CancellationToken cancellationToken = default);

    Task AddAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default);
}
