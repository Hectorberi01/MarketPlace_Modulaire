using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Domain.Orders.Events;

namespace Marketplace.Modules.Ordering.Domain.Orders;

/// <summary>
/// Commande multi-vendeur. Fige le prix de chaque ligne (prix de base +
/// réductions par financeur) au moment de l'achat. Son cycle de vie est piloté
/// par un Saga d'orchestration : réservation du stock, paiement, confirmation,
/// avec compensations. Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = new();

    private Order()
    {
    }

    private Order(OrderId id, Guid buyerId, Guid cartId, string currency, string? promotionCode)
        : base(id)
    {
        BuyerId = buyerId;
        CartId = cartId;
        Currency = currency;
        PromotionCode = promotionCode;
        Status = OrderStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid BuyerId { get; private set; }
    public Guid CartId { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>
    /// Code promo appliqué au panier, FIGÉ au moment de la commande. Null si aucun.
    ///
    /// La commande doit s'en souvenir : le panier est clôturé juste après le checkout,
    /// et c'est seulement à la CONFIRMATION (paiement encaissé) que la promotion est
    /// réellement décomptée. Sans ce snapshot, le module Pricing recevrait un événement
    /// « commande confirmée » sans savoir quel coupon consommer — et le coupon resterait
    /// éternellement disponible. C'est très exactement le bug qu'on corrige.
    /// </summary>
    public string? PromotionCode { get; private set; }

    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Date de livraison. NULLE tant que la commande n'est pas livrée — et sur toutes
    /// celles livrées avant l'existence de ce champ.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CE N'EST PAS UN CONFORT D'AFFICHAGE : C'EST LE POINT DE DÉPART DU DÉLAI DE
    /// RETOUR.
    ///
    /// Le délai devait courir depuis quelque part, et `CreatedAtUtc` aurait été le choix
    /// gratuit — aucune colonne à ajouter. Mais il aurait fait porter à l'acheteur le
    /// temps d'une livraison qu'il ne contrôle pas : une expédition lente lui aurait
    /// mangé sa fenêtre, et à la limite l'aurait fait expirer AVANT qu'il ait reçu son
    /// colis. Le délai part donc de la réception.
    ///
    /// NULLE SUR LES COMMANDES ANCIENNES, et c'est le seul choix honnête : on ne sait pas
    /// quand elles ont été livrées. `RequestReturnCommandHandler` traite ce cas en
    /// ACCEPTANT le retour — voir le raisonnement là-bas.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public DateTime? DeliveredAtUtc { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal TotalSellerDiscount { get; private set; }
    public decimal TotalPlatformDiscount { get; private set; }

    /// <summary>Frais de livraison encaissés par la plateforme (forfait choisi au checkout).</summary>
    public decimal ShippingFee { get; private set; }

    /// <summary>
    /// VALEUR DE LA COMMANDE. Ce n'est PAS le montant à encaisser — voir
    /// <see cref="AmountDue"/>.
    /// </summary>
    public decimal GrandTotal { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// TAXE CONTENUE DANS <see cref="GrandTotal"/>, ET NON AJOUTÉE À LUI.
    ///
    /// La place applique le régime « TVA comprise » : le prix saisi par le vendeur est
    /// le prix payé par l'acheteur. Cette colonne ne modifie donc AUCUN montant du
    /// parcours — ni le total affiché, ni le montant encaissé, ni la base de
    /// commission, ni le plafond de remboursement. Elle DÉCRIT une part de
    /// <see cref="GrandTotal"/>, pour la comptabilité et la facture.
    ///
    /// C'est l'invariant à ne jamais casser : le jour où quelqu'un ajoutera
    /// <c>TaxTotal</c> à <c>GrandTotal</c> « pour que le total soit juste », toutes les
    /// commandes existantes deviendront fausses et l'acheteur paiera deux fois la TVA.
    /// D'où <see cref="NetTotal"/> exposé en propriété calculée plutôt qu'en colonne :
    /// il n'y a qu'une seule vérité stockée, et c'est le total payé.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal TaxTotal { get; private set; }

    /// <summary>
    /// Taux retenu au moment de la commande (0.18 = 18 %). FIGÉ, pour la même raison
    /// que <see cref="OrderLine.CommissionRate"/> : un changement de barème fiscal ne
    /// doit pas réécrire rétroactivement la ventilation des commandes passées, sinon
    /// une facture réimprimée ne correspond plus à celle qui a été remise au client.
    /// Zéro quand aucune règle ne s'applique.
    /// </summary>
    public decimal TaxRate { get; private set; }

    /// <summary>
    /// Règle fiscale appliquée, ou <c>null</c> si aucune. Conservée pour qu'un contrôle
    /// puisse remonter du montant à la règle qui l'a produit.
    /// </summary>
    public Guid? TaxRuleId { get; private set; }

    /// <summary>Part hors taxe du total. Un RESTE, jamais un calcul indépendant.</summary>
    public decimal NetTotal => GrandTotal - TaxTotal;

    /// <summary>
    /// Part de la commande réglée par la CAGNOTTE de l'acheteur, figée au checkout.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// POURQUOI CE MONTANT VIT SUR LA COMMANDE, ET PAS SUR LE PAIEMENT.
    ///
    /// La cagnotte réduit ce qu'on encaisse, pas ce que la commande vaut : le vendeur
    /// est payé son prix, la commission est calculée sur le prix, et le client a bien
    /// acheté pour GrandTotal. Un seul chiffre ne peut donc pas dire les deux à la fois.
    ///
    /// Le ranger sur le PAIEMENT aurait paru plus simple — c'est lui qui encaisse — mais
    /// une commande peut n'avoir aucun paiement (cagnotte couvrant tout), et un retour
    /// survient des semaines plus tard, quand il faut savoir quelle part rendre en
    /// espèces et quelle part en cagnotte. Cette information appartient à la commande.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal WalletApplied { get; private set; }

    /// <summary>
    /// MONTANT À ENCAISSER auprès du prestataire de paiement. Zéro quand la cagnotte
    /// couvre tout — auquel cas aucun paiement n'est créé.
    /// </summary>
    public decimal AmountDue => GrandTotal - WalletApplied;

    /// <summary>La cagnotte couvre-t-elle l'intégralité de la commande ?</summary>
    public bool IsFullyCoveredByWallet => WalletApplied > 0m && AmountDue <= 0m;

    public string? CancellationReason { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────────
    // Adresse de livraison FIGÉE au moment de la commande. Copie, pas de FK vers
    // Identity : si l'acheteur modifie ou supprime son adresse ensuite, la commande
    // doit continuer de dire où le colis a été envoyé.
    //
    // On fige le CODE de commune, pas son libellé. Les codes ne changent jamais (c'est
    // le contrat de BeninGeography), alors qu'un libellé peut être corrigé — et une
    // correction d'orthographe ne doit pas réécrire l'historique.
    // ─────────────────────────────────────────────────────────────────────────────
    public string? ShipToLabel { get; private set; }
    public string? ShipToRecipient { get; private set; }
    public string? ShipToPhone { get; private set; }
    public string? ShipToCommuneCode { get; private set; }
    public string? ShipToQuartier { get; private set; }
    public string? ShipToLandmark { get; private set; }
    public string? ShipToLine1 { get; private set; }
    public string? ShipToCountryCode { get; private set; }

    /// <summary>
    /// Position figée, quand l'acheteur en avait une. FACULTATIVE : elle complète le
    /// repère, elle ne le remplace pas — et une commande sans position se livre
    /// exactement comme avant.
    ///
    /// Figée elle aussi : si l'acheteur déplace ensuite le point de son adresse, la
    /// commande doit continuer de dire où le colis a été envoyé.
    /// </summary>
    public double? ShipToLatitude { get; private set; }

    public double? ShipToLongitude { get; private set; }

    /// <summary>La commande porte-t-elle un point ouvrable dans une carto ?</summary>
    public bool HasShipToCoordinates => ShipToLatitude is not null && ShipToLongitude is not null;

    /// <summary>Libellé de la commune de livraison, résolu à l'affichage.</summary>
    public string ShipToCommuneName => BeninGeography.CommuneName(ShipToCommuneCode);

    /// <summary>
    /// La commande porte-t-elle une adresse exploitable ?
    ///
    /// Elle s'appuie sur la COMMUNE et le REPÈRE — pas sur la rue, qui est facultative
    /// au Bénin. Une commande dont on ne connaît que « rue X » n'est pas livrable.
    /// </summary>
    public bool HasShippingAddress =>
        !string.IsNullOrWhiteSpace(ShipToCommuneCode) && !string.IsNullOrWhiteSpace(ShipToLandmark);

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public static Result<Order> Create(
        Guid buyerId,
        Guid cartId,
        string currency,
        IEnumerable<OrderLineDraft> drafts,
        string? promotionCode = null)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("ordering.buyer_required", "L'acheteur est obligatoire.");
        }

        var draftList = drafts?.ToList() ?? new List<OrderLineDraft>();
        if (draftList.Count == 0)
        {
            return Error.Validation("ordering.no_lines", "Une commande doit comporter au moins une ligne.");
        }

        if (draftList.Any(d => d.Quantity <= 0))
        {
            return Error.Validation("ordering.line_quantity_invalid", "Chaque ligne doit avoir une quantité positive.");
        }

        var order = new Order(
            OrderId.New(),
            buyerId,
            cartId,
            currency.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(promotionCode) ? null : promotionCode.Trim().ToUpperInvariant());

        foreach (var draft in draftList)
        {
            order._lines.Add(new OrderLine(Guid.NewGuid(), draft));
        }

        order.RecomputeTotals();
        return order;
    }

    /// <summary>Fige l'adresse de livraison choisie (copie intégrale dans la commande).</summary>
    public void SetShippingAddress(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1, string? countryCode,
        double? latitude, double? longitude)
    {
        ShipToLabel = Trim(label, 60);
        ShipToRecipient = Trim(recipient, 120);
        ShipToPhone = Trim(phone, 20);
        ShipToCommuneCode = Trim(communeCode, 40);
        ShipToQuartier = Trim(quartier, 120);
        ShipToLandmark = Trim(landmark, 200);
        ShipToLine1 = Trim(line1, 200);
        ShipToCountryCode = Trim(countryCode, 2) ?? BeninGeography.CountryCode;

        // Les deux ou aucune : une latitude seule placerait le point dans le golfe
        // de Guinée, à 400 km de Cotonou.
        ShipToLatitude = latitude is not null && longitude is not null ? latitude : null;
        ShipToLongitude = ShipToLatitude is null ? null : longitude;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>
    /// Fige la ventilation hors taxe / taxe du total, SANS y toucher.
    ///
    /// Appelée APRÈS <see cref="SetShippingFee"/> : la taxe porte sur le total
    /// effectivement payé, frais de livraison compris — c'est un seul montant TTC aux
    /// yeux de l'acheteur, et le découper reviendrait à inventer une exonération que
    /// personne n'a décidée.
    ///
    /// Le montant est PLAFONNÉ au total plutôt que refusé : une taxe supérieure au prix
    /// payé n'a aucun sens, et échouer ici ferait perdre une commande valide pour une
    /// règle fiscale mal saisie en administration. Le plafond rend l'anomalie visible
    /// (<see cref="NetTotal"/> à zéro) sans casser le parcours d'achat.
    /// </summary>
    public void ApplyIncludedTax(decimal taxTotal, decimal rate, Guid? ruleId)
    {
        TaxTotal = taxTotal <= 0m ? 0m : Math.Min(taxTotal, GrandTotal);
        TaxRate = rate < 0m ? 0m : rate;
        TaxRuleId = ruleId == Guid.Empty ? null : ruleId;
    }

    /// <summary>Fixe les frais de livraison (forfait choisi) et recalcule le total.</summary>
    public void SetShippingFee(decimal fee)
    {
        ShippingFee = fee < 0m ? 0m : fee;
        RecomputeTotals();
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.UnitBasePrice * l.Quantity);
        TotalSellerDiscount = _lines.Sum(l => l.SellerDiscount * l.Quantity);
        TotalPlatformDiscount = _lines.Sum(l => l.PlatformDiscount * l.Quantity);
        GrandTotal = _lines.Sum(l => l.LineTotal) + ShippingFee;

        // La taxe DÉCRIT une part du total : elle ne peut pas lui être supérieure. Si le
        // total est recalculé à la baisse après la ventilation, on re-plafonne plutôt
        // que de laisser un NetTotal négatif traverser la comptabilité en silence.
        if (TaxTotal > GrandTotal)
        {
            TaxTotal = GrandTotal;
        }
    }

    /// <summary>
    /// Impute à la commande la part réglée par la cagnotte.
    ///
    /// Appelée APRÈS <see cref="SetShippingFee"/> : le plafond est le total livraison
    /// comprise, et l'inverser laisserait passer une cagnotte supérieure au montant dû.
    /// Le montant est plafonné plutôt que refusé — l'appelant a déjà réservé les fonds
    /// côté portefeuille, et échouer ici obligerait à défaire une écriture pour un
    /// arrondi.
    ///
    /// N'est possible qu'AVANT la mise en attente de paiement : une fois le montant
    /// annoncé au prestataire, il ne bouge plus.
    /// </summary>
    public Result ApplyWalletCredit(decimal amount)
    {
        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "ordering.wallet_credit_too_late",
                "La cagnotte ne peut plus être appliquée une fois la commande engagée au paiement."));
        }

        if (amount <= 0m)
        {
            return Result.Success();
        }

        WalletApplied = amount > GrandTotal ? GrandTotal : amount;
        return Result.Success();
    }

    /// <summary>Stock réservé : la commande attend le paiement. Émet OrderPlaced.</summary>
    public Result MarkAwaitingPayment()
    {
        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Transition invalide vers « en attente de paiement »."));
        }

        Status = OrderStatus.AwaitingPayment;
        Raise(new OrderPlacedDomainEvent(Id.Value, BuyerId, CartId, GrandTotal, Currency));
        return Result.Success();
    }

    /// <summary>Paiement encaissé.</summary>
    public Result MarkPaid()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Aucun paiement attendu dans cet état."));
        }

        Status = OrderStatus.Paid;
        return Result.Success();
    }

    /// <summary>Réservations soldées : la commande est confirmée.</summary>
    public Result Confirm()
    {
        if (Status != OrderStatus.Paid)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "La commande doit être payée avant confirmation."));
        }

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedDomainEvent(Id.Value, BuyerId, Currency, PromotionCode, BuildSellerShares()));
        return Result.Success();
    }

    /// <summary>
    /// Répartit la commande entre ses vendeurs : combien d'articles, pour quel montant.
    ///
    /// C'est la commande — et elle seule — qui détient cette vérité : elle connaît ses
    /// lignes, donc leurs vendeurs et leurs prix. La reconstituer ailleurs (dans le
    /// module Notifications, par exemple) obligerait à relire les lignes depuis un
    /// autre schéma, ce que l'architecture modulaire interdit précisément.
    ///
    /// Le montant est la somme des <c>LineTotal</c> du vendeur : le prix FINAL payé,
    /// remises comprises. Ce n'est pas encore ce qu'il touchera — la commission de la
    /// plateforme se retire plus loin, dans Settlement. La notification annonce donc
    /// « voici ce qui a été acheté chez vous », et non « voici ce que vous toucherez ».
    /// Confondre les deux ferait attendre au vendeur un virement plus élevé que celui
    /// qui arrivera.
    /// </summary>
    private List<OrderSellerShare> BuildSellerShares()
        => _lines
            .GroupBy(line => line.SellerId)
            .Select(g => new OrderSellerShare(
                g.Key,
                g.Sum(line => line.Quantity),
                g.Sum(line => line.LineTotal)))
            .ToList();

    /// <summary>
    /// Livraison confirmée (toutes les expéditions reçues). Déclenche, en aval,
    /// la libération de l'escrow et le payout vendeur (Saga, étape finale).
    /// </summary>
    public Result MarkDelivered()
    {
        if (Status != OrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Seule une commande confirmée peut être marquée livrée."));
        }

        Status = OrderStatus.Delivered;
        DeliveredAtUtc = DateTime.UtcNow;
        Raise(new OrderDeliveredDomainEvent(Id.Value, BuyerId));
        return Result.Success();
    }

    /// <summary>
    /// Annulation (réservations à libérer en compensation).
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// `Delivered` MANQUAIT DANS CETTE GARDE, ET LA COMPENSATION SE REJOUAIT À VIDE.
    ///
    /// La liste bloquait `Confirmed`, `Cancelled` et `Failed`. Elle avait été écrite
    /// avant que `Delivered` n'existe, et personne ne l'a rouverte en l'ajoutant à
    /// l'énumération. Un acheteur pouvait donc annuler sa commande LIVRÉE — et
    /// `CancelOrderCommandHandler` déroulait alors toute la compensation sur des
    /// engagements déjà consommés : `ReleaseReservationAsync` sur des réservations
    /// déjà soldées, ce qui gonfle le stock, et `ReleaseCreditAsync` sur une cagnotte
    /// déjà dépensée, ce qui rend l'argent une seconde fois.
    ///
    /// L'incohérence sautait aux yeux une fois posée à plat : une commande CONFIRMÉE
    /// n'était pas annulable, mais une commande LIVRÉE l'était. Personne n'aurait écrit
    /// cette règle volontairement.
    ///
    /// LA VOIE DE SORTIE APRÈS LIVRAISON EST LE RETOUR, pas l'annulation — et c'est
    /// exactement ce que `RequestReturnCommand` accepte désormais.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Cancel(string reason)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict("ordering.not_cancellable", "La commande n'est plus annulable."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new OrderCancelledDomainEvent(Id.Value, BuyerId, reason));
        return Result.Success();
    }

    /// <summary>Échec du Saga (ex. stock indisponible) avant paiement.</summary>
    public void Fail(string reason)
    {
        Status = OrderStatus.Failed;
        CancellationReason = reason;
    }
}
