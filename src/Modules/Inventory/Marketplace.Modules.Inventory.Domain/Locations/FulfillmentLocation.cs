using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Inventory.Domain.Locations;

public readonly record struct FulfillmentLocationId(Guid Value)
{
    public static FulfillmentLocationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Type de lieu : adresse vendeur (FBS) ou entrepôt plateforme (FBP).</summary>
public enum FulfillmentLocationType
{
    SellerAddress = 0,
    PlatformWarehouse = 1
}

/// <summary>
/// Lieu d'où part un colis. Unifie l'adresse vendeur (FBS) et l'entrepôt
/// plateforme (FBP) (cf. dossier, FulfillmentLocation).
/// </summary>
public sealed class FulfillmentLocation : AggregateRoot<FulfillmentLocationId>
{
    private FulfillmentLocation()
    {
    }

    private FulfillmentLocation(FulfillmentLocationId id, FulfillmentLocationType type, Guid? ownerId, Address address)
        : base(id)
    {
        Type = type;
        OwnerId = ownerId;
        Address = address;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public FulfillmentLocationType Type { get; private set; }

    /// <summary>Vendeur si FBS ; null pour un entrepôt plateforme.</summary>
    public Guid? OwnerId { get; private set; }

    public Address Address { get; private set; } = default!;
    public DateTime CreatedOnUtc { get; private set; }

    public static Result<FulfillmentLocation> Create(FulfillmentLocationType type, Guid? ownerId, Address address)
    {
        if (type == FulfillmentLocationType.SellerAddress && (ownerId is null || ownerId == Guid.Empty))
        {
            return Error.Validation("inventory.location.owner_required", "Une adresse vendeur doit référencer un vendeur.");
        }

        var normalizedOwner = type == FulfillmentLocationType.PlatformWarehouse ? null : ownerId;
        return new FulfillmentLocation(FulfillmentLocationId.New(), type, normalizedOwner, address);
    }

    public Result UpdateAddress(Address address)
    {
        Address = address;
        return Result.Success();
    }
}
