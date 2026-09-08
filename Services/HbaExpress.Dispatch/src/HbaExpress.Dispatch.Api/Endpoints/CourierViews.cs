using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Api.Endpoints;

/// <summary>
/// Ce que l'application coursier affiche. Des types de LECTURE, distincts des agrégats.
///
/// Exposer DeliveryJob directement sérialiserait ses offres — donc les identifiants des
/// AUTRES coursiers sollicités. Un livreur n'a pas à savoir à qui d'autre la course a été
/// proposée : c'est une information sur la flotte, pas sur sa course.
/// </summary>
public sealed record JobView(
    Guid JobId,
    Guid OrderId,
    decimal ShippingFee,
    string Currency,
    string Status,
    DateTime? OfferExpiresAtUtc,
    IReadOnlyList<PickupView> Pickups,
    DropoffView Dropoff);

public sealed record PickupView(
    Guid PickupPointId,
    int Sequence,
    string SellerName,
    string? SellerPhone,
    string CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude,
    bool IsPickedUp,
    string? Incident,
    IReadOnlyList<ParcelView> Parcels);

public sealed record ParcelView(string Sku, int Quantity);

public sealed record DropoffView(
    string? Recipient,
    string? Phone,
    string CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude);

public static class JobViewMapper
{
    /// <summary>
    /// <paramref name="courierId"/> sert UNIQUEMENT à retrouver l'échéance de SON offre.
    /// Aucune autre offre n'est exposée.
    /// </summary>
    public static JobView ToView(this DeliveryJob job, CourierId courierId)
        => new(
            job.Id.Value,
            job.OrderId,
            job.ShippingFee,
            job.Currency,
            job.Status.ToString(),
            job.Offers
                .Where(o => o.CourierId == courierId && o.Status == DispatchOfferStatus.Pending)
                .Select(o => (DateTime?)o.ExpiresAtUtc)
                .FirstOrDefault(),
            job.PickupsInSequence.Select(ToPickupView).ToList(),
            ToDropoffView(job.Dropoff));

    private static PickupView ToPickupView(PickupPoint pickup)
        => new(
            pickup.Id, pickup.Sequence, pickup.SellerName, pickup.SellerPhone,
            pickup.CommuneCode, pickup.Quartier, pickup.Landmark, pickup.Line,
            pickup.Position?.Latitude, pickup.Position?.Longitude,
            pickup.IsPickedUp, pickup.Incident,
            pickup.Parcels.Select(p => new ParcelView(p.Sku, p.Quantity)).ToList());

    private static DropoffView ToDropoffView(Dropoff dropoff)
        => new(
            dropoff.Recipient, dropoff.Phone, dropoff.CommuneCode,
            dropoff.Quartier, dropoff.Landmark, dropoff.Line,
            dropoff.Position?.Latitude, dropoff.Position?.Longitude);
}
