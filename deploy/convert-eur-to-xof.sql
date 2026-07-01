-- =====================================================================
--  Conversion des données existantes EUR -> XOF
--  Marketplace (monolithe modulaire, un schéma Postgres par module)
--
--  Taux fixe BCEAO : 1 EUR = 655,957 XOF (parité fixe euro / franc CFA).
--  Les montants sont en numeric(18,2) ; on arrondit au franc entier
--  (ROUND(..., 0)) car le XOF n'a pas de sous-unité.
--
--  SÛR À REJOUER : chaque UPDATE ne cible que les lignes encore en 'EUR'
--  (ou rattachées à un parent encore en 'EUR'). Une 2ᵉ exécution ne
--  retouche donc rien.
--
--  ORDRE IMPORTANT : les tables enfants SANS colonne devise
--  (order_lines, invoice_lines) sont converties AVANT leur parent,
--  via jointure sur la devise du parent (encore 'EUR' à ce moment).
--
--  Avant de lancer : FAIRE UNE SAUVEGARDE.
--    pg_dump -Fc marketplace > marketplace_avant_xof.dump
--  Puis :  psql -d marketplace -f convert-eur-to-xof.sql
-- =====================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ------- Catalogue / Offres (offers.offers) -------
UPDATE offers.offers
SET base_price_amount   = ROUND(base_price_amount * 655.957, 0),
    base_price_currency = 'XOF'
WHERE base_price_currency = 'EUR';

-- ------- Panier (cart.carts / cart.cart_items) -------
UPDATE cart.cart_items
SET "UnitBaseAmount" = ROUND("UnitBaseAmount" * 655.957, 0),
    "Currency"       = 'XOF'
WHERE "Currency" = 'EUR';

UPDATE cart.carts
SET "Currency" = 'XOF'
WHERE "Currency" = 'EUR';

-- ------- Commandes (ordering.orders / ordering.order_lines) -------
-- Lignes d'abord (pas de devise propre : on se base sur la commande parente).
UPDATE ordering.order_lines AS l
SET "UnitBasePrice"   = ROUND(l."UnitBasePrice"   * 655.957, 0),
    "SellerDiscount"  = ROUND(l."SellerDiscount"  * 655.957, 0),
    "PlatformDiscount"= ROUND(l."PlatformDiscount"* 655.957, 0),
    "FinalUnitPrice"  = ROUND(l."FinalUnitPrice"  * 655.957, 0)
FROM ordering.orders AS o
WHERE l."OrderId" = o."Id"
  AND o."Currency" = 'EUR';

UPDATE ordering.orders
SET "Subtotal"             = ROUND("Subtotal"             * 655.957, 0),
    "TotalSellerDiscount"  = ROUND("TotalSellerDiscount"  * 655.957, 0),
    "TotalPlatformDiscount"= ROUND("TotalPlatformDiscount"* 655.957, 0),
    "GrandTotal"           = ROUND("GrandTotal"           * 655.957, 0),
    "Currency"             = 'XOF'
WHERE "Currency" = 'EUR';

-- ------- Paiements (payments.payments) -------
UPDATE payments.payments
SET amount   = ROUND(amount * 655.957, 0),
    currency = 'XOF'
WHERE currency = 'EUR';

-- ------- Facturation (billing.*) -------
-- Lignes de facture d'abord (devise héritée de la facture).
UPDATE billing.invoice_lines AS l
SET "Amount" = ROUND(l."Amount" * 655.957, 0)
FROM billing.invoices AS i
WHERE l."InvoiceId" = i."Id"
  AND i."Currency" = 'EUR';

UPDATE billing.invoices
SET "TotalAmount" = ROUND("TotalAmount" * 655.957, 0),
    "Currency"    = 'XOF'
WHERE "Currency" = 'EUR';

-- Règles de commission (FixedFee/MinFee/MaxFee ; MinFee/MaxFee nullables).
UPDATE billing.commission_rules
SET "FixedFee" = ROUND("FixedFee" * 655.957, 0),
    "MinFee"   = ROUND("MinFee"   * 655.957, 0),
    "MaxFee"   = ROUND("MaxFee"   * 655.957, 0),
    "Currency" = 'XOF'
WHERE "Currency" = 'EUR';

-- ------- Règlements / Reversements (settlement.*) -------
UPDATE settlement.seller_earnings
SET "GrossAmount"      = ROUND("GrossAmount"      * 655.957, 0),
    "CommissionAmount" = ROUND("CommissionAmount" * 655.957, 0),
    "NetAmount"        = ROUND("NetAmount"        * 655.957, 0),
    "Currency"         = 'XOF'
WHERE "Currency" = 'EUR';

UPDATE settlement.settlement_batches
SET "TotalNet"  = ROUND("TotalNet" * 655.957, 0),
    "Currency"  = 'XOF'
WHERE "Currency" = 'EUR';

UPDATE settlement.payouts
SET "GrossAmount"      = ROUND("GrossAmount"      * 655.957, 0),
    "CommissionAmount" = ROUND("CommissionAmount" * 655.957, 0),
    "NetAmount"        = ROUND("NetAmount"        * 655.957, 0),
    "Currency"         = 'XOF'
WHERE "Currency" = 'EUR';

-- ------- Retours / RMA (returns.return_requests ; RefundAmount nullable) -------
UPDATE returns.return_requests
SET "RefundAmount" = ROUND("RefundAmount" * 655.957, 0),
    "Currency"     = 'XOF'
WHERE "Currency" = 'EUR';

-- ------- Analytics (analytics.metric_snapshots) -------
UPDATE analytics.metric_snapshots
SET "GmvAmount" = ROUND("GmvAmount" * 655.957, 0),
    "Currency"  = 'XOF'
WHERE "Currency" = 'EUR';

-- ------- Read model recherche (search.documents ; MinPrice/Currency nullables) -------
UPDATE search.documents
SET "MinPrice" = ROUND("MinPrice" * 655.957, 0),
    "Currency" = 'XOF'
WHERE "Currency" = 'EUR';

COMMIT;

-- =====================================================================
--  Contrôle : doit renvoyer 0 ligne partout (plus aucune donnée en EUR).
-- =====================================================================
SELECT 'offers.offers'              AS source, COUNT(*) AS eur_restants FROM offers.offers              WHERE base_price_currency = 'EUR'
UNION ALL SELECT 'cart.carts',            COUNT(*) FROM cart.carts            WHERE "Currency" = 'EUR'
UNION ALL SELECT 'cart.cart_items',       COUNT(*) FROM cart.cart_items       WHERE "Currency" = 'EUR'
UNION ALL SELECT 'ordering.orders',       COUNT(*) FROM ordering.orders       WHERE "Currency" = 'EUR'
UNION ALL SELECT 'payments.payments',     COUNT(*) FROM payments.payments     WHERE currency = 'EUR'
UNION ALL SELECT 'billing.invoices',      COUNT(*) FROM billing.invoices      WHERE "Currency" = 'EUR'
UNION ALL SELECT 'billing.commission_rules', COUNT(*) FROM billing.commission_rules WHERE "Currency" = 'EUR'
UNION ALL SELECT 'settlement.seller_earnings', COUNT(*) FROM settlement.seller_earnings WHERE "Currency" = 'EUR'
UNION ALL SELECT 'settlement.settlement_batches', COUNT(*) FROM settlement.settlement_batches WHERE "Currency" = 'EUR'
UNION ALL SELECT 'settlement.payouts',    COUNT(*) FROM settlement.payouts    WHERE "Currency" = 'EUR'
UNION ALL SELECT 'returns.return_requests', COUNT(*) FROM returns.return_requests WHERE "Currency" = 'EUR'
UNION ALL SELECT 'analytics.metric_snapshots', COUNT(*) FROM analytics.metric_snapshots WHERE "Currency" = 'EUR'
UNION ALL SELECT 'search.documents',      COUNT(*) FROM search.documents      WHERE "Currency" = 'EUR';

-- =====================================================================
--  NOTES
--  • Le panier est aussi mis en cache dans Redis (cache-aside). Après la
--    conversion, videz la clé de cache panier (ou FLUSHDB sur la base de
--    dev) pour éviter de servir un total EUR périmé.
--  • pricing.promotions possède une colonne « Value » SANS devise
--    (montant ou pourcentage selon le type) : volontairement NON convertie
--    ici pour ne pas corrompre les promotions en pourcentage. Convertissez
--    à la main les promotions de type montant fixe si vous en avez.
--  • Ce script requalifie les données EXISTANTES. Les nouveaux
--    enregistrements sont déjà en XOF (défauts applicatifs corrigés).
-- =====================================================================
