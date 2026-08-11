using HBA.Deliveries.Domain.Deliveries;
using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Drivers;

/// <summary>
/// État du COMPTE. Décidé par l'exploitation, change rarement.
/// </summary>
public enum DriverAccountStatus
{
    /// <summary>Inscrit, pièces non encore validées.</summary>
    PendingVerification = 0,

    /// <summary>Autorisé à travailler.</summary>
    Active = 1,

    /// <summary>Suspendu temporairement.</summary>
    Suspended = 2,

    /// <summary>Bloqué définitivement.</summary>
    Blocked = 3
}

/// <summary>
/// Disponibilité OPÉRATIONNELLE. Décidée par le livreur, change plusieurs fois par jour.
/// </summary>
public enum DriverAvailability
{
    /// <summary>Hors ligne : ne reçoit aucune proposition.</summary>
    Offline = 0,

    /// <summary>En ligne et libre.</summary>
    Available = 1,

    /// <summary>En ligne mais déjà sur une course.</summary>
    Busy = 2,

    /// <summary>En ligne, en pause.</summary>
    OnBreak = 3
}

public enum VehicleType
{
    /// <summary>Deux-roues. L'écrasante majorité de la flotte à Cotonou.</summary>
    Motorcycle = 0,

    Bicycle = 1,
    Car = 2,
    Van = 3,
    OnFoot = 4,

    /// <summary>
    /// Tricycle à moteur. Ajouté après coup : il manquait, alors qu'il occupe au
    /// Bénin la place exacte entre la moto et la camionnette — jusqu'à environ
    /// 150 kg, dans des ruelles où une voiture ne passe pas. Sans lui, ces
    /// courses n'étaient tarifables ni attribuables.
    /// </summary>
    Tricycle = 5
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN LIVREUR.
///
/// DEUX DIMENSIONS QU'IL NE FAUT SURTOUT PAS CONFONDRE
///
/// Le cahier d'architecture insiste, et il a raison : le STATUT DE COMPTE et la
/// DISPONIBILITÉ sont deux choses distinctes.
///
///   • Le statut dit « cette personne a le droit de travailler ». Il est décidé
///     par l'exploitation, après vérification des pièces, et bouge rarement.
///   • La disponibilité dit « cette personne est prête, maintenant ». Elle est
///     décidée par le livreur, et bouge dix fois par jour.
///
/// Les fusionner en un seul champ conduit invariablement au même incident : un
/// livreur suspendu pour un motif grave repasse « disponible » depuis son
/// téléphone et reçoit de nouveau des courses. Ici, c'est impossible par
/// construction — <see cref="GoOnline"/> refuse si le compte n'est pas actif.
///
/// LA POSITION N'EST PAS DANS CET AGRÉGAT
///
/// Elle change toutes les quelques secondes pour chaque livreur en ligne.
/// L'écrire en base à ce rythme saturerait PostgreSQL pour une donnée dont on ne
/// garde jamais l'historique. La position courante vit dans Redis ; seule la
/// DERNIÈRE POSITION CONNUE est conservée ici, et uniquement pour survivre à un
/// redémarrage du cache.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Driver : AggregateRoot<DriverId>
{
    private Driver(DriverId id, Guid userId, string fullName, string phone, VehicleType vehicle)
        : base(id)
    {
        UserId = userId;
        FullName = fullName;
        Phone = phone;
        Vehicle = vehicle;
        AccountStatus = DriverAccountStatus.PendingVerification;
        Availability = DriverAvailability.Offline;
        RegisteredAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private Driver()
    {
        FullName = string.Empty;
        Phone = string.Empty;
    }

    /// <summary>Compte HBA correspondant. L'identité vit dans Identity, pas ici.</summary>
    public Guid UserId { get; private set; }

    public string FullName { get; private set; }

    public string Phone { get; private set; }

    public VehicleType Vehicle { get; private set; }

    public DriverAccountStatus AccountStatus { get; private set; }

    public DriverAvailability Availability { get; private set; }

    public DateTime RegisteredAtUtc { get; private set; }

    public DateTime? VerifiedAtUtc { get; private set; }

    /// <summary>Motif de la dernière suspension ou du blocage.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Dernière position connue, recopiée depuis Redis de loin en loin.</summary>
    public Coordinates? LastKnownPosition { get; private set; }

    public DateTime? LastPositionAtUtc { get; private set; }

    /// <summary>Nombre de courses menées à leur terme. Alimente le score de dispatch.</summary>
    public int CompletedDeliveries { get; private set; }

    /// <summary>
    /// Peut-il recevoir une proposition ? C'est la SEULE question que le dispatch
    /// doit poser — jamais l'un des deux champs pris isolément.
    /// </summary>
    public bool CanReceiveOffers =>
        AccountStatus is DriverAccountStatus.Active && Availability is DriverAvailability.Available;

    public static Result<Driver> Register(Guid userId, string? fullName, string? phone, VehicleType vehicle)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<Driver>(
                Error.Validation("driver.user_required", "Un compte utilisateur est requis."));
        }

        var name = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        if (name is null)
        {
            return Result.Failure<Driver>(
                Error.Validation("driver.name_required", "Le nom du livreur est requis."));
        }

        var normalizedPhone = BeninGeography.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            return Result.Failure<Driver>(
                Error.Validation("driver.phone_invalid",
                    $"Un numéro joignable est requis ({BeninGeography.DialingCode} suivi de {BeninGeography.LocalPhoneLength} chiffres)."));
        }

        return new Driver(DriverId.New(), userId, name, normalizedPhone, vehicle);
    }

    // ─── Décisions de l'exploitation ────────────────────────────────────────

    public Result Verify()
    {
        if (AccountStatus is DriverAccountStatus.Blocked)
        {
            return Result.Failure(
                Error.Conflict("driver.blocked", "Ce compte est bloqué : il ne peut pas être vérifié."));
        }

        AccountStatus = DriverAccountStatus.Active;
        VerifiedAtUtc = DateTime.UtcNow;
        StatusReason = null;
        return Result.Success();
    }

    /// <summary>
    /// Suspend le compte. Le livreur passe HORS LIGNE dans le même geste : le
    /// laisser « disponible » avec un compte suspendu créerait exactement
    /// l'incohérence que la séparation des deux dimensions cherche à éviter.
    /// </summary>
    public Result Suspend(string? reason)
    {
        if (AccountStatus is DriverAccountStatus.Blocked)
        {
            return Result.Failure(
                Error.Conflict("driver.blocked", "Ce compte est déjà bloqué."));
        }

        AccountStatus = DriverAccountStatus.Suspended;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Availability = DriverAvailability.Offline;
        return Result.Success();
    }

    public Result Block(string? reason)
    {
        AccountStatus = DriverAccountStatus.Blocked;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Availability = DriverAvailability.Offline;
        return Result.Success();
    }

    // ─── Décisions du livreur ───────────────────────────────────────────────

    public Result GoOnline()
    {
        if (AccountStatus is not DriverAccountStatus.Active)
        {
            return Result.Failure(Error.Forbidden(
                "driver.not_active",
                "Votre compte n'est pas actif : la vérification de vos pièces doit d'abord être terminée."));
        }

        Availability = DriverAvailability.Available;
        return Result.Success();
    }

    public Result GoOffline()
    {
        // Passer hors ligne pendant une course est REFUSÉ : le colis est chez le
        // livreur, et le client attend. La sortie passe par la fin de mission ou
        // par un retrait de mission décidé par l'exploitation.
        if (Availability is DriverAvailability.Busy)
        {
            return Result.Failure(Error.Conflict(
                "driver.busy",
                "Terminez votre course en cours avant de passer hors ligne."));
        }

        Availability = DriverAvailability.Offline;
        return Result.Success();
    }

    public Result TakeBreak()
    {
        if (Availability is DriverAvailability.Busy)
        {
            return Result.Failure(Error.Conflict("driver.busy", "Terminez votre course en cours avant de faire une pause."));
        }

        if (AccountStatus is not DriverAccountStatus.Active)
        {
            return Result.Failure(Error.Forbidden("driver.not_active", "Votre compte n'est pas actif."));
        }

        Availability = DriverAvailability.OnBreak;
        return Result.Success();
    }

    // ─── Cycle de mission ───────────────────────────────────────────────────

    /// <summary>
    /// Passe le livreur en mission. Public — et non <c>internal</c> — parce que
    /// c'est la couche Application qui orchestre l'acceptation : elle touche DEUX
    /// agrégats, la course et le livreur, et aucun des deux n'a le droit de piloter
    /// l'autre. La garde reste ici.
    /// </summary>
    public Result MarkBusy()
    {
        if (!CanReceiveOffers)
        {
            return Result.Failure(Error.Conflict("driver.unavailable", "Ce livreur n'est pas disponible."));
        }

        Availability = DriverAvailability.Busy;
        return Result.Success();
    }

    /// <summary>Fin de mission : le livreur redevient disponible et son compteur avance.</summary>
    public Result CompleteMission()
    {
        if (Availability is not DriverAvailability.Busy)
        {
            return Result.Failure(Error.Conflict("driver.not_on_mission", "Ce livreur n'est pas en mission."));
        }

        CompletedDeliveries++;
        Availability = DriverAvailability.Available;
        return Result.Success();
    }

    /// <summary>
    /// Recopie la position depuis le cache. Sans effet si le compte n'est pas
    /// actif : une position de livreur bloqué n'a aucun usage légitime, et la
    /// conserver serait une collecte de données sans finalité.
    /// </summary>
    public void RecordPosition(Coordinates position)
    {
        if (AccountStatus is not DriverAccountStatus.Active)
        {
            return;
        }

        LastKnownPosition = position;
        LastPositionAtUtc = DateTime.UtcNow;
    }
}
