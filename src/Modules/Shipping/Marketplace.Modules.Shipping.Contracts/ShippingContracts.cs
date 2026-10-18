namespace Marketplace.Modules.Shipping.Contracts;

/// <summary>Vue publique d'un transporteur du catalogue.</summary>
public sealed record CarrierSummary(
    Guid Id,
    string Name,
    string Code,
    string? TrackingUrlTemplate,
    string? LogoUrl,
    bool IsActive,
    DateTime CreatedAtUtc);

/// <summary>Ligne d'une expédition.</summary>
public sealed record ShipmentItemSummary(string Sku, int Quantity);

/// <summary>Vue publique d'une expédition.</summary>
/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE CODE DE REMISE D'UNE EXPÉDITION — UN TYPE À PART, ET C'EST LA PROTECTION.
///
/// Il aurait été plus court d'ajouter un champ `DeliveryCode` à `ShipmentSummary`.
/// C'était aussi le moyen le plus sûr de tout casser : ce résumé est servi AU VENDEUR
/// par `SellerFulfillmentEndpoints`, et un vendeur qui lit le code avant la remise n'a
/// plus rien à prouver. Le mécanisme entier ne coûterait plus que sa complexité.
///
/// Le code voyage donc dans son propre type, rendu par sa propre méthode, appelée
/// depuis une seule route — celle de l'acheteur. Pour qu'il fuite, il faudrait
/// désormais l'écrire explicitement quelque part : aucun ajout de champ, aucune
/// sérialisation élargie ne peut le faire par inadvertance.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ShipmentDeliveryCode(Guid ShipmentId, Guid SellerId, string Code);

public sealed record ShipmentSummary(
    Guid Id,
    Guid OrderId,
    Guid SellerId,
    Guid BuyerId,
    Guid ShipFromLocationId,
    string Status,
    string? Carrier,
    string? TrackingNumber,
    string? TrackingUrl,
    DateTime CreatedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? DeliveredAtUtc,
    /// <summary>
    /// « Courier », « Carrier », ou nul tant que personne n'a pris le colis.
    ///
    /// SANS CE CHAMP, L'ÉCRAN DE SUIVI NE PEUT RIEN DIRE. Une livraison par coursier de
    /// la plateforme n'a ni transporteur ni numéro de suivi — c'est normal, une moto n'en
    /// a pas — et l'application ne pouvait donc pas distinguer « porté par notre
    /// coursier » de « pas encore expédié ». Les deux se lisaient comme un écran vide.
    /// </summary>
    string? DeliveryMode,
    /// <summary>
    /// Pourquoi le dernier coursier a renoncé — nul si aucune course n'a échoué.
    ///
    /// SANS CE CHAMP, LA NOTIFICATION ENVOYÉE AU VENDEUR N'A NULLE PART OÙ ATTERRIR.
    /// On lui écrit que son colis n'est pas parti et qu'il doit l'expédier par un
    /// transporteur ; s'il ouvre son tableau de bord, il retrouve une expédition « en
    /// préparation » exactement identique à celle d'avant l'incident. Un message d'alerte
    /// qui ne se vérifie nulle part finit par être pris pour une erreur du système.
    /// </summary>
    string? CourierFailureReason,
    IReadOnlyList<ShipmentItemSummary> Items);

/// <summary>
/// Une option de livraison proposée à l'acheteur pour une destination donnée.
///
/// <paramref name="ZoneCode"/> est nul quand l'option vient du FORFAIT DE REPLI codé
/// en dur — c'est-à-dire quand aucune grille n'est encore enregistrée. Le distinguer
/// permet à l'administration de voir d'un coup d'œil qu'elle regarde le tarif de
/// secours et non une zone qu'elle croirait avoir configurée.
/// </summary>
public sealed record ShippingRateOption(
    string ServiceCode,
    string Label,
    string Eta,
    decimal Amount,
    string Currency,
    string? ZoneCode,
    string? ZoneName);

/// <summary>Vue publique d'une zone de livraison et de sa grille.</summary>
public sealed record ShippingZoneSummary(
    Guid Id,
    string Code,
    string Name,
    bool IsFallback,
    bool IsActive,
    IReadOnlyList<string> CommuneCodes,
    IReadOnlyList<ShippingRateOption> Rates,
    DateTime CreatedAtUtc);

/// <summary>
/// Les communes qu'aucune zone explicite ne couvre. Elles fonctionnent — le repli les
/// prend — mais au tarif le plus large. L'écran d'administration les affiche pour
/// qu'une commune ne reste pas au tarif national pendant des mois faute d'y avoir pensé.
/// </summary>
public sealed record ShippingCoverageGapView(
    IReadOnlyList<string> UncoveredCommuneCodes,
    bool HasActiveFallback);
