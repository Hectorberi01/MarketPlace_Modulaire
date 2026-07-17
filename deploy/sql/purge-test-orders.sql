-- =====================================================================
--  PURGE des commandes de TEST (repartir sur une base propre).
--  ⚠️ Supprime TOUTES les commandes et leurs lignes. À n'exécuter qu'en
--     staging/démo, pas en production avec de vraies commandes.
--
--  Note (monolithe modulaire) : les autres modules (Payments, Shipping,
--  Settlement) stockent l'orderId comme simple Guid, SANS clé étrangère
--  inter-schéma. Supprimer les commandes ne casse donc rien, mais laisse
--  des lignes « orphelines » (paiements/expéditions/écritures) inoffensives
--  côté démo. Décommente le bloc optionnel plus bas pour les purger aussi.
--
--  Exécution (Neon staging) :
--    psql "postgres://…neon…/neondb?sslmode=require" -f purge-test-orders.sql
--  ou coller dans la console SQL de Neon.
-- =====================================================================

BEGIN;

DELETE FROM ordering.order_lines;
DELETE FROM ordering.orders;

-- ---- OPTIONNEL : purge des données liées pour un état 100 % propre ----
-- (dé-commente si tu veux aussi remettre à zéro paiements / expéditions /
--  gains vendeur. Adapte les noms de schéma/tables si besoin.)
--
-- DELETE FROM payments.payments;
-- DELETE FROM shipping.shipment_items;
-- DELETE FROM shipping.shipments;
-- DELETE FROM settlement.wallet_transactions;
-- DELETE FROM settlement.withdrawals;
-- UPDATE settlement.seller_wallets   SET available_balance = 0, pending_balance = 0;
-- UPDATE settlement.platform_wallet  SET balance = 0;

COMMIT;
