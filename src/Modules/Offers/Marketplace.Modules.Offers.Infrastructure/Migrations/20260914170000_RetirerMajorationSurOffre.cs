using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Marketplace.Modules.Offers.Infrastructure.Persistence;

#nullable disable

namespace Marketplace.Modules.Offers.Infrastructure.Migrations;

/// <summary>
/// Retire la majoration plateforme empilee sur chaque offre, SANS changer un seul prix
/// affiche.
///
/// <para>
/// LE NOUVEAU MODELE. La plateforme ne se remunere plus article par article
/// (prix_vendeur x (1 + 10 % + 5 %)) mais par un frais unique de 2 % sur le total de la
/// commande. Les deux taux d'offre passent donc a zero, et `ComputeBreakdown` rend
/// desormais productPrice = sellerPrice.
/// </para>
///
/// <para>
/// CE QUE FAIT CETTE MIGRATION, ET CE QU'ELLE NE FAIT PAS. Le prix ACHETEUR ne bouge
/// pas : c'est une decision explicite. On ne rebaisse pas les etiquettes de 13 % du
/// jour au lendemain — la marge retiree a la plateforme est cedee au VENDEUR, dont le
/// prix net remonte au niveau du prix acheteur actuel. Une offre affichee 11 500 reste
/// affichee 11 500 ; le vendeur y touchait 10 000, il y touchera 11 500.
/// </para>
///
/// <para>
/// L'ORDRE DES AFFECTATIONS NE PIEGE PAS : PostgreSQL evalue TOUTES les expressions du
/// SET contre la ligne AVANT modification. `original_seller_price` est donc majore avec
/// les ANCIENS taux, ceux de la ligne, et non avec les zeros qu'on ecrit dans la meme
/// instruction.
/// </para>
///
/// <para>
/// POURQUOI `original_seller_price` AUSSI. C'est le prix net d'AVANT remise, celui que
/// `Offer.RemoveDiscount` reprend pour reconstruire le prix a l'expiration d'une promo.
/// L'oublier ferait chuter le prix de toute offre en promotion au moment ou sa remise
/// se termine — une baisse silencieuse de 13 %, des semaines apres le deploiement, sur
/// les seules offres remisees. `compare_at_amount` n'est pas touche : c'est deja un
/// prix acheteur.
/// </para>
///
/// <para>
/// PAS DE `Down` REVERSIBLE. L'information d'origine — la part vendeur d'avant — n'est
/// plus reconstituable une fois les taux a zero : on ne sait plus si 11 500 etait un
/// prix majore de 15 % ou un prix net saisi tel quel. Un `Down` qui redescendrait les
/// prix « au juge » serait pire que pas de `Down` du tout. Le retour arriere, s'il
/// fallait le faire, passe par une restauration de sauvegarde.
/// </para>
/// </summary>
[DbContext(typeof(OffersDbContext))]
[Migration("20260914170000_RetirerMajorationSurOffre")]
public partial class RetirerMajorationSurOffre : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            UPDATE offers.offers
            SET ""SellerPrice"" = base_price_amount,
                original_seller_price = CASE
                    WHEN original_seller_price IS NULL THEN NULL
                    ELSE ROUND(
                        original_seller_price * (1 + ""CommissionRate"" + ""ProviderFeeRate""),
                        2)
                END,
                ""CommissionAmount""  = 0,
                ""ProviderFeeAmount"" = 0,
                ""CommissionRate""    = 0,
                ""ProviderFeeRate""   = 0;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Volontairement vide : voir l'encadre de la classe. Redescendre les prix
        // supposerait de savoir lesquels avaient ete majores, information que ce
        // « Up » consomme definitivement.
    }
}
