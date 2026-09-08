using HbaExpress.Dispatch.Domain.Couriers;
using Marketplace.BuildingBlocks.Domain.Primitives;

namespace HbaExpress.Dispatch.Domain.Jobs;

/// <summary>Un colis attendu à un point de ramassage.</summary>
public sealed class Parcel : Entity<Guid>
{
    private Parcel()
    {
    }

    internal Parcel(Guid id, string sku, int quantity) : base(id)
    {
        Sku = sku;
        Quantity = quantity;
    }

    public string Sku { get; private set; } = default!;
    public int Quantity { get; private set; }
}

/// <summary>
/// Où le coursier récupère la part d'un vendeur.
///
/// <see cref="Landmark"/> n'est pas un champ de confort : au Bénin la rue est souvent
/// inexistante, et c'est le repère — « en face de la pharmacie » — qui rend la boutique
/// trouvable. Une adresse sans repère envoie le coursier tourner en rond.
/// </summary>
public sealed class PickupPoint : Entity<Guid>
{
    private readonly List<Parcel> _parcels = new();

    private PickupPoint()
    {
    }

    internal PickupPoint(
        Guid id, int sequence, Guid shipmentId, Guid sellerId, string sellerName, string? sellerPhone,
        string communeCode, string? quartier, string? landmark, string? line,
        GeoPoint? position, IEnumerable<Parcel> parcels)
        : base(id)
    {
        Sequence = sequence;
        ShipmentId = shipmentId;
        SellerId = sellerId;
        SellerName = sellerName;
        SellerPhone = sellerPhone;
        CommuneCode = communeCode;
        Quartier = quartier;
        Landmark = landmark;
        Line = line;
        Position = position;
        _parcels.AddRange(parcels);
    }

    /// <summary>Ordre de collecte. Optimisable plus tard ; l'ordre reçu par défaut.</summary>
    public int Sequence { get; private set; }

    public Guid ShipmentId { get; private set; }
    public Guid SellerId { get; private set; }
    public string SellerName { get; private set; } = default!;
    public string? SellerPhone { get; private set; }

    public string CommuneCode { get; private set; } = default!;
    public string? Quartier { get; private set; }
    public string? Landmark { get; private set; }
    public string? Line { get; private set; }
    public GeoPoint? Position { get; private set; }

    public bool IsPickedUp { get; private set; }
    public DateTime? PickedUpAtUtc { get; private set; }

    /// <summary>
    /// Incident constaté sur place : vendeur absent, colis manquant. Le point reste NON
    /// ramassé — on ne fait pas disparaître un problème en le notant.
    /// </summary>
    public string? Incident { get; private set; }

    public IReadOnlyList<Parcel> Parcels => _parcels.AsReadOnly();

    internal void MarkPickedUp(DateTime nowUtc)
    {
        IsPickedUp = true;
        PickedUpAtUtc = nowUtc;
        Incident = null;
    }

    internal void ReportIncident(string reason) => Incident = reason;
}

/// <summary>Où le coursier livre l'acheteur.</summary>
public sealed class Dropoff : Entity<Guid>
{
    private Dropoff()
    {
    }

    internal Dropoff(
        Guid id, Guid buyerId, string? recipient, string? phone,
        string communeCode, string? quartier, string? landmark, string? line, GeoPoint? position)
        : base(id)
    {
        BuyerId = buyerId;
        Recipient = recipient;
        Phone = phone;
        CommuneCode = communeCode;
        Quartier = quartier;
        Landmark = landmark;
        Line = line;
        Position = position;
    }

    public Guid BuyerId { get; private set; }
    public string? Recipient { get; private set; }
    public string? Phone { get; private set; }
    public string CommuneCode { get; private set; } = default!;
    public string? Quartier { get; private set; }
    public string? Landmark { get; private set; }
    public string? Line { get; private set; }
    public GeoPoint? Position { get; private set; }
}
