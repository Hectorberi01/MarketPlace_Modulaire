namespace HbaExpress.Dispatch.Domain.Jobs;

public readonly record struct DeliveryJobId(Guid Value)
{
    public static DeliveryJobId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Cycle de vie d'une course (voir le CDC §4.2).</summary>
public enum DeliveryJobStatus
{
    /// <summary>Construite depuis la commande prête, pas encore diffusée.</summary>
    Created = 0,

    /// <summary>Offerte à des coursiers éligibles ; en attente d'acceptation.</summary>
    Offered = 1,

    /// <summary>UN coursier l'a prise. Assignation irréversible sans annulation explicite.</summary>
    Accepted = 2,

    /// <summary>Ramassage en cours chez les vendeurs.</summary>
    PickingUp = 3,

    /// <summary>Tous les ramassages faits, en route vers l'acheteur.</summary>
    InTransit = 4,

    Delivered = 5,

    /// <summary>Personne n'a accepté dans le délai global → repli en cascade.</summary>
    Expired = 6,

    Cancelled = 7,

    /// <summary>Échec en cours de route (acheteur absent, colis refusé).</summary>
    Failed = 8
}

/// <summary>Sort d'une offre envoyée à un coursier.</summary>
public enum DispatchOfferStatus
{
    /// <summary>Envoyée, ni acceptée ni expirée : la seule qui puisse encore gagner.</summary>
    Pending = 0,

    Accepted = 1,

    /// <summary>Refusée explicitement par le coursier.</summary>
    Declined = 2,

    /// <summary>Délai dépassé sans réponse.</summary>
    Expired = 3,

    /// <summary>
    /// Un autre coursier a gagné, ou une nouvelle vague d'offres a été lancée.
    /// Distinct de « refusée » : le coursier n'a rien fait de mal, et son taux
    /// d'acceptation ne doit pas en souffrir.
    /// </summary>
    Superseded = 4
}
