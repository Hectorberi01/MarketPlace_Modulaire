using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Sellers.Infrastructure.Migrations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// INFOS SOCIÉTÉ : « city » DEVIENT « commune », NORMALISÉE.
///
/// AUCUN CHANGEMENT DE SCHÉMA. Les infos société vivent dans une colonne jsonb ; seule
/// une CLÉ du document change. C'est pour cela que le snapshot EF est identique à celui
/// de la migration précédente : rien à comparer, rien à diffuser.
///
/// LES CLÉS SONT EN camelCase. Le converter sérialise avec
/// « JsonSerializerDefaults.Web », donc la clé est « city », pas « City ». Chercher
/// « City » ici ne trouverait rien, la migration passerait au vert, et les vendeurs
/// perdraient silencieusement leur commune — le champ étant facultatif, personne ne
/// s'en apercevrait avant longtemps.
///
/// Ce que fait la migration : retirer « city », et ajouter « commune » avec le CODE
/// correspondant quand la ville en désigne une. Sinon, on retire « city » sans rien
/// ajouter : mieux vaut un champ déclaratif vide qu'une commune inventée dans un dossier
/// de vérification.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public partial class BeninSellerCommune : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Villes reconnues → clé « commune » portant le code.
        migrationBuilder.Sql(@"
            WITH mapping (normalized, code) AS (
                VALUES
                    ('abomey', 'abomey'),
                    ('abomey calavi', 'abomey-calavi'),
                    ('adja ouere', 'adja-ouere'),
                    ('adjarra', 'adjarra'),
                    ('adjohoun', 'adjohoun'),
                    ('agbangnizoun', 'agbangnizoun'),
                    ('aguegues', 'aguegues'),
                    ('akpro misserete', 'akpro-misserete'),
                    ('allada', 'allada'),
                    ('aplahoue', 'aplahoue'),
                    ('athieme', 'athieme'),
                    ('avrankou', 'avrankou'),
                    ('banikoara', 'banikoara'),
                    ('bante', 'bante'),
                    ('bassila', 'bassila'),
                    ('bembereke', 'bembereke'),
                    ('bohicon', 'bohicon'),
                    ('bonou', 'bonou'),
                    ('bopa', 'bopa'),
                    ('boukoumbe', 'boukoumbe'),
                    ('calavi', 'abomey-calavi'),
                    ('cobly', 'cobly'),
                    ('come', 'come'),
                    ('copargo', 'copargo'),
                    ('cotonou', 'cotonou'),
                    ('cove', 'cove'),
                    ('dangbo', 'dangbo'),
                    ('dassa zoume', 'dassa-zoume'),
                    ('djakotomey', 'djakotomey'),
                    ('djidja', 'djidja'),
                    ('djougou', 'djougou'),
                    ('dogbo', 'dogbo'),
                    ('dogbo tota', 'dogbo'),
                    ('glazoue', 'glazoue'),
                    ('gogounou', 'gogounou'),
                    ('grand popo', 'grand-popo'),
                    ('houeyogbe', 'houeyogbe'),
                    ('ifangni', 'ifangni'),
                    ('kalale', 'kalale'),
                    ('kandi', 'kandi'),
                    ('karimama', 'karimama'),
                    ('kerou', 'kerou'),
                    ('ketou', 'ketou'),
                    ('klouekanme', 'klouekanme'),
                    ('kouande', 'kouande'),
                    ('kpomasse', 'kpomasse'),
                    ('lalo', 'lalo'),
                    ('lokossa', 'lokossa'),
                    ('malanville', 'malanville'),
                    ('materi', 'materi'),
                    ('n dali', 'n-dali'),
                    ('natitingou', 'natitingou'),
                    ('nikki', 'nikki'),
                    ('ouake', 'ouake'),
                    ('ouassa pehunco', 'ouassa-pehunco'),
                    ('ouesse', 'ouesse'),
                    ('ouidah', 'ouidah'),
                    ('ouinhi', 'ouinhi'),
                    ('parakou', 'parakou'),
                    ('pehunco', 'ouassa-pehunco'),
                    ('perere', 'perere'),
                    ('pobe', 'pobe'),
                    ('porto novo', 'porto-novo'),
                    ('sakete', 'sakete'),
                    ('savalou', 'savalou'),
                    ('save', 'save'),
                    ('segbana', 'segbana'),
                    ('seme kpodji', 'seme-podji'),
                    ('seme podji', 'seme-podji'),
                    ('semekpodji', 'seme-podji'),
                    ('sinende', 'sinende'),
                    ('so ava', 'so-ava'),
                    ('tanguieta', 'tanguieta'),
                    ('tchaourou', 'tchaourou'),
                    ('toffo', 'toffo'),
                    ('tori bossito', 'tori-bossito'),
                    ('toucountouna', 'toucountouna'),
                    ('toviklin', 'toviklin'),
                    ('za kpota', 'za-kpota'),
                    ('zagnanado', 'zagnanado'),
                    ('ze', 'ze'),
                    ('zogbodomey', 'zogbodomey')
            )
            UPDATE sellers.sellers AS s
               SET metadata = (s.metadata - 'city') || jsonb_build_object('commune', m.code)
              FROM mapping AS m
             WHERE s.metadata IS NOT NULL
               AND s.metadata ? 'city'
               AND btrim(regexp_replace(
                       translate(lower(s.metadata ->> 'city'),
                                 'àáâãäåçèéêëìíîïñòóôõöùúûüýÿÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝ',
                                 'aaaaaaceeeeiiiinooooouuuuyyaaaaaaceeeeiiiinooooouuuuy'),
                       '[^a-z0-9]+', ' ', 'g')) = m.normalized;");

        // 2. Villes non reconnues (ou vides) → on retire simplement la clé obsolète.
        //    Sans cette étape, « city » resterait dans le document, invisible du modèle
        //    C# et donc effacée au premier enregistrement du vendeur : autant l'assumer.
        migrationBuilder.Sql(@"
            UPDATE sellers.sellers
               SET metadata = metadata - 'city'
             WHERE metadata IS NOT NULL
               AND metadata ? 'city';");
    }

    /// <summary>
    /// Remet la clé « city » avec le CODE de commune (« abomey-calavi »), pas le libellé
    /// d'origine : celui-ci n'est plus connu.
    /// </summary>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            UPDATE sellers.sellers
               SET metadata = (metadata - 'commune') || jsonb_build_object('city', metadata ->> 'commune')
             WHERE metadata IS NOT NULL
               AND metadata ? 'commune';");
    }
}
