using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Marketing.Infrastructure.Migrations
{
    /// <summary>
    /// Présentation publique d'une campagne : adresse, texte, visuel, et le drapeau qui
    /// décide de son exposition.
    ///
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// AUCUNE DONNÉE EXISTANTE N'EST TOUCHÉE.
    ///
    /// Les trois colonnes de texte sont nullables ; `IsPublic` est NOT NULL avec un
    /// défaut à `false`, ce qui donne aux campagnes déjà enregistrées exactement le
    /// comportement qu'elles avaient : aucune n'apparaît sur la vitrine tant que
    /// personne ne l'a décidé. C'est la seule valeur de départ acceptable — l'inverse
    /// publierait d'un coup toutes les opérations internes, avec leurs noms de travail.
    ///
    /// L'INDEX SUR LE SLUG EST UNIQUE MAIS FILTRÉ. Sans le filtre, PostgreSQL aurait
    /// accepté plusieurs NULL (il les considère comme distincts) — l'unicité aurait donc
    /// fonctionné par accident. Le filtre le dit explicitement, et documente que
    /// l'absence d'adresse est le cas ordinaire.
    ///
    /// Les migrations s'appliquent au démarrage de l'API : à tester sur une base de
    /// recette d'abord, une migration qui échoue ici relance la pile en boucle.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class AddCampaignPublicPresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                schema: "marketing",
                table: "campaigns",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "marketing",
                table: "campaigns",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeroImageUrl",
                schema: "marketing",
                table: "campaigns",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                schema: "marketing",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_Slug",
                schema: "marketing",
                table: "campaigns",
                column: "Slug",
                unique: true,
                filter: "\"Slug\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_campaigns_Slug",
                schema: "marketing",
                table: "campaigns");

            migrationBuilder.DropColumn(name: "IsPublic", schema: "marketing", table: "campaigns");
            migrationBuilder.DropColumn(name: "HeroImageUrl", schema: "marketing", table: "campaigns");
            migrationBuilder.DropColumn(name: "Description", schema: "marketing", table: "campaigns");
            migrationBuilder.DropColumn(name: "Slug", schema: "marketing", table: "campaigns");
        }
    }
}
