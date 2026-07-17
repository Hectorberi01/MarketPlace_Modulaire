-- ═══════════════════════════════════════════════════════════════════════════════
-- ORPHELINS DE SÉVÉRANCE — DIAGNOSTIC PUIS PURGE
--
-- À EXÉCUTER DANS L'ÉDITEUR SQL DE NEON (console web → SQL Editor).
-- Pas besoin de psql, pas de certificat à configurer : copier / coller / exécuter.
--
-- ─── LE CONTEXTE ───
--
-- Neuf relations parent-enfant n'avaient pas `IsRequired()`. EF les considérait donc
-- OPTIONNELLES : quand un enfant quittait sa collection, il ne le SUPPRIMAIT pas, il le
-- « sevrait » — `UPDATE enfant SET FK = NULL`. La ligne restait en base, rattachée à rien.
--
-- Ce n'est NI une perte de données, NI une faille. Le domaine charge toujours les enfants
-- PAR leur clé étrangère : un orphelin (FK = NULL) est invisible et sans effet. Les stocks
-- restent justes, les réactions s'affichent bien, les rôles sont bien révoqués.
--
-- C'est une FUITE DE STOCKAGE, sur le chemin le plus chaud de la plateforme :
--   • stock_reservations : UNE LIGNE MORTE PAR LIGNE DE COMMANDE, à chaque confirmation ET
--     à chaque annulation. La table grossit sans fin.
--   • message_reactions  : une ligne à chaque retrait d'un emoji.
--   • user_roles         : une ligne à chaque révocation de rôle (rare).
--
-- Les SIX AUTRES devraient être à ZÉRO : rien ne supprime jamais une commande, une
-- conversation, un lot de règlement, ni un jeton de rafraîchissement. C'est une PRÉDICTION
-- tirée de la lecture du code — l'étape 1 sert justement à la vérifier.
-- ═══════════════════════════════════════════════════════════════════════════════


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║ ÉTAPE 1 — COMPTER. Ne modifie RIEN. Exécuter ce bloc seul.                ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝
--
-- Le comptage est dynamique (query_to_xml exécute un COUNT par table) et ignore
-- proprement les tables qui n'existeraient pas encore — une base neuve, ou un module
-- pas encore migré, ne fait pas échouer tout le diagnostic.

SELECT
    t.sch || '.' || t.tbl                                        AS table_enfant,
    t.fk                                                         AS colonne_fk,
    (xpath(
        '/row/c/text()',
        query_to_xml(
            format('SELECT count(*) AS c FROM %I.%I WHERE %I IS NULL', t.sch, t.tbl, t.fk),
            false, true, '')
    ))[1]::text::bigint                                          AS orphelins,
    CASE
        WHEN t.tbl IN ('stock_reservations', 'message_reactions', 'user_roles')
            THEN 'attendu : > 0'
        ELSE 'ATTENDU : 0  ⚠️ si > 0, ne rien effacer et chercher'
    END                                                          AS prediction
FROM (VALUES
    ('inventory',  'stock_reservations',        'InventoryItemId'),
    ('messaging',  'message_reactions',         'MessageId'),
    ('identity',   'user_roles',                'UserId'),
    ('identity',   'refresh_tokens',            'UserId'),
    ('messaging',  'conversation_participants', 'ConversationId'),
    ('messaging',  'conversation_messages',     'ConversationId'),
    ('messaging',  'message_hidden_for',        'MessageId'),
    ('ordering',   'order_lines',               'OrderId'),
    ('settlement', 'payouts',                   'SettlementBatchId')
) AS t(sch, tbl, fk)
WHERE EXISTS (
    SELECT 1
      FROM information_schema.columns c
     WHERE c.table_schema = t.sch
       AND c.table_name   = t.tbl
       AND c.column_name  = t.fk
)
ORDER BY orphelins DESC;


-- ╔═══════════════════════════════════════════════════════════════════════════╗
-- ║ ÉTAPE 2 — PURGER.                                                         ║
-- ║                                                                           ║
-- ║ ⚠️ NE PAS EXÉCUTER AVANT D'AVOIR LU LE RÉSULTAT DE L'ÉTAPE 1.             ║
-- ╚═══════════════════════════════════════════════════════════════════════════╝
--
-- Une ligne dont la clé étrangère est NULL n'appartient à AUCUN parent. Le domaine ne peut
-- pas l'atteindre — il charge toujours les enfants PAR leur FK. Elle est morte par
-- construction, et la supprimer ne retire rien à personne.
--
-- MAIS si `ordering.order_lines` ou `settlement.payouts` renvoient autre chose que 0 :
-- ARRÊTEZ-VOUS. Ma lecture du code prédit zéro. Un chiffre non nul signifie que quelque
-- chose supprime des commandes ou des lots de règlement — et ce document ne l'explique pas.
--
-- Décommenter le bloc ci-dessous pour l'exécuter.

/*
BEGIN;

DELETE FROM inventory.stock_reservations        WHERE "InventoryItemId"   IS NULL;
DELETE FROM messaging.message_reactions         WHERE "MessageId"         IS NULL;
DELETE FROM messaging.message_hidden_for        WHERE "MessageId"         IS NULL;
DELETE FROM messaging.conversation_messages     WHERE "ConversationId"    IS NULL;
DELETE FROM messaging.conversation_participants WHERE "ConversationId"    IS NULL;
DELETE FROM identity.user_roles                 WHERE "UserId"            IS NULL;
DELETE FROM identity.refresh_tokens             WHERE "UserId"            IS NULL;
DELETE FROM ordering.order_lines                WHERE "OrderId"           IS NULL;
DELETE FROM settlement.payouts                  WHERE "SettlementBatchId" IS NULL;

COMMIT;
*/

-- Puis relancer l'ÉTAPE 1 : tout doit renvoyer 0. Alors seulement, les migrations
-- `MakeChildForeignKeysRequired` (qui passent ces colonnes en NOT NULL) pourront s'appliquer.
