using HbaExpress.Dispatch.Domain.Couriers;
using Marketplace.BuildingBlocks.Domain.Primitives;

namespace HbaExpress.Dispatch.Domain.Jobs;

/// <summary>
/// Une course proposée à UN coursier, avec une date limite.
///
/// L'offre n'est pas un message : c'est une TRACE. Le message part par le hub temps
/// réel et se perd si le réseau tombe ; la trace, elle, dit qui a été sollicité, quand,
/// et ce qu'il en a fait. C'est ce qui permet de calculer un taux d'acceptation, de
/// comprendre pourquoi une course n'a pas trouvé preneur, et de ne pas ré-offrir à
/// quelqu'un qui vient de refuser.
/// </summary>
public sealed class DispatchOffer : Entity<Guid>
{
    private DispatchOffer()
    {
    }

    internal DispatchOffer(Guid id, CourierId courierId, int round, DateTime sentAtUtc, DateTime expiresAtUtc)
        : base(id)
    {
        CourierId = courierId;
        Round = round;
        SentAtUtc = sentAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        Status = DispatchOfferStatus.Pending;
    }

    public CourierId CourierId { get; private set; }

    /// <summary>Vague d'offres. La ré-offre élargit le rayon et incrémente ce numéro.</summary>
    public int Round { get; private set; }

    public DateTime SentAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DispatchOfferStatus Status { get; private set; }
    public DateTime? RespondedAtUtc { get; private set; }

    /// <summary>Cette offre peut-elle encore être acceptée à cet instant ?</summary>
    public bool IsLive(DateTime nowUtc)
        => Status == DispatchOfferStatus.Pending && nowUtc < ExpiresAtUtc;

    internal void Accept(DateTime nowUtc)
    {
        Status = DispatchOfferStatus.Accepted;
        RespondedAtUtc = nowUtc;
    }

    internal void Decline(DateTime nowUtc)
    {
        Status = DispatchOfferStatus.Declined;
        RespondedAtUtc = nowUtc;
    }

    /// <summary>
    /// Un autre l'a prise, ou une nouvelle vague est partie. VOLONTAIREMENT DISTINCT
    /// d'un refus : le coursier n'a rien fait de mal, et son taux d'acceptation ne doit
    /// pas être abîmé par la lenteur du réseau ou par la vitesse d'un collègue.
    /// </summary>
    internal void Supersede(DateTime nowUtc)
    {
        if (Status != DispatchOfferStatus.Pending)
        {
            return;
        }

        Status = DispatchOfferStatus.Superseded;
        RespondedAtUtc = nowUtc;
    }

    internal void Expire(DateTime nowUtc)
    {
        if (Status != DispatchOfferStatus.Pending)
        {
            return;
        }

        Status = DispatchOfferStatus.Expired;
        RespondedAtUtc = nowUtc;
    }
}
