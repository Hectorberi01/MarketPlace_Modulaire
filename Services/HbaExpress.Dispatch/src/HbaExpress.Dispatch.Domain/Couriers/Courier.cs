using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Domain.Couriers;

/// <summary>
/// Le coursier vu par le DISPATCH : où il est, s'il travaille, ce qu'il peut porter.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'AUTRE MOITIÉ DU COURSIER.
///
/// Son identité de bénéficiaire — nom, téléphone, compte Mobile Money, droit d'être
/// payé — vit dans le module Delivery de la marketplace, avec son portefeuille. Ici
/// vit tout ce qui change à la minute : en ligne ou non, position, zone, véhicule.
///
/// LA SÉPARATION SUIT LE RYTHME DES DONNÉES, PAS LE MÉTIER. Une position se réécrit
/// toutes les quinze secondes ; un compte de versement change deux fois par an. Les
/// loger ensemble obligerait la base qui porte l'argent à encaisser le débit d'écriture
/// d'une flotte en mouvement.
///
/// <see cref="Standing"/> est RECOPIÉ depuis la marketplace par événement — le service
/// ne décide pas qui a le droit de travailler, il l'apprend. <see cref="Availability"/>,
/// au contraire, n'appartient qu'ici : c'est le coursier qui la déclare.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Courier : AggregateRoot<CourierId>
{
    private readonly List<string> _communeCodes = new();

    private Courier()
    {
    }

    private Courier(CourierId id, Guid userId, string displayName, VehicleType vehicle)
        : base(id)
    {
        UserId = userId;
        DisplayName = displayName;
        Vehicle = vehicle;
        Standing = CourierStanding.Pending;
        Availability = CourierAvailability.Offline;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Le compte avec lequel ce coursier se connecte.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX IDENTIFIANTS, ET IL FAUT LES DEUX. C'EST CONTRE-INTUITIF, DONC ÉCRIT ICI.
    ///
    /// La marketplace donne au coursier un identifiant de BÉNÉFICIAIRE (<c>CourierId</c>,
    /// la clé de cet agrégat) et il possède par ailleurs un compte UTILISATEUR
    /// (<c>UserId</c>). Ce sont deux Guid différents.
    ///
    ///  • C'est le CourierId qui fait payer : POST /api/delivery/courses/completed
    ///    l'attend, et lui seul désigne le portefeuille à créditer.
    ///  • C'est le UserId qui authentifie : le jeton JWT du coursier porte son compte
    ///    utilisateur dans la claim « sub », la marketplace n'en émettant pas d'autre.
    ///
    /// Ne garder que le premier — ce qui était le cas — donnait un service qui offre des
    /// courses à un coursier enregistré et répond 404 à ce même coursier quand il essaie
    /// de les prendre : le jeton présente un identifiant que la table ne contient pas.
    /// La panne ne se voit pas à la compilation, et pas non plus dans les tests de chaque
    /// service pris séparément — seulement à la jointure.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>Nom d'affichage, recopié de la marketplace pour l'écran de l'acheteur.</summary>
    public string DisplayName { get; private set; } = default!;

    public VehicleType Vehicle { get; private set; }
    public CourierStanding Standing { get; private set; }
    public CourierAvailability Availability { get; private set; }

    /// <summary>Dernière position connue. Null tant qu'il n'a jamais émis de point.</summary>
    public GeoPoint? LastPosition { get; private set; }

    /// <summary>
    /// Horodatage de cette position. Une position sans date serait inutilisable : à
    /// quinze minutes, un coursier à moto est à dix kilomètres de là.
    /// </summary>
    public DateTime? LastPositionAtUtc { get; private set; }

    /// <summary>Communes desservies. Vide = aucune, donc aucune offre.</summary>
    public IReadOnlyList<string> CommuneCodes => _communeCodes.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Date d'ÉMISSION de la dernière annonce de statut appliquée — pas la date à
    /// laquelle on l'a reçue.
    ///
    /// C'est la différence qui fait tout : recevoir plus tard ne veut pas dire décider
    /// plus tard. Un message peut arriver après un autre parce qu'il a été rejoué, ou
    /// simplement parce que le drain de l'outbox l'a réessayé plus longtemps.
    ///
    /// Nul tant qu'aucune annonce n'a été appliquée — un coursier créé par la toute
    /// première annonce ne l'a pas encore.
    /// </summary>
    public DateTime? StandingAnnouncedAtUtc { get; private set; }

    public static Result<Courier> Register(
        Guid courierId, Guid userId, string displayName, VehicleType vehicle)
    {
        if (courierId == Guid.Empty)
        {
            return Error.Validation("courier.id_required", "L'identifiant du coursier est obligatoire.");
        }

        if (userId == Guid.Empty)
        {
            // Sans compte utilisateur, ce coursier existerait en base et ne pourrait
            // jamais se connecter : offert des courses, incapable d'en prendre une.
            return Error.Validation(
                "courier.user_required",
                "Le compte utilisateur du coursier est obligatoire — c'est par lui qu'il s'authentifie.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation("courier.name_required", "Le nom du coursier est obligatoire.");
        }

        return new Courier(new CourierId(courierId), userId, displayName.Trim(), vehicle);
    }

    /// <summary>
    /// Recopie le droit de travailler décidé par la marketplace, SI l'annonce est plus
    /// récente que la dernière appliquée.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE VERROU MONOTONE — CE QUI FERME VRAIMENT LE REJEU DE SIGNATURE.
    ///
    /// Cette méthode recopiait le statut sans condition. C'est ce qu'on lui demande de
    /// faire — ce service ne sait pas ce qu'est un dossier vérifié — mais ça rendait
    /// l'annonce REJOUABLE : renvoyer un vieux « Active » capturé avant la suspension
    /// réactivait le coursier, et l'annulation d'une sanction ne laissait aucune trace
    /// d'intrusion, juste un statut qui remonte tout seul.
    ///
    /// L'horodatage signé (voir InboundSignature) ramène la fenêtre à cinq minutes. Ce
    /// contrôle-ci la ferme entièrement : une annonce qui n'est pas STRICTEMENT plus
    /// récente que la dernière appliquée est ignorée, quelle que soit la manière dont
    /// elle est arrivée.
    ///
    /// CE N'EST PAS QU'UNE DÉFENSE. L'outbox réessaie avec un délai croissant : un
    /// « Active » en échec pendant deux minutes peut légitimement être livré APRÈS le
    /// « Suspended » qui l'a suivi. Sans ce contrôle, un coursier suspendu redevenait
    /// actif sans que personne n'attaque quoi que ce soit.
    ///
    /// ÉGALITÉ REFUSÉE, ET C'EST VOULU. Deux annonces au même instant sont soit le même
    /// message livré deux fois — auquel cas la seconde n'apporte rien — soit deux
    /// décisions indiscernables, où appliquer l'une plutôt que l'autre serait arbitraire.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="announcedAtUtc">Instant d'ÉMISSION de l'annonce par la marketplace.</param>
    /// <returns>Vrai si l'annonce a été appliquée, faux si elle a été écartée comme périmée.</returns>
    public bool ApplyStanding(CourierStanding standing, DateTime announcedAtUtc)
    {
        if (StandingAnnouncedAtUtc is not null && announcedAtUtc <= StandingAnnouncedAtUtc)
        {
            return false;
        }

        StandingAnnouncedAtUtc = announcedAtUtc;
        Standing = standing;

        // Un coursier suspendu est sorti de la file IMMÉDIATEMENT, sans attendre qu'il
        // se déconnecte. Le laisser « en ligne » le ferait continuer à recevoir des
        // offres jusqu'à sa prochaine action.
        if (standing != CourierStanding.Active)
        {
            Availability = CourierAvailability.Offline;
        }

        return true;
    }

    /// <summary>Le coursier se déclare disponible. Refusé s'il n'a pas le droit de travailler.</summary>
    public Result GoOnline()
    {
        if (Standing != CourierStanding.Active)
        {
            return Result.Failure(Error.Validation(
                "courier.not_active", "Votre compte n'est pas encore validé ou a été suspendu."));
        }

        Availability = CourierAvailability.Online;
        return Result.Success();
    }

    public void GoOffline() => Availability = CourierAvailability.Offline;

    /// <summary>Le coursier a pris une course : il ne reçoit plus d'offres.</summary>
    public void MarkOnDelivery() => Availability = CourierAvailability.OnDelivery;

    /// <summary>Course terminée : il redevient disponible, s'il a toujours le droit.</summary>
    public void ReleaseFromDelivery()
        => Availability = Standing == CourierStanding.Active
            ? CourierAvailability.Online
            : CourierAvailability.Offline;

    public Result UpdatePosition(GeoPoint position, DateTime nowUtc)
    {
        LastPosition = position;
        LastPositionAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Déclare les communes desservies. Chaque code est validé contre les 77 communes
    /// officielles : une faute de frappe ferait disparaître le coursier du dispatch sans
    /// que rien ne le signale.
    /// </summary>
    public Result SetServiceArea(IEnumerable<string>? communeCodes)
    {
        var resolved = new List<string>();
        foreach (var raw in communeCodes ?? Enumerable.Empty<string>())
        {
            var code = BeninGeography.ResolveCommuneCode(raw);
            if (code is null)
            {
                return Result.Failure(Error.Validation("courier.area.unknown_commune", $"Commune inconnue : « {raw} »."));
            }

            if (!resolved.Contains(code))
            {
                resolved.Add(code);
            }
        }

        _communeCodes.Clear();
        _communeCodes.AddRange(resolved);
        return Result.Success();
    }

    public bool Serves(string? communeCode)
        => communeCode is not null && _communeCodes.Contains(communeCode);

    /// <summary>
    /// Ce coursier peut-il recevoir une offre EN CE MOMENT ?
    ///
    /// <paramref name="positionMaxAge"/> est ce qui distingue « en ligne » de
    /// « joignable » : une application fermée depuis dix minutes laisse un coursier
    /// marqué en ligne, avec une position périmée. Lui offrir une course, c'est perdre
    /// les vingt secondes du délai d'acceptation pour rien — et pendant ce temps
    /// l'acheteur attend.
    /// </summary>
    public bool CanReceiveOffers(DateTime nowUtc, TimeSpan positionMaxAge)
        => Standing == CourierStanding.Active
           && Availability == CourierAvailability.Online
           && HasFreshPosition(nowUtc, positionMaxAge);

    /// <summary>
    /// Le coursier peut-il AGIR sur une course — l'accepter, ramasser, partir, livrer ?
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// « LE COURSIER NE PEUT PAS FAIRE UNE COURSE DEPUIS LE WEB », TRADUIT EN RÈGLE MÉTIER.
    ///
    /// La formulation naturelle serait « refuser les navigateurs ». Elle ne tient pas :
    /// rien, dans une requête HTTP, ne prouve honnêtement de quel appareil elle vient. Un
    /// en-tête se forge, un User-Agent se change, et un jeton marqué « mobile » s'obtient
    /// en appelant la route de connexion mobile depuis un script. On aurait un contrôle
    /// qui a l'air d'un mur et n'en est pas un.
    ///
    /// La vraie contrainte n'est pas l'appareil : c'est d'ÊTRE SUR LA ROUTE. Un coursier
    /// qui accepte une course doit pouvoir aller la chercher, et un coursier qui déclare
    /// avoir livré doit s'être déplacé. Exiger une position RÉCENTE dit exactement cela,
    /// se vérifie côté serveur, et ne dépend d'aucune déclaration du client.
    ///
    /// Une console web reste parfaitement utilisable pour ce qu'elle doit faire — consulter
    /// ses gains, son historique, demander un retrait — et devient incapable de faire
    /// avancer une course, sans qu'on ait eu besoin de la reconnaître.
    ///
    /// CE N'EST PAS UN MUR NON PLUS, et il faut le dire : un client déterminé peut envoyer
    /// des coordonnées inventées. Mais il ment alors sur un fait métier vérifiable — sa
    /// trace est enregistrée, horodatée, et confrontable aux points de ramassage. C'est
    /// une fraude qui laisse une preuve, là où un en-tête forgé n'en laisse aucune.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public bool CanActOnJob(DateTime nowUtc, TimeSpan positionMaxAge)
        => Standing == CourierStanding.Active
           && HasFreshPosition(nowUtc, positionMaxAge);

    /// <summary>
    /// Position connue ET récente. Les deux comptent : à quinze minutes, un coursier à
    /// moto est à dix kilomètres de là.
    /// </summary>
    public bool HasFreshPosition(DateTime nowUtc, TimeSpan maxAge)
        => LastPosition is not null
           && LastPositionAtUtc is { } at
           && nowUtc - at <= maxAge;

    /// <summary>Âge de la dernière position, ou <c>null</c> s'il n'en a jamais émis.</summary>
    public TimeSpan? PositionAge(DateTime nowUtc)
        => LastPositionAtUtc is { } at ? nowUtc - at : null;
}
