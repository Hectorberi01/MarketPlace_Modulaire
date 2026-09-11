using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Disputes.Domain.Disputes.Events;

namespace Marketplace.Modules.Disputes.Domain.Disputes;

/// <summary>
/// Litige acheteur ↔ vendeur sur une commande. La plateforme arbitre ; l'escrow
/// est gelé le temps de la décision (cf. dossier). Agrégat racine : possède son
/// fil de messages et preuves.
/// </summary>
public sealed class Dispute : AggregateRoot<DisputeId>
{
    private readonly List<DisputeMessage> _messages = new();

    private Dispute()
    {
    }

    private Dispute(DisputeId id, Guid orderId, Guid raisedBy, DisputeType type)
        : base(id)
    {
        OrderId = orderId;
        RaisedBy = raisedBy;
        Type = type;
        Status = DisputeStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public Guid RaisedBy { get; private set; }
    public DisputeType Type { get; private set; }
    public DisputeStatus Status { get; private set; }
    public DisputeResolution? Resolution { get; private set; }
    /// <summary>Montant remboursé — renseigné uniquement pour un remboursement partiel.</summary>
    public decimal? RefundAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    public IReadOnlyCollection<DisputeMessage> Messages => _messages.AsReadOnly();

    public static Result<Dispute> Open(Guid orderId, Guid raisedBy, DisputeType type, string initialMessage, string? photoUrl)
    {
        if (orderId == Guid.Empty || raisedBy == Guid.Empty)
        {
            return Error.Validation("disputes.refs_required", "Commande et auteur sont obligatoires.");
        }

        if (string.IsNullOrWhiteSpace(initialMessage))
        {
            return Error.Validation("disputes.message_required", "Le motif du litige est obligatoire.");
        }

        if (!PreuveRecevable(photoUrl, out var preuve))
        {
            return Error.Validation("disputes.photo_url_invalid", PreuveRefusee);
        }

        var dispute = new Dispute(DisputeId.New(), orderId, raisedBy, type);
        dispute._messages.Add(new DisputeMessage(Guid.NewGuid(), raisedBy, initialMessage.Trim(), preuve));
        dispute.Raise(new DisputeOpenedDomainEvent(dispute.Id.Value, orderId, raisedBy, type.ToString()));
        return dispute;
    }

    public Result AddMessage(Guid authorId, string body, string? photoUrl)
    {
        if (Status is DisputeStatus.Resolved)
        {
            return Result.Failure(Error.Conflict("disputes.resolved", "Un litige tranché ne reçoit plus de messages."));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result.Failure(Error.Validation("disputes.message_required", "Le message ne peut pas être vide."));
        }

        if (!PreuveRecevable(photoUrl, out var preuve))
        {
            return Result.Failure(Error.Validation("disputes.photo_url_invalid", PreuveRefusee));
        }

        _messages.Add(new DisputeMessage(Guid.NewGuid(), authorId, body.Trim(), preuve));
        return Result.Success();
    }

    private const string PreuveRefusee =
        "La pièce jointe doit être une adresse https:// complète.";

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CETTE CHAÎNE DEVIENT UN LIEN CLIQUABLE DANS LA CONSOLE D'ADMINISTRATION.
    ///
    /// Elle n'était vérifiée NULLE PART. `OpenDisputeCommandValidator` contrôle
    /// l'auteur, la commande, le type et le message — jamais la preuve ; et
    /// `AddDisputeMessageCommand` n'a aucun validateur. N'importe quel client ou
    /// vendeur authentifié pouvait donc déposer la chaîne de son choix, et l'écran
    /// Modération la rendait telle quelle dans un `href`.
    ///
    /// `javascript:` dans un `href` est un point d'exécution connu, et la CSP de la
    /// console ne le bloque pas : son `script-src` porte `'unsafe-inline'`, qui
    /// autorise précisément ces URL. Le script tournait alors dans l'origine de la
    /// console — où `/api/bff/*` est un proxy DÉJÀ authentifié : suspension de
    /// boutique, validation de KYB, remboursement vers un numéro choisi.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POURQUOI ICI, ET PAS DANS UN VALIDATEUR DE COMMANDE.
    ///
    /// Parce que c'est l'absence d'un validateur qui a créé le trou. Deux commandes
    /// portent cette valeur, une seule avait un validateur, et la suivante n'en aura
    /// peut-être pas non plus. L'agrégat est le seul passage obligé des deux chemins.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// CE QUE CE GARDE NE FAIT PAS : il n'impose pas d'HÔTE.
    ///
    /// Le module Catalogue a déjà la bonne réponse pour cela — `IMediaUrlPolicy`, une
    /// liste blanche adossée aux hôtes de stockage réellement configurés. Elle vit
    /// dans Catalogue, et la frontière de module interdit d'y toucher d'ici ; surtout,
    /// aucune route de téléversement n'existe pour les litiges, donc on ignore d'où
    /// viennent les preuves déjà déposées par les applications. Restreindre l'hôte
    /// sans le savoir casserait un parcours client en production.
    ///
    /// Exiger `https://` ferme entièrement l'exécution : `javascript:`, `data:` et
    /// `vbscript:` sont des URI absolues valides — `Uri.TryCreate` les accepte — et
    /// c'est le contrôle de SCHÉMA, lui seul, qui les rejette. Il reste qu'une preuve peut
    /// pointer vers un serveur quelconque, qui apprendra l'adresse IP du modérateur
    /// qui l'ouvre. C'est une fuite, pas une prise de contrôle — et elle se fermera
    /// le jour où les litiges auront leur propre téléversement.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static bool PreuveRecevable(string? photoUrl, out string? normalisee)
    {
        normalisee = null;

        // La preuve est FACULTATIVE : la plupart des litiges n'en portent pas, et
        // refuser l'absence transformerait ce garde en régression fonctionnelle.
        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            return true;
        }

        var brut = photoUrl.Trim();

        // Borne de longueur : une URL de plus de 2 Ko n'est pas une adresse d'image,
        // c'est une charge utile. La couper ici évite de la stocker et de la relire.
        if (brut.Length > 2048)
        {
            return false;
        }

        // `UriKind.Absolute` n'écarte que les chemins relatifs — « /preuve.jpg »,
        // « preuve.jpg ». IL N'ÉCARTE PAS « javascript: » NI « data: » : ce sont des
        // URI absolues parfaitement valides, et `TryCreate` les accepte. C'est le seul
        // point de ce fichier où il serait facile de croire le travail déjà fait.
        if (!Uri.TryCreate(brut, UriKind.Absolute, out var parsee))
        {
            return false;
        }

        // VOICI LA LIGNE QUI FERME L'EXÉCUTION, et elle est seule à le faire.
        //
        // Une liste BLANCHE d'un seul schéma, et non une liste noire : refuser
        // « javascript: » puis « data: » puis « vbscript: » revient à courir derrière
        // la prochaine trouvaille. N'accepter que https ferme la question une fois.
        //
        // Https et pas « http ou https » : une preuve servie en clair peut être
        // remplacée en transit, et c'est un modérateur qui la regarde pour trancher un
        // litige d'argent.
        if (parsee.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        normalisee = brut;
        return true;
    }

    public Result StartReview()
    {
        if (Status != DisputeStatus.Open)
        {
            return Result.Failure(Error.Conflict("disputes.not_open", "Seul un litige ouvert peut passer en examen."));
        }

        Status = DisputeStatus.UnderReview;
        return Result.Success();
    }

    public Result Resolve(DisputeResolution resolution, decimal? refundAmount = null)
    {
        if (Status is DisputeStatus.Resolved)
        {
            return Result.Failure(Error.Conflict("disputes.already_resolved", "Le litige est déjà tranché."));
        }

        // Le montant n'a de sens que pour un remboursement partiel : requis et > 0
        // dans ce cas, ignoré (mis à null) pour les autres décisions.
        if (resolution == DisputeResolution.PartialRefund)
        {
            if (refundAmount is not { } amount || amount <= 0)
            {
                return Result.Failure(Error.Validation(
                    "disputes.refund_amount_required",
                    "Un remboursement partiel exige un montant strictement positif."));
            }
            RefundAmount = amount;
        }
        else
        {
            RefundAmount = null;
        }

        Status = DisputeStatus.Resolved;
        Resolution = resolution;
        ResolvedAtUtc = DateTime.UtcNow;
        Raise(new DisputeResolvedDomainEvent(Id.Value, OrderId, resolution.ToString(), RefundAmount));
        return Result.Success();
    }

    public Result Escalate()
    {
        if (Status is DisputeStatus.Resolved)
        {
            return Result.Failure(Error.Conflict("disputes.resolved", "Un litige tranché ne peut pas être escaladé."));
        }

        Status = DisputeStatus.Escalated;
        return Result.Success();
    }

    /// <summary>Rouvre un litige tranché ou escaladé (arbitrage admin) ; repasse en examen.</summary>
    public Result Reopen()
    {
        if (Status is DisputeStatus.Open or DisputeStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict("disputes.not_closed", "Le litige est déjà ouvert."));
        }

        Status = DisputeStatus.UnderReview;
        Resolution = null;
        RefundAmount = null;
        ResolvedAtUtc = null;
        return Result.Success();
    }
}
