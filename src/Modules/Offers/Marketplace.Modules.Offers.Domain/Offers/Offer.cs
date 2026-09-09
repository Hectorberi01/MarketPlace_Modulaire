using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Domain.Offers.Events;

namespace Marketplace.Modules.Offers.Domain.Offers;

/// <summary>
/// Le pont produit × vendeur : un vendeur propose une variante à un prix de base,
/// dans un état de condition et un mode de fulfillment (cf. dossier, module Offers).
/// Le prix de base est immuable côté promo (Pricing empile dessus) — ici on peut
/// le changer explicitement.
/// </summary>
public sealed class Offer : AggregateRoot<OfferId>
{
    private Offer()
    {
    }

    private Offer(
        OfferId id,
        Guid productId,
        Guid sellerId,
        Sku variantSku,
        decimal sellerPrice,
        decimal commissionAmount,
        decimal providerFeeAmount,
        decimal commissionRate,
        decimal providerFeeRate,
        Money basePrice,
        OfferCondition condition,
        FulfillmentType fulfillmentType,
        Guid shipFromLocationId,
        int handlingTime)
        : base(id)
    {
        ProductId = productId;
        SellerId = sellerId;
        VariantSku = variantSku;
        SellerPrice = sellerPrice;
        CommissionAmount = commissionAmount;
        ProviderFeeAmount = providerFeeAmount;
        CommissionRate = commissionRate;
        ProviderFeeRate = providerFeeRate;
        BasePrice = basePrice;
        Condition = condition;
        FulfillmentType = fulfillmentType;
        ShipFromLocationId = shipFromLocationId;
        HandlingTime = handlingTime;
        Status = OfferStatus.Active;
        CreatedOnUtc = DateTime.UtcNow;

        // L'événement porte le PRIX PRODUIT (payé par l'acheteur), pas le prix vendeur.
        Raise(new OfferCreatedDomainEvent(id.Value, productId, sellerId, variantSku.Value, basePrice.Amount, basePrice.Currency));
    }

    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public Sku VariantSku { get; private set; } = default!;

    /// <summary>Prix NET saisi par le vendeur (hors majoration plateforme).</summary>
    public decimal SellerPrice { get; private set; }

    /// <summary>Commission plateforme empilée sur le prix vendeur.</summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>Frais provider empilés sur le prix vendeur.</summary>
    public decimal ProviderFeeAmount { get; private set; }

    // ---- Barème appliqué ---------------------------------------------------------
    //
    // LES DEUX TAUX CI-DESSOUS SONT MÉMORISÉS, PAS SEULEMENT LES MONTANTS QU'ILS ONT
    // PRODUITS. Ils voyagent ensuite jusqu'à la ligne de commande (Cart puis Ordering),
    // où Settlement les relit pour répartir le brut.
    //
    // Sans eux, l'accrual relisait les taux depuis la CONFIGURATION, au moment de la
    // confirmation. Passer la commission de 10 % à 12 % changeait donc rétroactivement
    // la répartition de toutes les offres déjà tarifées à 10 % : le prix acheteur avait
    // été construit avec l'ancien taux, le partage se faisait avec le nouveau, et le
    // vendeur touchait moins que le prix net qu'il avait fixé. En silence, et sur tout
    // son catalogue.

    /// <summary>Taux de commission plateforme appliqué au dernier calcul de prix (0.10 = 10 %).</summary>
    public decimal CommissionRate { get; private set; }

    /// <summary>Taux de frais provider appliqué au dernier calcul de prix (0.05 = 5 %).</summary>
    public decimal ProviderFeeRate { get; private set; }

    /// <summary>
    /// PRIX PRODUIT payé par l'acheteur = prix vendeur + commission + frais provider.
    /// Inchangé pour le reste du système (cart, pricing, order l'utilisent).
    /// </summary>
    public Money BasePrice { get; private set; } = default!;
    public OfferCondition Condition { get; private set; }
    public FulfillmentType FulfillmentType { get; private set; }
    public Guid ShipFromLocationId { get; private set; }
    public int HandlingTime { get; private set; }
    public OfferStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    // ---- Remise (promo financée par le vendeur) ----------------------------------
    //
    // Modèle « changement de prix + prix de comparaison » : quand une remise est
    // active, SellerPrice/CommissionAmount/BasePrice portent DÉJÀ le prix réduit, et
    // ces trois champs mémorisent l'origine (pour restaurer et afficher le prix barré).
    // Aucun calcul dynamique à la lecture : le client lit BasePrice (réduit) + CompareAtPrice.

    /// <summary>Prix vendeur d'origine, mémorisé tant qu'une remise est active (null sinon).</summary>
    public decimal? OriginalSellerPrice { get; private set; }

    /// <summary>Prix acheteur d'origine (barré) tant qu'une remise est active (null sinon).</summary>
    public Money? CompareAtPrice { get; private set; }

    /// <summary>Fin de la promo, à titre d'affichage. Null = sans échéance.</summary>
    public DateTime? DiscountEndsOnUtc { get; private set; }

    /// <summary>Vrai si une remise est active.</summary>
    public bool HasDiscount => CompareAtPrice is not null;

    public static Result<Offer> Create(
        Guid productId,
        Guid sellerId,
        Sku variantSku,
        decimal sellerPrice,
        decimal commissionRate,
        decimal providerFeeRate,
        string currency,
        OfferCondition condition,
        FulfillmentType fulfillmentType,
        Guid shipFromLocationId,
        int handlingTime)
    {
        if (productId == Guid.Empty)
        {
            return Error.Validation("offers.offer.product_required", "Le produit est obligatoire.");
        }

        if (sellerId == Guid.Empty)
        {
            return Error.Validation("offers.offer.seller_required", "Le vendeur est obligatoire.");
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LA DEVISE DOIT ÊTRE UNE DEVISE QUE LA PLACE SAIT TARIFER.
        //
        // `CreateOfferCommandValidator` ne portait AUCUNE règle sur la devise : le
        // client la choisissait librement, et elle pilotait ensuite tout le calcul
        // monétaire de l'offre. « ZZZ » était accepté ; `Money.DecimalsFor` lui
        // prêtait deux décimales par défaut, la commission et les frais étaient
        // arrondis sur cette hypothèse, l'offre était publiée — et l'erreur ne
        // survenait qu'à l'encaissement, chez une passerelle qui ne connaît pas ce
        // code. Entre les deux, un catalogue affichant des prix dans une devise qui
        // n'existe pas.
        //
        // POURQUOI ICI ET PAS SEULEMENT DANS LE VALIDATOR.
        //
        // Le validator ne couvre qu'un chemin d'entrée. Le domaine les couvre tous :
        // BFF vendeur, API d'administration, futur import en masse. Une offre ne
        // peut naître que dans une devise dont l'arrondi est CONNU — c'est cette
        // connaissance, et elle seule, que le contrôle de précision juste en dessous
        // suppose acquise.
        // ═════════════════════════════════════════════════════════════════════════
        if (!Money.IsSupported(currency))
        {
            return Error.Validation(
                "offers.offer.currency_unsupported",
                $"La devise « {currency} » n'est pas prise en charge. "
                + $"Devises acceptées : {string.Join(", ", Money.SupportedCurrencies)}.");
        }

        // PRIX STRICTEMENT POSITIF — invariant du domaine, pas simple validation d'entrée.
        //
        // `Money.Create` ne rejette que le NÉGATIF : un prix de 0 passait, en création comme
        // en modification (ChangeOfferPrice n'avait d'ailleurs aucun validator). Un vendeur
        // pouvait donc lister un article à 0 XOF — gratuit, tout en engageant la logistique,
        // la commission et les points de fidélité. On barre ici, dans le domaine, parce que
        // c'est le seul endroit qu'aucun appelant (BFF, API, futur import en masse) ne peut
        // contourner.
        if (sellerPrice <= 0m)
        {
            return Error.Validation("offers.offer.price_invalid", "Le prix vendeur doit être strictement positif.");
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LE PRIX VENDEUR DOIT TENIR DANS SA DEVISE.
        //
        // Un prix de 1000,50 XOF passait — le franc CFA n'a pourtant aucune
        // subdivision. L'offre publiait alors, dans une seule et même réponse :
        // prix vendeur 1000,50, commission 100, frais 50, et prix acheteur 1150
        // (arrondi par `Money.Create`). Une addition fausse, visible à l'écran, et
        // un écart jusqu'à 0,50 par unité sur le net reversé.
        //
        // On refuse plutôt que d'arrondir en silence : le vendeur a saisi un
        // montant, il doit voir lequel sera retenu.
        // ═════════════════════════════════════════════════════════════════════════
        if (sellerPrice != Money.Round(sellerPrice, currency))
        {
            return Error.Validation(
                "offers.offer.price_precision",
                Money.IsZeroDecimal(currency)
                    ? $"Le {currency} ne se divise pas : saisissez un montant entier."
                    : $"Le prix comporte trop de décimales pour le {currency}.");
        }

        if (shipFromLocationId == Guid.Empty)
        {
            return Error.Validation("offers.offer.location_required", "La localisation d'expédition est obligatoire.");
        }

        if (handlingTime < 0)
        {
            return Error.Validation("offers.offer.handling_invalid", "Le délai de préparation ne peut pas être négatif.");
        }

        var (commissionAmount, providerFeeAmount, productPrice) =
            ComputeBreakdown(sellerPrice, commissionRate, providerFeeRate, currency);

        var priceResult = Money.Create(productPrice, currency);
        if (priceResult.IsFailure)
        {
            return Result.Failure<Offer>(priceResult.Error);
        }

        return new Offer(
            OfferId.New(),
            productId,
            sellerId,
            variantSku,
            sellerPrice,
            commissionAmount,
            providerFeeAmount,
            commissionRate,
            providerFeeRate,
            priceResult.Value,
            condition,
            fulfillmentType,
            shipFromLocationId,
            handlingTime);
    }

    public Result ChangePrice(decimal sellerPrice, decimal commissionRate, decimal providerFeeRate)
    {
        // Même invariant qu'à la création : pas de prix nul ou négatif. Cette méthode
        // n'avait AUCUNE validation de prix — c'était le trou par lequel une offre passait
        // à 0 après coup.
        if (sellerPrice <= 0m)
        {
            return Result.Failure(Error.Validation(
                "offers.offer.price_invalid", "Le prix vendeur doit être strictement positif."));
        }

        // MÊME CONTRÔLE DE PRÉCISION QU'À LA CRÉATION.
        //
        // Le refuser à la création et l'admettre à la modification ne ferme rien : il
        // suffisait de créer l'offre à 1000 XOF puis de la repasser à 1000,50 pour
        // retrouver exactement l'addition fausse. La devise, elle, n'a pas à être
        // revérifiée : elle vient de `BasePrice`, donc d'une création déjà validée, et
        // aucune méthode ne permet d'en changer.
        if (sellerPrice != Money.Round(sellerPrice, BasePrice.Currency))
        {
            return Result.Failure(Error.Validation(
                "offers.offer.price_precision",
                Money.IsZeroDecimal(BasePrice.Currency)
                    ? $"Le {BasePrice.Currency} ne se divise pas : saisissez un montant entier."
                    : $"Le prix comporte trop de décimales pour le {BasePrice.Currency}."));
        }

        var (commissionAmount, providerFeeAmount, productPrice) =
            ComputeBreakdown(sellerPrice, commissionRate, providerFeeRate, BasePrice.Currency);

        var priceResult = Money.Create(productPrice, BasePrice.Currency);
        if (priceResult.IsFailure)
        {
            return Result.Failure(priceResult.Error);
        }

        SellerPrice = sellerPrice;
        CommissionAmount = commissionAmount;
        ProviderFeeAmount = providerFeeAmount;
        CommissionRate = commissionRate;
        ProviderFeeRate = providerFeeRate;
        BasePrice = priceResult.Value;

        // Un changement de prix manuel ANNULE toute remise en cours (le nouveau prix
        // devient la référence).
        OriginalSellerPrice = null;
        CompareAtPrice = null;
        DiscountEndsOnUtc = null;

        // L'événement porte le PRIX PRODUIT (payé par l'acheteur), pas le prix vendeur.
        Raise(new OfferPriceChangedDomainEvent(Id.Value, ProductId, BasePrice.Amount, BasePrice.Currency));
        return Result.Success();
    }

    /// <summary>
    /// Applique une remise (financée par le vendeur) : le prix vendeur baisse, la
    /// commission est recalculée sur le prix réduit, et le prix acheteur d'origine est
    /// mémorisé comme prix barré. Ré-appliquer repart TOUJOURS du prix d'origine (pas
    /// d'empilement de remises).
    /// </summary>
    public Result ApplyDiscount(
        DiscountType type, decimal value, DateTime? endsOnUtc, decimal commissionRate, decimal providerFeeRate)
    {
        if (value <= 0m)
        {
            return Result.Failure(Error.Validation("offers.discount.value_invalid", "La remise doit être strictement positive."));
        }

        if (type == DiscountType.Percentage && value >= 100m)
        {
            return Result.Failure(Error.Validation("offers.discount.percentage_invalid", "Le pourcentage doit être inférieur à 100."));
        }

        if (endsOnUtc is { } end && end <= DateTime.UtcNow)
        {
            return Result.Failure(Error.Validation("offers.discount.end_past", "La date de fin doit être dans le futur."));
        }

        // On repart toujours du prix d'origine, jamais du prix déjà réduit.
        var reference = OriginalSellerPrice ?? SellerPrice;

        // Même correction que dans `ComputeBreakdown` : l'arrondi suit la devise.
        var discounted = type == DiscountType.Percentage
            ? Money.Round(reference * (1m - value / 100m), BasePrice.Currency)
            : reference - Money.Round(value, BasePrice.Currency);

        if (discounted <= 0m || discounted >= reference)
        {
            return Result.Failure(Error.Validation(
                "offers.discount.result_invalid",
                "La remise doit produire un prix réduit strictement positif et inférieur au prix d'origine."));
        }

        var (_, _, referenceBuyerPrice) = ComputeBreakdown(reference, commissionRate, providerFeeRate, BasePrice.Currency);
        var compareAt = Money.Create(referenceBuyerPrice, BasePrice.Currency);
        if (compareAt.IsFailure)
        {
            return Result.Failure(compareAt.Error);
        }

        var (commissionAmount, providerFeeAmount, productPrice) = ComputeBreakdown(discounted, commissionRate, providerFeeRate, BasePrice.Currency);
        var newPrice = Money.Create(productPrice, BasePrice.Currency);
        if (newPrice.IsFailure)
        {
            return Result.Failure(newPrice.Error);
        }

        OriginalSellerPrice = reference;
        CompareAtPrice = compareAt.Value;
        DiscountEndsOnUtc = endsOnUtc;

        SellerPrice = discounted;
        CommissionAmount = commissionAmount;
        ProviderFeeAmount = providerFeeAmount;
        CommissionRate = commissionRate;
        ProviderFeeRate = providerFeeRate;
        BasePrice = newPrice.Value;

        Raise(new OfferPriceChangedDomainEvent(Id.Value, ProductId, BasePrice.Amount, BasePrice.Currency));
        return Result.Success();
    }

    /// <summary>Retire la remise : restaure le prix vendeur d'origine (et le prix acheteur).</summary>
    public Result RemoveDiscount(decimal commissionRate, decimal providerFeeRate)
    {
        if (OriginalSellerPrice is not { } original)
        {
            return Result.Success(); // pas de remise : rien à faire
        }

        var (commissionAmount, providerFeeAmount, productPrice) = ComputeBreakdown(original, commissionRate, providerFeeRate, BasePrice.Currency);
        var priceResult = Money.Create(productPrice, BasePrice.Currency);
        if (priceResult.IsFailure)
        {
            return Result.Failure(priceResult.Error);
        }

        SellerPrice = original;
        CommissionAmount = commissionAmount;
        ProviderFeeAmount = providerFeeAmount;
        CommissionRate = commissionRate;
        ProviderFeeRate = providerFeeRate;
        BasePrice = priceResult.Value;

        OriginalSellerPrice = null;
        CompareAtPrice = null;
        DiscountEndsOnUtc = null;

        Raise(new OfferPriceChangedDomainEvent(Id.Value, ProductId, BasePrice.Amount, BasePrice.Currency));
        return Result.Success();
    }

    /// <summary>
    /// Empile la majoration plateforme sur le prix vendeur net :
    ///   commission = Round(sellerPrice * commRate), frais = Round(sellerPrice * provRate),
    ///   prix produit = sellerPrice + commission + frais — chaque arrondi étant fait
    ///   à la précision de LA DEVISE.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// L'ARRONDI EST UNE PROPRIÉTÉ DE LA DEVISE, PAS UNE CONSTANTE.
    ///
    /// Ces deux lignes faisaient `Math.Round(x)` — zéro décimale, QUELLE QUE SOIT LA
    /// DEVISE. Juste en franc CFA, faux partout ailleurs. Et la devise arrivait du
    /// client sans le moindre contrôle.
    ///
    /// Le chiffrage, sur une offre en euro à 10,40 € net, barème 10 % / 5 % :
    ///
    ///   ici        : commission Round(1,04) = 1,00 ; frais Round(0,52) = 1,00
    ///                → prix acheteur 12,40 €
    ///   Settlement : commission 1,08 ; frais 0,54 → net vendeur 10,78 €
    ///
    /// Le vendeur a fixé 10,40 € et touche 10,78 €. La plateforme a facturé 2,00 € de
    /// majoration et n'en garde que 1,62 € : 0,38 € perdus par unité, soit 19 % de sa
    /// propre marge, dans le sens qui la coûte.
    ///
    /// `Money.Round` connaît le nombre de décimales de chaque devise. C'est la
    /// primitive que doivent appeler tous les calculs monétaires du dépôt, et le
    /// contournement se voyait d'autant mieux qu'elle existait déjà.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static (decimal Commission, decimal ProviderFee, decimal ProductPrice) ComputeBreakdown(
        decimal sellerPrice, decimal commissionRate, decimal providerFeeRate, string currency)
    {
        var commissionAmount = Money.Round(sellerPrice * commissionRate, currency);
        var providerFeeAmount = Money.Round(sellerPrice * providerFeeRate, currency);
        var productPrice = sellerPrice + commissionAmount + providerFeeAmount;
        return (commissionAmount, providerFeeAmount, productPrice);
    }

    public Result SetHandlingTime(int handlingTime)
    {
        if (handlingTime < 0)
        {
            return Result.Failure(Error.Validation("offers.offer.handling_invalid", "Le délai de préparation ne peut pas être négatif."));
        }

        HandlingTime = handlingTime;
        return Result.Success();
    }

    public Result ChangeShipFromLocation(Guid shipFromLocationId)
    {
        if (shipFromLocationId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("offers.offer.location_required", "La localisation d'expédition est obligatoire."));
        }

        ShipFromLocationId = shipFromLocationId;
        return Result.Success();
    }

    public Result Activate()
    {
        Status = OfferStatus.Active;
        return Result.Success();
    }

    public Result Pause()
    {
        Status = OfferStatus.Paused;
        return Result.Success();
    }

    public Result MarkOutOfStock()
    {
        Status = OfferStatus.OutOfStock;
        return Result.Success();
    }
}
