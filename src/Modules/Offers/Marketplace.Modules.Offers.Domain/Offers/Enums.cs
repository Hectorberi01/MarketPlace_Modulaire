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

/// <summary>Type de remise appliquée à une mise en vente (financée par le vendeur).</summary>
public enum DiscountType
{
    /// <summary>Pourcentage du prix vendeur (ex. 15 %).</summary>
    Percentage = 0,

    /// <summary>Montant fixe déduit du prix vendeur.</summary>
    Amount = 1
}
