-- =============================================================================
--  AMORÇAGE DES SCHÉMAS — base managée (OVH Cloud Databases)
-- =============================================================================
--
--  À N'UTILISER QUE SI LE DROIT « CREATE » NE PEUT PAS ÊTRE ACCORDÉ.
--
--  La solution normale tient en un réglage : dans la console OVH, onglet
--  « Utilisateurs et droits », passer l'utilisateur applicatif en **Admin** sur la
--  base. EF Core crée alors ses 25 schémas tout seul, et rien de ce fichier n'est
--  nécessaire.
--
--  Ce script est le repli pour le cas où le fournisseur refuse ce niveau de droit :
--  un rôle privilégié crée les schémas À L'AVANCE, puis délègue tout à l'utilisateur
--  applicatif. EF n'a alors plus besoin de « CREATE » sur la base — seulement de
--  créer des tables DANS des schémas existants, ce que « CREATE ON SCHEMA » couvre.
--
--  ─────────────────────────────────────────────────────────────────────────────
--  CE SCRIPT NE CRÉE AUCUNE TABLE, ET C'EST DÉLIBÉRÉ.
--
--  Les tables sont décrites par les migrations EF Core des 25 modules. Les écrire
--  à la main produirait un schéma que l'historique `__ef_migrations_history` ne
--  connaît pas : au démarrage suivant, EF les considérerait comme absentes et
--  tenterait de TOUT recréer, échouant sur des objets déjà présents. On aurait
--  troqué un blocage clair contre une divergence durable entre le code et la base.
--
--  Après ce script, laissez l'API appliquer ses migrations (RunMigrationsAtStartup).
--  ─────────────────────────────────────────────────────────────────────────────
--
--  EXÉCUTION — avec un rôle propriétaire de la base :
--
--    psql "host=<hôte>.eu.clouddb.ovh.net port=35292 dbname=HbaExpressDb \
--          user=<role_proprietaire> sslmode=require" \
--         -v app_user=<utilisateur_applicatif> -f bootstrap-schemas.sql
--
--  Idempotent : relançable sans effet de bord.
-- =============================================================================

\set ON_ERROR_STOP on

-- Sécurité : sans -v app_user, on s'arrêterait après avoir créé des schémas
-- appartenant au mauvais rôle — l'API échouerait alors sur une erreur différente
-- et l'on repartirait dans un diagnostic inutile.
\if :{?app_user}
\else
\echo '>> ERREUR : variable « app_user » non fournie.'
\echo '>> Relancez avec :  -v app_user=<utilisateur_applicatif>'
\quit
\endif

\echo '>> Base    :' :DBNAME
\echo '>> Rôle    :' :USER
\echo '>> Cible   :' :app_user

BEGIN;

-- Les variables psql (« :app_user ») sont substituées CÔTÉ CLIENT : elles n'existent
-- pas à l'intérieur d'un bloc DO, qui est une chaîne exécutée par le serveur. On la
-- transfère donc en paramètre de session, relisible par current_setting().
SET bootstrap.app_user = :'app_user';

DO $$
DECLARE
    -- Un schéma par module. Liste extraite des appels `HasDefaultSchema(...)`
    -- des DbContext : c'est la source de vérité, pas ce fichier.
    --
    -- ⚠️ Un module ajouté plus tard apportera son propre schéma. Si le droit
    -- CREATE reste refusé à l'utilisateur applicatif, il faudra revenir ici —
    -- raison de plus pour préférer le réglage « Admin » côté OVH.
    schemas text[] := ARRAY[
        'analytics', 'billing', 'cart', 'catalog', 'disputes',
        'fraud', 'identity', 'inventory', 'loyalty', 'marketing',
        'messaging', 'notifications', 'offers', 'ordering', 'payments',
        'pricing', 'recommendations', 'returns', 'reviews', 'search',
        'sellers', 'settlement', 'shipping', 'tax', 'wishlist'
    ];
    s text;
    app_user text := current_setting('bootstrap.app_user');
BEGIN
    FOREACH s IN ARRAY schemas LOOP
        EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION %I', s, app_user);

        -- Le propriétaire a tous les droits, mais on est explicite : le schéma
        -- peut préexister avec un autre propriétaire (reprise, restauration).
        EXECUTE format('GRANT ALL ON SCHEMA %I TO %I', s, app_user);
        EXECUTE format('GRANT ALL ON ALL TABLES IN SCHEMA %I TO %I', s, app_user);
        EXECUTE format('GRANT ALL ON ALL SEQUENCES IN SCHEMA %I TO %I', s, app_user);

        -- Objets À VENIR. Sans cette ligne, les tables créées plus tard par les
        -- migrations appartiendraient au créateur sans que l'applicatif y ait
        -- accès si un autre rôle venait à les poser.
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT ALL ON TABLES TO %I', s, app_user);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT ALL ON SEQUENCES TO %I', s, app_user);
    END LOOP;

    RAISE NOTICE '% schémas créés ou vérifiés pour « % ».', array_length(schemas, 1), app_user;
END
$$;

COMMIT;

-- ── Vérification ─────────────────────────────────────────────────────────────
-- Les 25 lignes doivent afficher « t » dans la colonne peut_creer. Une seule à
-- « f » et les migrations du module correspondant échoueront.
SELECT
    n.nspname                                            AS schema,
    pg_get_userbyid(n.nspowner)                          AS proprietaire,
    has_schema_privilege(current_setting('bootstrap.app_user'), n.nspname, 'CREATE') AS peut_creer
FROM pg_namespace n
WHERE n.nspname IN (
    'analytics', 'billing', 'cart', 'catalog', 'disputes',
    'fraud', 'identity', 'inventory', 'loyalty', 'marketing',
    'messaging', 'notifications', 'offers', 'ordering', 'payments',
    'pricing', 'recommendations', 'returns', 'reviews', 'search',
    'sellers', 'settlement', 'shipping', 'tax', 'wishlist'
)
ORDER BY n.nspname;
