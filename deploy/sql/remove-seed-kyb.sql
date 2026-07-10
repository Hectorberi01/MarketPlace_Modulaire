-- =====================================================================
--  Supprime la pièce KYB de DÉMONSTRATION de la boutique semée.
--  Cette pièce pointait vers un fichier qui n'existe pas dans R2
--  (URL fictive « seed-registry.pdf ») → téléchargement en NoSuchKey.
--
--  Le vendeur RESTE « vérifié » : le statut KYB est stocké sur la table
--  sellers.sellers (colonne KybStatus), pas sur les documents. Supprimer
--  une pièce ne dé-vérifie donc pas la boutique.
--
--  Exécution (Neon staging) :
--    psql "postgres://…neon…/neondb?sslmode=require" -f remove-seed-kyb.sql
--  ou coller dans la console SQL de Neon.
-- =====================================================================

DELETE FROM sellers.kyb_documents
WHERE "FileUrl" = 'https://hbamediacore.fr/uploads/kyb/seed-registry.pdf';
