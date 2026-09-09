using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.UnitTests;

/// <summary>Constructeurs de scénarios — coordonnées réelles de Cotonou.</summary>
internal static class TestJobs
{
    /// <summary>Marché Dantokpa, Cotonou.</summary>
    public static GeoPoint Dantokpa => GeoPoint.Create(6.3703, 2.4183).Value;

    /// <summary>Gbedjromede, environ 1,5 km au nord de Dantokpa.</summary>
    public static GeoPoint Gbedjromede => GeoPoint.Create(6.3835, 2.4155).Value;

    /// <summary>Godomey, environ 8 km à l'ouest — hors du rayon d'une première vague.</summary>
    public static GeoPoint Godomey => GeoPoint.Create(6.3690, 2.3350).Value;

    public static DeliveryJob NewJob(int pickupCount = 1, GeoPoint? pickupAt = null)
    {
        var pickups = Enumerable.Range(0, pickupCount).Select(i => new PickupDraft(
            ShipmentId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            SellerName: $"Boutique {i + 1}",
            SellerPhone: "+2290197123456",
            CommuneCode: "cotonou",
            Quartier: "Gbedjromede",
            Landmark: "En face de la pharmacie",
            Line: null,
            Position: pickupAt ?? Dantokpa,
            Parcels: new[] { ("SKU-1", 1) })).ToList();

        return DeliveryJob.Create(
            Guid.NewGuid(), 1_500m, "XOF", pickups,
            new DropoffDraft(
                Guid.NewGuid(), "Kossi", "+2290197000000",
                "cotonou", "Fidjrosse", "Après le carrefour", null, Gbedjromede)).Value;
    }

    public static Courier OnlineCourier(GeoPoint at, DateTime nowUtc, string commune = "cotonou")
    {
        // Deux Guid distincts, comme en vrai : identifiant de bénéficiaire et compte
        // utilisateur. Les confondre dans un test masquerait précisément le bug
        // que cette distinction corrige.
        var courier = Courier.Register(
            Guid.NewGuid(), Guid.NewGuid(), "Kossi Adjovi", VehicleType.Motorcycle).Value;
        // ANNONCÉ DANS LE PASSÉ, DÉLIBÉRÉMENT. ApplyStanding refuse une annonce qui
        // n'est pas STRICTEMENT plus récente que la dernière : dater celle-ci de
        // « maintenant » ferait échouer, au hasard de la résolution de l'horloge, tout
        // test qui suspend ensuite le coursier.
        courier.ApplyStanding(CourierStanding.Active, DateTime.UtcNow.AddHours(-1));
        courier.SetServiceArea(new[] { commune });
        courier.GoOnline();
        courier.UpdatePosition(at, nowUtc);
        return courier;
    }
}
