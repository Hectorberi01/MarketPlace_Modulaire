namespace Marketplace.Modules.Offers.Domain.Offers;

/// <summary>État de condition de l'article proposé.</summary>
public enum OfferCondition
{
    New = 0,
    Used = 1,
    Refurbished = 2
}

/// <summary>Mode de fulfillment : FBS (vendeur expédie) ou FBP (plateforme expédie).</summary>
public enum FulfillmentType
{
    Fbs = 0,
    Fbp = 1
}

/// <summary>Statut d'une offre.</summary>
public enum OfferStatus
{
    Active = 0,
    Paused = 1,
    OutOfStock = 2
}
