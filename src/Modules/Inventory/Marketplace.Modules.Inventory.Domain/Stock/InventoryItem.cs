using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock.Events;

namespace Marketplace.Modules.Inventory.Domain.Stock;

public readonly record struct InventoryItemId(Guid Value)
{
    public static InventoryItemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Stock d'un SKU sur une localisation donnée. Disponible = OnHand − Reserved
/// (cf. dossier, InventoryItem). Agrégat racine : possède ses réservations.
/// </summary>
public sealed class InventoryItem : AggregateRoot<InventoryItemId>
{
    private readonly List<StockReservation> _reservations = new();

    private InventoryItem()
    {
    }

    private InventoryItem(InventoryItemId id, Sku sku, Guid locationId, int onHand, int reorderThreshold)
        : base(id)
    {
        Sku = sku;
        LocationId = locationId;
        OnHand = onHand;
        ReorderThreshold = reorderThreshold;

        Raise(new InventoryItemCreatedDomainEvent(id.Value, sku.Value, locationId));
    }

    public Sku Sku { get; private set; } = default!;
    public Guid LocationId { get; private set; }
    public int OnHand { get; private set; }
    public int ReorderThreshold { get; private set; }

    /// <summary>
    /// Compteur de mouvements de stock. Incrémenté par TOUTE mutation de l'agrégat.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE COLONNE EXISTE POUR RENDRE LE VERROU OPTIMISTE EFFECTIF. NE PAS LA RETIRER.
    ///
    /// `Reserve()` et `ReleaseReservation()` ne modifient AUCUNE colonne de cette table :
    /// ils ajoutent ou suppriment des lignes ENFANTS (stock_reservations). EF ne génère
    /// donc, pour ces opérations, aucun `UPDATE inventory_items` — et un jeton de
    /// concurrence n'est vérifié que dans la clause WHERE d'un UPDATE.
    ///
    /// Conséquence, sans ce compteur : le jeton `xmin` serait posé sur
    /// l'agrégat, visible dans la configuration, rassurant à la relecture — et
    /// TOTALEMENT INERTE sur le seul chemin qui compte. Deux acheteurs réservant le
    /// dernier article au même instant liraient tous deux `Available = 1`, passeraient
    /// tous deux le contrôle, insèreraient tous deux leur réservation. Survente.
    ///
    /// En incrémentant ce compteur, chaque mutation salit la ligne parente : EF émet
    /// `UPDATE inventory_items SET "StockVersion" = … WHERE "Id" = … AND xmin = …`, le
    /// second écrivain touche 0 ligne, et lève `DbUpdateConcurrencyException` → 409.
    ///
    /// La valeur elle-même n'est lue par personne. C'est le fait de l'écrire qui protège.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public int StockVersion { get; private set; }

    public IReadOnlyCollection<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>
    /// Quantité réservée — SEULES LES RÉSERVATIONS NON EXPIRÉES COMPTENT.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES LIGNES EXPIRÉES RESTENT EN BASE, ET C'EST VOLONTAIRE.
    ///
    /// Elles ne comptent plus pour la DISPONIBILITÉ — c'est tout l'objet de ce filtre —
    /// mais elles ne sont pas supprimées pour autant, parce que `ConfirmReservation` en
    /// a encore besoin : une commande payée doit décrémenter le stock physique même si
    /// son paiement a mis plus longtemps que prévu. L'expiration gouverne ce qu'on peut
    /// PROMETTRE à d'autres, pas ce qu'on DOIT à celui qui a payé.
    ///
    /// C'est la distinction qui manquait, et c'est aussi pourquoi « filtrer les
    /// expirées » ne se réduit pas à une ligne : filtrer sans cette précaution aurait
    /// libéré le stock d'une commande en cours de paiement, l'aurait laissé vendre à un
    /// autre, et aurait produit une survente au moment de la confirmation.
    ///
    /// `DateTime.UtcNow` DANS UN AGRÉGAT, comme ailleurs dans ce dépôt (User, Order,
    /// Shipment). L'alternative — passer l'heure en paramètre — obligerait chaque lecture
    /// de disponibilité, y compris les projections, à fournir une horloge ; on aurait
    /// alors une propriété correcte seulement là où quelqu'un a pensé à la passer, ce qui
    /// est exactement l'état qu'on corrige.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public int Reserved => _reservations.Where(r => r.IsLiveAt(DateTime.UtcNow)).Sum(r => r.Quantity);

    /// <summary>Quantité réellement disponible à la vente.</summary>
    public int Available => OnHand - Reserved;

    public bool IsLowStock => Available <= ReorderThreshold;

    /// <summary>
    /// Salit la ligne parente pour que le verrou optimiste (`xmin`) soit RÉELLEMENT
    /// vérifié — y compris quand la mutation ne touche que des lignes enfants.
    /// Voir le commentaire de <see cref="StockVersion"/>. Appelé par TOUTE mutation.
    /// </summary>
    private void Touch() => StockVersion++;

    public static Result<InventoryItem> Create(Sku sku, Guid locationId, int onHand, int reorderThreshold)
    {
        if (locationId == Guid.Empty)
        {
            return Error.Validation("inventory.item.location_required", "La localisation est obligatoire.");
        }

        if (onHand < 0)
        {
            return Error.Validation("inventory.item.onhand_negative", "Le stock physique ne peut pas être négatif.");
        }

        if (reorderThreshold < 0)
        {
            return Error.Validation("inventory.item.threshold_negative", "Le seuil de réapprovisionnement ne peut pas être négatif.");
        }

        return new InventoryItem(InventoryItemId.New(), sku, locationId, onHand, reorderThreshold);
    }

    /// <summary>Entrée de stock (réception fournisseur / vendeur).</summary>
    public Result Receive(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("inventory.item.quantity_invalid", "La quantité doit être positive."));
        }

        OnHand += quantity;
        Touch();
        return Result.Success();
    }

    /// <summary>Réserve du stock pour une commande, si disponible.</summary>
    public Result Reserve(Guid orderId, int quantity, DateTime expiresAtUtc)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("inventory.item.quantity_invalid", "La quantité doit être positive."));
        }

        if (Available < quantity)
        {
            return Result.Failure(Error.Conflict("inventory.item.insufficient_stock", "Stock insuffisant pour la réservation."));
        }

        _reservations.Add(new StockReservation(Guid.NewGuid(), orderId, quantity, expiresAtUtc));

        // INDISPENSABLE. L'ajout ci-dessus est une INSERTION dans une table enfant :
        // sans Touch(), EF n'émet aucun UPDATE sur inventory_items, la clause
        // `AND xmin = …` n'est jamais évaluée, et deux réservations concurrentes du
        // dernier article passent toutes les deux. C'est la survente.
        Touch();

        Raise(new StockReservedDomainEvent(Id.Value, Sku.Value, orderId, quantity));

        if (Available == 0)
        {
            Raise(new StockDepletedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return Result.Success();
    }

    /// <summary>Libère les réservations d'une commande (paiement échoué / annulation).</summary>
    public Result ReleaseReservation(Guid orderId)
    {
        _reservations.RemoveAll(r => r.OrderId == orderId);
        Touch(); // Suppression d'enfants uniquement : même piège que Reserve().
        return Result.Success();
    }

    /// <summary>
    /// Confirme la vente : décrémente le stock physique et solde les réservations.
    ///
    /// L'EXPIRATION N'EST PAS REGARDÉE ICI, DÉLIBÉRÉMENT. La commande est payée : le
    /// stock physique doit partir, que la réservation ait tenu ou non. Filtrer sur
    /// l'expiration ferait échouer la confirmation d'un paiement lent, et priverait la
    /// commande de son décrément alors même que l'argent est encaissé.
    ///
    /// LE RÉSULTAT EST DÉSORMAIS LU PAR L'APPELANT. Ce commentaire disait auparavant
    /// que `ConfirmOrderPaymentCommandHandler` ignorait le résultat, « donc l'échec
    /// serait muet ». C'était exact, et c'était une survente : la commande passait
    /// `Confirmed` sans que `OnHand` bouge. Le handler teste maintenant la valeur de
    /// retour et refuse de confirmer. Le raisonnement ci-dessus tient toujours — il
    /// ne repose simplement plus sur un défaut pour être vrai.
    /// </summary>
    public Result ConfirmReservation(Guid orderId)
    {
        var reserved = _reservations.Where(r => r.OrderId == orderId).Sum(r => r.Quantity);
        if (reserved == 0)
        {
            return Result.Failure(Error.NotFound("inventory.item.reservation_not_found", "Aucune réservation pour cette commande."));
        }

        OnHand -= reserved;
        _reservations.RemoveAll(r => r.OrderId == orderId);
        Touch();
        return Result.Success();
    }

    /// <summary>Ajuste le stock physique (inventaire, casse, retour).</summary>
    public Result AdjustOnHand(int delta)
    {
        if (OnHand + delta < Reserved)
        {
            return Result.Failure(Error.Conflict("inventory.item.adjust_below_reserved", "L'ajustement passerait le stock sous le réservé."));
        }

        OnHand += delta;
        Touch();
        return Result.Success();
    }

    public Result SetReorderThreshold(int reorderThreshold)
    {
        if (reorderThreshold < 0)
        {
            return Result.Failure(Error.Validation("inventory.item.threshold_negative", "Le seuil ne peut pas être négatif."));
        }

        ReorderThreshold = reorderThreshold;
        Touch();
        return Result.Success();
    }
}
