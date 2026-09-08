namespace Marketplace.Modules.Delivery.Domain.Couriers;

/// <summary>Identité forte d'un coursier.</summary>
public readonly record struct CourierId(Guid Value)
{
    public static CourierId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Statut du compte coursier CÔTÉ MARKETPLACE — c'est-à-dire : a-t-il le droit d'être
/// payé ?
///
/// Ce n'est PAS le statut opérationnel du coursier. Sa disponibilité (en ligne / hors
/// ligne), sa position, sa zone, son taux d'acceptation appartiennent au service
/// coursier, qui les fait vivre à la seconde. Les dupliquer ici garantirait deux
/// vérités divergentes sur la même personne.
/// </summary>
public enum CourierStatus
{
    /// <summary>Enregistré, vérification en cours : aucune course ne lui est payée.</summary>
    Pending = 0,

    /// <summary>Vérifié : il peut être crédité et retirer.</summary>
    Active = 1,

    /// <summary>
    /// Suspendu. Ses courses passées restent créditées et son solde reste le sien —
    /// suspendre n'est pas confisquer. Seuls les NOUVEAUX crédits sont refusés.
    /// </summary>
    Suspended = 2
}
