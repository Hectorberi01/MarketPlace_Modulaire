using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Pricing.Contracts;
using CartAggregate = Marketplace.Modules.Cart.Domain.Carts.Cart;

namespace Marketplace.Modules.Cart.Application.Carts;

/// <summary>
/// Valorise un panier : pour chaque ligne, appelle Pricing pour obtenir le prix
/// effectif (avec la trace des financeurs), puis agrège les totaux. Le panier ne
/// stocke jamais ces prix — ils sont recalculés à chaque lecture.
/// </summary>
internal static class CartPricer
{
    public static async Task<CartSummary> PriceAsync(
        CartAggregate cart,
        IPricingModuleApi pricing,
        IOrderingModuleApi ordering,
        decimal platformFeeRate,
        CancellationToken cancellationToken)
    {
        var lines = new List<CartLineSummary>(cart.Items.Count);
        decimal subtotal = 0m, totalSeller = 0m, totalPlatform = 0m, grand = 0m;

        // ═════════════════════════════════════════════════════════════════════════
        // CES DEUX VALEURS ÉTAIENT CODÉES EN DUR — `Code: null` ET `IsFirstOrder: false`.
        //
        // Conséquence, et elle était totale : `Promotion.AppliesTo()` rejette toute promo
        // qui exige un code lorsque le code fourni ne correspond pas — et `null` ne
        // correspond jamais. `PromotionConditions.AreMet()` rejette toute promo
        // « première commande » lorsque `isFirstOrder` est faux — et il l'était toujours.
        //
        // Autrement dit, DEUX familles entières de promotions étaient inapplicables : le
        // back-office pouvait les créer, l'admin les activer, et aucun acheteur au monde
        // n'en aurait jamais vu la couleur. Le moteur tournait, mais à vide.
        //
        // Le coût de l'appel à Ordering est un EXISTS sur un index (BuyerId) — et il ne
        // sert qu'à répondre « cet acheteur a-t-il déjà commandé ? ». On l'évalue UNE fois
        // pour le panier, pas une fois par ligne : c'est une propriété de l'acheteur, pas
        // de l'article.
        // ═════════════════════════════════════════════════════════════════════════
        var isFirstOrder = !await ordering.HasPlacedOrderAsync(cart.BuyerId, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════════
        // LE SOUS-TOTAL DU PANIER SE CALCULE AVANT LA BOUCLE, PAS PENDANT.
        //
        // Il alimente les CONDITIONS d'éligibilité (« à partir de 50 000 d'achat ») et
        // la remise de panier. On passait auparavant le sous-total de la LIGNE, sous un
        // champ nommé `Subtotal` : une promo à seuil était refusée sur un panier de
        // 100 000 fait de dix lignes à 10 000, et accordée sur TOUTES les lignes dès
        // qu'une seule franchissait le seuil. Deux erreurs symétriques, du même oubli.
        // ═════════════════════════════════════════════════════════════════════════
        var cartSubtotal = cart.Items.Sum(i => i.UnitBaseAmount * i.Quantity);
        var totalQuantity = cart.Items.Sum(i => i.Quantity);

        foreach (var item in cart.Items)
        {
            var lineAmount = item.UnitBaseAmount * item.Quantity;

            var request = new PriceRequest(
                LineAmount: lineAmount,
                Currency: item.Currency,
                ProductId: item.ProductId,
                CategoryId: item.CategoryId,
                SellerId: item.SellerId,
                Quantity: item.Quantity,
                CartSubtotal: cartSubtotal,
                TotalQuantity: totalQuantity,
                Code: cart.PromotionCode,
                IsFirstOrder: isFirstOrder);

            var b = await pricing.CalculatePriceAsync(request, cancellationToken);

            // ─────────────────────────────────────────────────────────────────────
            // PLUS AUCUNE MULTIPLICATION PAR LA QUANTITÉ ICI.
            //
            // Pricing reçoit désormais le montant de la LIGNE et rend des montants de
            // LIGNE. Multiplier le résultat, comme on le faisait, revenait à appliquer
            // une remise à montant fixe une fois par article : 500 devenaient 1 500 sur
            // une ligne de trois.
            //
            // Effet de bord bienvenu : un pourcentage n'est plus arrondi par unité puis
            // multiplié, mais arrondi une seule fois sur la ligne.
            // ─────────────────────────────────────────────────────────────────────
            lines.Add(new CartLineSummary(
                item.OfferId, item.ProductId, item.CategoryId, item.SellerId, item.Sku,
                item.ShipFromLocationId, item.Quantity, item.UnitBaseAmount,
                b.SellerDiscount, b.PlatformDiscount,
                UnitPriceOf(b.FinalAmount, item.Quantity), b.FinalAmount, item.Currency,
                item.CommissionRate, item.ProviderFeeRate));

            subtotal += lineAmount;
            totalSeller += b.SellerDiscount;
            totalPlatform += b.PlatformDiscount;
            grand += b.FinalAmount;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LA REMISE DE PANIER : UN SEUL APPEL, PUIS UNE RÉPARTITION EN RESTE.
        //
        // Une remise à montant fixe est une remise d'ORDRE. Calculée ligne par ligne,
        // elle est retirée autant de fois qu'il y a de lignes — c'était le défaut, et
        // il se cumulait avec la multiplication par la quantité.
        //
        // La répartition proportionnelle est indispensable : le règlement vendeur lit
        // `SellerDiscount` et `PlatformDiscount` SUR CHAQUE LIGNE, et une remise qui ne
        // serait imputée nulle part ferait payer la plateforme sans qu'elle le sache.
        // La DERNIÈRE ligne reçoit le reste plutôt qu'une part arrondie : c'est ce qui
        // garantit que la somme des parts vaut exactement la remise, au franc près.
        // ═════════════════════════════════════════════════════════════════════════
        if (lines.Count > 0 && cartSubtotal > 0m)
        {
            var cartDiscount = await pricing.CalculateCartDiscountAsync(
                new CartDiscountRequest(cartSubtotal, cart.Currency, totalQuantity, cart.PromotionCode, isFirstOrder),
                cancellationToken);

            if (cartDiscount.Total > 0m)
            {
                // Plafonnée au total déjà remisé : les remises de ligne ont pu absorber
                // une partie du panier, et cumuler sans borne rendrait un total négatif.
                var seller = Math.Min(cartDiscount.SellerDiscount, grand);
                var platform = Math.Min(cartDiscount.PlatformDiscount, grand - seller);

                Distribute(lines, seller, platform, grand);

                totalSeller += seller;
                totalPlatform += platform;
                grand -= seller + platform;
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LE FRAIS DE SERVICE, ANNONCÉ AVANT LA COMMANDE.
        //
        // Même assiette et même arrondi que dans `Order.RecomputeTotals` : le total des
        // articles APRÈS remises, arrondi une fois selon la devise. Les deux calculs
        // doivent rendre le même franc, sans quoi le checkout refuserait la commande
        // sur son propre contrôle « le montant a changé depuis l'affichage ».
        //
        // C'est bien la commande qui fait foi — ce montant-ci n'est qu'un affichage.
        // Mais un affichage faux vaut ici un parcours d'achat bloqué, pas une simple
        // coquille.
        // ═════════════════════════════════════════════════════════════════════════
        var fraisPlateforme = platformFeeRate <= 0m
            ? 0m
            : Money.Round(grand * platformFeeRate, cart.Currency);

        return new CartSummary(
            cart.Id.Value, cart.BuyerId, cart.Currency, cart.Status.ToString(),
            lines, subtotal, totalSeller, totalPlatform, grand + fraisPlateforme, cart.PromotionCode,
            fraisPlateforme, platformFeeRate);
    }

    /// <summary>
    /// Prix unitaire AFFICHÉ, déduit du total de ligne.
    ///
    /// Purement indicatif : le montant qui fait foi est le total de ligne, seul à être
    /// arrondi une fois. Reconstituer l'unitaire par division peut produire un chiffre
    /// qui, remultiplié, ne redonne pas exactement la ligne — c'est normal, et c'est
    /// pourquoi aucun calcul d'argent ne doit repartir de cette valeur.
    /// </summary>
    private static decimal UnitPriceOf(decimal lineAmount, int quantity)
        => quantity <= 0 ? lineAmount : decimal.Round(lineAmount / quantity, 2);

    /// <summary>
    /// Répartit une remise de panier sur les lignes, au prorata de leur poids, la
    /// dernière ligne prenant le RESTE.
    ///
    /// Le reste, et non un arrondi de plus : trois lignes égales et une remise de 500
    /// donneraient 166,67 chacune, soit 500,01. Un centime créé de rien à chaque
    /// commande, invisible et permanent.
    /// </summary>
    private static void Distribute(
        List<CartLineSummary> lines, decimal sellerDiscount, decimal platformDiscount, decimal weightTotal)
    {
        if (weightTotal <= 0m)
        {
            return;
        }

        var sellerLeft = sellerDiscount;
        var platformLeft = platformDiscount;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var isLast = i == lines.Count - 1;

            var sellerPart = isLast
                ? sellerLeft
                : Math.Min(sellerLeft, decimal.Round(sellerDiscount * line.LineTotal / weightTotal, 2));
            var platformPart = isLast
                ? platformLeft
                : Math.Min(platformLeft, decimal.Round(platformDiscount * line.LineTotal / weightTotal, 2));

            // Une ligne ne peut pas devenir négative : ce qu'elle ne peut pas absorber
            // reste dans l'enveloppe et retombe sur les suivantes.
            var absorbable = line.LineTotal;
            if (sellerPart + platformPart > absorbable)
            {
                sellerPart = Math.Min(sellerPart, absorbable);
                platformPart = absorbable - sellerPart;
            }

            sellerLeft -= sellerPart;
            platformLeft -= platformPart;

            lines[i] = line with
            {
                SellerDiscount = line.SellerDiscount + sellerPart,
                PlatformDiscount = line.PlatformDiscount + platformPart,
                LineTotal = line.LineTotal - sellerPart - platformPart,
                FinalUnitPrice = UnitPriceOf(line.LineTotal - sellerPart - platformPart, line.Quantity),
            };
        }
    }
}
