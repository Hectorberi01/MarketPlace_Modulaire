using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════════
    /// REPRISE DE DONNÉES : LE RENOMMAGE PRÉCÉDENT A CHANGÉ L'UNITÉ, PAS SEULEMENT LE NOM.
    ///
    /// `AddOrderLineTotal` s'est contentée d'un RENAME de `FinalUnitPrice` en `LineTotal`,
    /// parce que c'est tout ce qu'EF Core peut déduire d'un changement de modèle. Or le
    /// domaine ne s'est pas contenté de renommer : trois colonnes de `order_lines` sont
    /// passées de l'UNITÉ à la LIGNE dans le même commit.
    ///
    ///     avant : LineTotal (calculé) = FinalUnitPrice x Quantity
    ///             TotalSellerDiscount = SOMME(SellerDiscount x Quantity)
    ///     après : LineTotal (stocké)  = montant de la ligne, remises déduites
    ///             TotalSellerDiscount = SOMME(SellerDiscount)
    ///
    /// Sans cette reprise, toute commande historique de quantité supérieure à 1 se relit
    /// avec un montant de ligne divisé par sa quantité. Rien ne plante : le payout vendeur
    /// est simplement faux, et il est faux en silence.
    ///
    /// POURQUOI UNE MIGRATION SÉPARÉE, ET NON UNE CORRECTION DE LA PRÉCÉDENTE
    /// `AddOrderLineTotal` est déjà appliquée en production — les migrations tournent au
    /// démarrage de l'API. Modifier son contenu n'aurait donc rien rejoué : EF ne réexécute
    /// jamais une migration dont l'identifiant figure déjà dans `__EFMigrationsHistory`.
    ///
    /// COMMENT ON DISTINGUE UNE LIGNE À REPRENDRE D'UNE LIGNE DÉJÀ CORRECTE
    /// Les totaux de `orders` sont justes dans les DEUX régimes : l'ancien code multipliait
    /// par la quantité, le nouveau somme des montants de ligne. Ils servent donc d'arbitre.
    /// Une commande est à reprendre quand la somme de ses `LineTotal` NE retombe PAS sur
    /// `GrandTotal - ShippingFee`, alors que cette même somme multipliée par les quantités
    /// y retombe exactement. Les commandes entièrement en quantité 1 sont identiques dans
    /// les deux régimes : elles ne satisfont pas la condition, et ne sont pas touchées.
    ///
    /// LE TEST PORTE SUR `LineTotal` SEUL, ET LE GARDE-FOU SUR EXACTEMENT LE MÊME TEST.
    /// Une première version exigeait EN PLUS que les remises se réconcilient au centime,
    /// tout en ne revérifiant ensuite que les deux tests `LineTotal`. Une commande
    /// retouchée — remboursement partiel, arrondi — aurait alors été écartée de la
    /// reprise puis comptée comme un échec par le garde-fou : exception, migration en
    /// échec, API en boucle de redémarrage, et toute la pile avec elle.
    /// Les trois colonnes ont changé d'unité dans le même commit : le régime se lit sur
    /// `LineTotal`, et les remises suivent. Leur non-réconciliation est signalée en
    /// NOTICE — c'est une anomalie antérieure, pas une raison de refuser de démarrer.
    /// ═════════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class BackfillOrderLineTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH commandes_a_reprendre AS (
                    SELECT l."OrderId" AS order_id
                    FROM ordering.order_lines l
                    JOIN ordering.orders o ON o."Id" = l."OrderId"
                    GROUP BY l."OrderId", o."GrandTotal", o."ShippingFee"
                    HAVING SUM(l."LineTotal")                <> o."GrandTotal" - o."ShippingFee"
                       AND SUM(l."LineTotal" * l."Quantity") = o."GrandTotal" - o."ShippingFee"
                )
                UPDATE ordering.order_lines l
                SET "LineTotal"        = l."LineTotal"        * l."Quantity",
                    "SellerDiscount"   = l."SellerDiscount"   * l."Quantity",
                    "PlatformDiscount" = l."PlatformDiscount" * l."Quantity"
                FROM commandes_a_reprendre r
                WHERE l."OrderId" = r.order_id;
                """);

            // Garde-fou : STRICTEMENT le même test que la reprise ci-dessus. S'il reste une
            // commande reconnaissable au format unitaire, c'est que l'UPDATE ne l'a pas
            // touchée, et là on refuse de démarrer — une migration qui échoue se voit, un
            // payout faux, non. Le NOTICE qui suit signale les remises qui ne se
            // réconcilient pas : anomalie de données antérieure, à regarder, sans bloquer.
            migrationBuilder.Sql("""
                DO $remises$
                DECLARE incoherentes integer;
                BEGIN
                    SELECT COUNT(*) INTO incoherentes FROM (
                        SELECT l."OrderId"
                        FROM ordering.order_lines l
                        JOIN ordering.orders o ON o."Id" = l."OrderId"
                        GROUP BY l."OrderId", o."TotalSellerDiscount", o."TotalPlatformDiscount"
                        HAVING SUM(l."SellerDiscount")   <> o."TotalSellerDiscount"
                            OR SUM(l."PlatformDiscount") <> o."TotalPlatformDiscount"
                    ) AS q;
                    IF incoherentes > 0 THEN
                        RAISE NOTICE
                            'Reprise LineTotal : % commande(s) ont des remises de ligne qui ne totalisent pas celles de la commande (anomalie anterieure, non bloquante)', incoherentes;
                    END IF;
                END
                $remises$;
                """);

            migrationBuilder.Sql("""
                DO $verif$
                DECLARE restantes integer;
                BEGIN
                    SELECT COUNT(*) INTO restantes FROM (
                        SELECT l."OrderId"
                        FROM ordering.order_lines l
                        JOIN ordering.orders o ON o."Id" = l."OrderId"
                        GROUP BY l."OrderId", o."GrandTotal", o."ShippingFee"
                        HAVING SUM(l."LineTotal")               <> o."GrandTotal" - o."ShippingFee"
                           AND SUM(l."LineTotal" * l."Quantity") = o."GrandTotal" - o."ShippingFee"
                    ) AS q;
                    IF restantes > 0 THEN
                        RAISE EXCEPTION
                            'Reprise LineTotal incomplete : % commande(s) restent au format unitaire', restantes;
                    END IF;
                END
                $verif$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Le retour en arrière n'est possible que si chaque montant de ligne se divise
            // EXACTEMENT par sa quantité. Une remise de panier à montant fixe — la raison
            // même de ce changement de modèle — ne se divise pas : elle n'a aucune
            // expression unitaire. On échoue explicitement plutôt que d'arrondir en douce.
            migrationBuilder.Sql("""
                DO $verif$
                DECLARE indivisibles integer;
                BEGIN
                    SELECT COUNT(*) INTO indivisibles
                    FROM ordering.order_lines
                    WHERE "Quantity" > 0
                      AND (MOD("LineTotal",        "Quantity") <> 0
                        OR MOD("SellerDiscount",   "Quantity") <> 0
                        OR MOD("PlatformDiscount", "Quantity") <> 0);
                    IF indivisibles > 0 THEN
                        RAISE EXCEPTION
                            'Retour arriere impossible : % ligne(s) ne se divisent pas par leur quantite (remise de panier)', indivisibles;
                    END IF;
                END
                $verif$;
                """);

            migrationBuilder.Sql("""
                UPDATE ordering.order_lines
                SET "LineTotal"        = "LineTotal"        / "Quantity",
                    "SellerDiscount"   = "SellerDiscount"   / "Quantity",
                    "PlatformDiscount" = "PlatformDiscount" / "Quantity"
                WHERE "Quantity" > 0;
                """);
        }
    }
}
