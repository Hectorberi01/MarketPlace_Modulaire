-- ═══════════════════════════════════════════════════════════════════════════════════
-- PHASE 0 — ÉTAT DES LIEUX AVANT LE RETRAIT DE LA LIVRAISON
--
-- Retrait décidé le 08/10/2026 : le module Delivery, le service Dispatch et
-- l'application livreur disparaissent. CE FICHIER NE MODIFIE RIEN. Il lit, il compte,
-- il exporte. Les décisions qu'il éclaire — payer, solder, clore — sont des décisions
-- humaines, et aucune d'elles n'est automatisable ici.
--
-- POURQUOI CE FICHIER EXISTE
-- `WithdrawalPayoutResolver` est le SEUL endroit qui sait où verser l'argent d'un
-- coursier, et il l'apprend de `delivery.couriers`. Le provider, le numéro et le nom du
-- compte Mobile Money ne vivent nulle part ailleurs. Supprimer le schéma `delivery`
-- avant d'avoir exporté ces colonnes rend les soldes de `settlement` IMPOSSIBLES À
-- SOLDER : l'argent reste dû, et plus personne ne sait à quel numéro l'envoyer.
--
-- Aucune clé étrangère ne relie `settlement` à `delivery`. Rien ne s'opposera donc à la
-- suppression — et c'est précisément le danger.
--
-- CONVENTION DE NOMMAGE, à ne pas perdre de vue en relisant ces requêtes :
-- les TABLES sont en minuscules (`wallets`), les COLONNES en PascalCase (`"OwnerType"`).
-- Les guillemets sur les colonnes sont OBLIGATOIRES ; sans eux PostgreSQL replie le nom
-- en minuscules et ne le trouve pas.
--
-- ORDRE D'EXÉCUTION : §1 (constat) → §2 (export) → §3 (contrôles) → décision humaine.
-- Les deux schémas vivent dans la MÊME base (monolithe modulaire) — à confirmer sur
-- votre instance ; le dispatch, lui, a sa propre base.
-- ═══════════════════════════════════════════════════════════════════════════════════


-- ───────────────────────────────────────────────────────────────────────────────────
-- §1. LE CONSTAT — combien, et combien ça fait
-- ───────────────────────────────────────────────────────────────────────────────────

-- 1.1 Combien de coursiers, et dans quel état
SELECT "Status", count(*) AS coursiers
FROM delivery.couriers
GROUP BY "Status"
ORDER BY "Status";

-- 1.2 LES SOLDES DUS — la question qui commande tout le reste
--
-- `Available` est l'argent acquis et retirable. `Pending` existe pour les coursiers
-- mais n'est jamais crédité (le versement passe directement en disponible).
-- Un solde NÉGATIF est possible : la politique l'autorise sur Courier/Available.
SELECT
    a."Account",
    count(*)              AS portefeuilles,
    sum(a."Balance")      AS total,
    min(a."Balance")      AS plus_bas,
    max(a."Balance")      AS plus_haut,
    w."Currency"
FROM settlement.wallets w
JOIN settlement.wallet_accounts a ON a."WalletId" = w."Id"
WHERE w."OwnerType" = 'Courier'
GROUP BY a."Account", w."Currency"
ORDER BY a."Account";

-- 1.3 Le détail, coursier par coursier, AVEC de quoi le payer
--
-- C'est la liste de travail du règlement. Elle joint les deux schémas tant qu'ils
-- existent encore tous les deux — après la suppression de `delivery`, elle n'est plus
-- reconstituable.
SELECT
    c."DisplayName",
    c."Phone",
    c."Status"                AS statut_coursier,
    a."Balance"               AS solde_disponible,
    w."Currency",
    c."PayoutProvider",
    c."PayoutAccountNumber",
    c."PayoutAccountName",
    w."OwnerId"               AS courier_id
FROM settlement.wallets w
JOIN settlement.wallet_accounts a ON a."WalletId" = w."Id" AND a."Account" = 'Available'
LEFT JOIN delivery.couriers c ON c."Id" = w."OwnerId"
WHERE w."OwnerType" = 'Courier'
  AND a."Balance" <> 0
ORDER BY a."Balance" DESC;

-- 1.4 LES RETRAITS NON SOLDÉS — les deux statuts n'appellent PAS le même geste
--
--   Requested  : demandé, rien n'est parti. Peut être approuvé ou annulé.
--   Processing : le versement a DÉJÀ été demandé au prestataire et n'est pas confirmé.
--                Ne jamais rembourser sans réconciliation : on rendrait deux fois.
SELECT
    w."Status",
    count(*)          AS nombre,
    sum(w."Amount")   AS total,
    w."Currency",
    min(w."CreatedAtUtc") AS plus_ancien
FROM settlement.withdrawals w
WHERE w."OwnerType" = 'Courier'
  AND w."Status" IN ('Requested', 'Processing')
GROUP BY w."Status", w."Currency";

-- 1.5 Le détail de ces retraits, avec le compte de destination
SELECT
    w."Id"            AS withdrawal_id,
    w."Status",
    w."Amount",
    w."Currency",
    w."CreatedAtUtc",
    w."ProviderRef",
    c."DisplayName",
    c."PayoutProvider",
    c."PayoutAccountNumber",
    c."PayoutAccountName"
FROM settlement.withdrawals w
LEFT JOIN delivery.couriers c ON c."Id" = w."OwnerId"
WHERE w."OwnerType" = 'Courier'
  AND w."Status" IN ('Requested', 'Processing')
ORDER BY w."CreatedAtUtc";

-- 1.6 Le grand livre coursier, pour le volume d'archive à prévoir
SELECT count(*) AS ecritures, min("CreatedAtUtc") AS depuis, max("CreatedAtUtc") AS jusqu_a
FROM settlement.wallet_transactions
WHERE "OwnerType" = 'Courier';


-- ───────────────────────────────────────────────────────────────────────────────────
-- §2. L'EXPORT — à faire AVANT toute suppression, sans exception
--
-- `\copy` (psql, côté client) plutôt que `COPY` (serveur) : sur une base managée vous
-- n'avez pas de système de fichiers serveur où écrire.
-- ───────────────────────────────────────────────────────────────────────────────────

-- 2.1 Les comptes de versement — LE FICHIER QUI NE DOIT PAS MANQUER
\copy (SELECT * FROM delivery.couriers) TO 'export_couriers.csv' WITH CSV HEADER

-- 2.2 Les portefeuilles coursiers et leurs soldes
\copy (SELECT w.*, a."Account", a."Balance" FROM settlement.wallets w JOIN settlement.wallet_accounts a ON a."WalletId" = w."Id" WHERE w."OwnerType" = 'Courier') TO 'export_wallets_coursiers.csv' WITH CSV HEADER

-- 2.3 Tous les retraits coursiers, quel que soit leur statut
\copy (SELECT * FROM settlement.withdrawals WHERE "OwnerType" = 'Courier') TO 'export_withdrawals_coursiers.csv' WITH CSV HEADER

-- 2.4 Le grand livre — la pièce comptable
\copy (SELECT * FROM settlement.wallet_transactions WHERE "OwnerType" = 'Courier') TO 'export_ledger_coursiers.csv' WITH CSV HEADER

-- 2.5 Le schéma `delivery` en entier, format restaurable
--     À LANCER DEPUIS LE SHELL, pas depuis psql :
--     pg_dump --schema=delivery --format=custom -f delivery_avant_retrait.dump "<chaîne>"


-- ───────────────────────────────────────────────────────────────────────────────────
-- §3. LES CONTRÔLES BLOQUANTS — tant qu'une seule ligne sort, on ne touche à rien
-- ───────────────────────────────────────────────────────────────────────────────────

-- 3.1 Reste-t-il de l'argent sur un portefeuille coursier ?
--     Doit rendre 0. Sinon : payer, ou décider explicitement de l'abandon.
SELECT count(*) AS soldes_non_nuls
FROM settlement.wallets w
JOIN settlement.wallet_accounts a ON a."WalletId" = w."Id"
WHERE w."OwnerType" = 'Courier' AND a."Balance" <> 0;

-- 3.2 Reste-t-il un retrait en cours ?
--     Doit rendre 0. Un `Processing` qui traîne est un versement dont on ignore le sort.
SELECT count(*) AS retraits_ouverts
FROM settlement.withdrawals
WHERE "OwnerType" = 'Courier' AND "Status" IN ('Requested', 'Processing');

-- 3.3 Des portefeuilles coursiers orphelins ? (le coursier n'existe plus)
--     Aucune clé étrangère ne l'empêche. S'il y en a, ils sont DÉJÀ intraçables, et
--     leur solde est déjà perdu — à constater maintenant plutôt qu'après.
SELECT w."OwnerId", a."Balance"
FROM settlement.wallets w
JOIN settlement.wallet_accounts a ON a."WalletId" = w."Id" AND a."Account" = 'Available'
LEFT JOIN delivery.couriers c ON c."Id" = w."OwnerId"
WHERE w."OwnerType" = 'Courier' AND c."Id" IS NULL;

-- 3.4 Des coursiers sans compte de versement renseigné ?
--     Ils ne peuvent pas être payés en l'état : à corriger via
--     `PUT /admin/couriers/{id}/payout`, la seule route qui reste pour cela.
SELECT c."Id", c."DisplayName", c."Phone", a."Balance"
FROM delivery.couriers c
JOIN settlement.wallets w ON w."OwnerId" = c."Id" AND w."OwnerType" = 'Courier'
JOIN settlement.wallet_accounts a ON a."WalletId" = w."Id" AND a."Account" = 'Available'
WHERE a."Balance" > 0
  AND (c."PayoutAccountNumber" IS NULL OR btrim(c."PayoutAccountNumber") = '');


-- ───────────────────────────────────────────────────────────────────────────────────
-- §4. CE QUI N'EST PAS DANS CE FICHIER, ET POURQUOI
--
-- AUCUNE SUPPRESSION, AUCUN UPDATE DE SOLDE. Vider un portefeuille revient à décider
-- qu'une dette n'existe plus ; cela se décide, se trace et se paie — cela ne se
-- script pas. Les retraits se soldent par les routes d'administration encore en place
-- (`/admin/couriers/withdrawals/pending` et `/processing`), qui passent par
-- `ApproveWithdrawalCommand` et laissent une écriture au grand livre. Un UPDATE direct
-- n'en laisserait aucune.
--
-- LES CONSENTEMENTS JURIDIQUES ne sont pas touchés non plus. Les acceptations
-- enregistrées sous `legal_terms_driver` / `legal_privacy_driver` se conservent pour la
-- durée de prescription, même quand la relation cesse. Archiver les documents dans le
-- CMS, garder le registre.
--
-- `DROP SCHEMA delivery CASCADE` n'est pas ici volontairement : il vient après les
-- phases 1 à 3, pas après celle-ci.
-- ───────────────────────────────────────────────────────────────────────────────────
