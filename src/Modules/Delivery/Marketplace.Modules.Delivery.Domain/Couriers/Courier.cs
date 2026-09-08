using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Delivery.Domain.Couriers.Events;

namespace Marketplace.Modules.Delivery.Domain.Couriers;

/// <summary>
/// Le coursier, vu par la MARKETPLACE — c'est-à-dire réduit à ce qu'il faut pour le
/// payer et pour répondre de lui.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CET AGRÉGAT NE CONTIENT PAS, ET POURQUOI C'EST LE POINT PRINCIPAL.
///
/// Pas de véhicule, pas de position, pas de zone, pas de disponibilité, pas de note,
/// pas de taux d'acceptation. Tout cela appartient au SERVICE COURSIER, déployé à part
/// (voir docs/CDC_Service_Livraison_Coursier.md) : ce sont des données qui changent à la
/// seconde, que le dispatch temps réel fait vivre, et dont la marketplace n'a aucun
/// usage.
///
/// Les recopier ici créerait deux vérités sur la même personne, et la question « laquelle
/// fait foi ? » n'aurait pas de bonne réponse. On ne garde donc que ce dont le monolithe
/// a réellement besoin : de quoi virer de l'argent à quelqu'un, et de quoi refuser de le
/// faire.
///
/// Trois champs, trois usages, aucun décoratif :
///  • le nom          → le bénéficiaire affiché par le PSP sur le virement ;
///  • le téléphone    → joindre le coursier quand un versement échoue ;
///  • le versement    → où l'argent part.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Courier : AggregateRoot<CourierId>
{
    private Courier()
    {
    }

    private Courier(
        CourierId id, Guid userId, string displayName, string phone,
        CourierPayoutAccount payout, string? externalRef)
        : base(id)
    {
        UserId = userId;
        DisplayName = displayName;
        Phone = phone;
        Payout = payout;
        ExternalRef = externalRef;
        Status = CourierStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    /// <summary>
    /// Compte utilisateur de la plateforme. Un coursier se connecte comme tout le monde ;
    /// il n'a pas d'identité parallèle.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>Nom affiché au PSP comme bénéficiaire du virement.</summary>
    public string DisplayName { get; private set; } = default!;

    /// <summary>Numéro de contact, normalisé au format béninois.</summary>
    public string Phone { get; private set; } = default!;

    /// <summary>Où l'argent part.</summary>
    public CourierPayoutAccount Payout { get; private set; } = default!;

    /// <summary>
    /// Identifiant du même coursier DANS LE SERVICE COURSIER, s'il en a un.
    ///
    /// C'est le seul lien entre les deux mondes, et il est facultatif : un coursier peut
    /// être enregistré côté marketplace avant d'exister côté dispatch. Le service s'en
    /// sert pour désigner qui payer sans que la marketplace ait à connaître son modèle.
    /// </summary>
    public string? ExternalRef { get; private set; }

    public CourierStatus Status { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Ce coursier peut-il recevoir de l'argent aujourd'hui ?</summary>
    public bool CanBePaid => Status == CourierStatus.Active;

    public static Result<Courier> Register(
        Guid userId, string displayName, string phone,
        string payoutProvider, string payoutAccountNumber, string? payoutAccountName = null,
        string? externalRef = null)
    {
        if (userId == Guid.Empty)
        {
            return Error.Validation("delivery.courier.user_required", "Un compte utilisateur est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation("delivery.courier.name_required", "Le nom du coursier est obligatoire.");
        }

        // Le téléphone passe par la normalisation béninoise partagée : un numéro saisi
        // « 97 12 34 56 » et le même saisi « +229 97123456 » doivent désigner la même
        // personne, sans quoi le doublon se découvre le jour du premier versement raté.
        var normalizedPhone = BeninGeography.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            return Error.Validation("delivery.courier.phone_invalid", "Numéro de téléphone invalide.");
        }

        var payout = CourierPayoutAccount.Create(payoutProvider, payoutAccountNumber, payoutAccountName ?? displayName);
        if (payout.IsFailure)
        {
            return Result.Failure<Courier>(payout.Error);
        }

        var courier = new Courier(
            CourierId.New(), userId, displayName.Trim(), normalizedPhone, payout.Value,
            string.IsNullOrWhiteSpace(externalRef) ? null : externalRef.Trim());

        courier.Raise(new CourierRegisteredDomainEvent(courier.Id.Value, courier.DisplayName));
        return courier;
    }

    /// <summary>Met à jour le compte de versement (changement d'opérateur, de numéro).</summary>
    public Result ChangePayoutAccount(string provider, string accountNumber, string? accountName)
    {
        var payout = CourierPayoutAccount.Create(provider, accountNumber, accountName ?? DisplayName);
        if (payout.IsFailure)
        {
            return payout;
        }

        Payout = payout.Value;
        Touch();
        return Result.Success();
    }

    /// <summary>Vérification terminée : le coursier peut être payé.</summary>
    public Result Activate()
    {
        if (Status == CourierStatus.Active)
        {
            return Result.Success();
        }

        Status = CourierStatus.Active;
        SuspensionReason = null;
        Touch();
        Raise(new CourierActivatedDomainEvent(Id.Value));
        return Result.Success();
    }

    /// <summary>
    /// Suspend le coursier. SON SOLDE RESTE LE SIEN : ce qu'il a livré, il l'a gagné.
    /// Seuls les nouveaux crédits sont refusés — et son retrait reste possible, parce
    /// que retenir l'argent de quelqu'un qu'on ne veut plus faire travailler n'est pas
    /// une sanction, c'est une dette qu'on refuse d'honorer.
    /// </summary>
    public Result Suspend(string reason)
    {
        Status = CourierStatus.Suspended;
        SuspensionReason = string.IsNullOrWhiteSpace(reason) ? "Suspendu par l'administrateur." : reason.Trim();
        Touch();
        Raise(new CourierSuspendedDomainEvent(Id.Value, SuspensionReason));
        return Result.Success();
    }

    /// <summary>Rattache ce coursier à son identifiant dans le service de dispatch.</summary>
    public void LinkExternalRef(string externalRef)
    {
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            return;
        }

        ExternalRef = externalRef.Trim();
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
