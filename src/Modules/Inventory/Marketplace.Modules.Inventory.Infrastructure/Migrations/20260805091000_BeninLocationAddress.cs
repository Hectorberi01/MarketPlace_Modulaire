using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Inventory.Infrastructure.Migrations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LIEUX D'EXPÉDITION — MÊME MODÈLE D'ADRESSE QUE LE CARNET ACHETEUR.
///
/// LE CAS PARTICULIER : « address_commune_code » EST NOT NULL.
///
/// Contrairement au carnet acheteur, où l'on tolère des adresses héritées incomplètes,
/// la commune est ici OBLIGATOIRE en base. C'est possible parce que ces lignes sont peu
/// nombreuses et maîtrisées, et parce qu'un lieu d'expédition sans commune rendrait
/// l'offre du vendeur inexpédiable.
///
/// D'où l'ordre STRICT : ajouter en nullable → rattraper depuis « address_city » →
/// combler ce qui reste → SEULEMENT ENSUITE passer en NOT NULL. Poser la contrainte
/// avant le rattrapage ferait échouer la migration, donc le démarrage de l'API.
///
/// LE REPLI SUR « cotonou ». Un lieu dont la ville ne correspond à aucune commune reçoit
/// « cotonou » — ce n'est pas une devinette déguisée, c'est un choix assumé : la
/// contrainte NOT NULL ne laisse pas d'autre issue, et une valeur visiblement fausse sur
/// une poignée de lieux vaut mieux qu'une migration qui refuse de s'appliquer. Le vendeur
/// la corrigera à sa première modification, et la requête ci-dessous les recense :
///
///     SELECT id, address_line FROM inventory.fulfillment_locations
///      WHERE address_commune_code = 'cotonou';
///
/// « address_landmark » reste NULL sur l'existant : le point de repère n'existait pas, il
/// n'y a rien à en tirer. Obligatoire à l'écriture, il se remplit à la première mise à jour.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public partial class BeninLocationAddress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "address_commune_code", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(40)", maxLength: 40, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "address_quartier", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "address_landmark", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "address_country_code", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(2)", maxLength: 2, nullable: false, defaultValue: "BJ");

        // La rue devient facultative : au Bénin, beaucoup n'en ont pas.
        migrationBuilder.AlterColumn<string>(
            name: "address_line", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(500)", maxLength: 500, nullable: true,
            oldClrType: typeof(string), oldType: "character varying(500)", oldMaxLength: 500, oldNullable: false);

        // Rattrapage — table de correspondance et normalisation IDENTIQUES à celles des
        // migrations Identity et Ordering. Les trois doivent rester alignées : une même
        // ville doit produire la même commune partout.
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
            UPDATE inventory.fulfillment_locations AS l
               SET address_commune_code = m.code
              FROM mapping AS m
             WHERE l.address_commune_code IS NULL
               AND l.address_city IS NOT NULL
               AND btrim(regexp_replace(
                       translate(lower(l.address_city),
                                 'àáâãäåçèéêëìíîïñòóôõöùúûüýÿÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝ',
                                 'aaaaaaceeeeiiiinooooouuuuyyaaaaaaceeeeiiiinooooouuuuy'),
                       '[^a-z0-9]+', ' ', 'g')) = m.normalized;");

        // Repli obligé avant la contrainte NOT NULL (voir l'en-tête). L'ancienne ville est
        // conservée dans « address_line » pour que la correction reste possible sans
        // deviner : on préfixe plutôt que d'écraser.
        //
        // `left(..., 500)` N'EST PAS DÉCORATIF. `address_city` fait 150 caractères et
        // `address_line` 500 : concaténées, elles peuvent en produire 651, qu'aucune
        // colonne varchar(500) n'accepte. PostgreSQL rejette l'UPDATE, la migration
        // avorte — et comme elles s'appliquent au démarrage, ni l'API ni les BFF ne
        // démarrent. C'est le même raisonnement que pour les téléphones plus haut ; il
        // manquait ici.
        migrationBuilder.Sql(@"
            UPDATE inventory.fulfillment_locations
               SET address_line = left(btrim(COALESCE(address_city, '') || ' ' || COALESCE(address_line, '')), 500),
                   address_commune_code = 'cotonou'
             WHERE address_commune_code IS NULL;");

        migrationBuilder.AlterColumn<string>(
            name: "address_commune_code", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(40)", maxLength: 40, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(40)", oldMaxLength: 40, oldNullable: true);

        migrationBuilder.DropColumn(name: "address_city", schema: "inventory", table: "fulfillment_locations");
        migrationBuilder.DropColumn(name: "address_country", schema: "inventory", table: "fulfillment_locations");
    }

    /// <summary>Retour arrière structurel : la ville revient sous forme de code, quartier et repère sont perdus.</summary>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "address_city", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(150)", maxLength: 150, nullable: false, defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "address_country", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "");

        migrationBuilder.Sql(@"
            UPDATE inventory.fulfillment_locations
               SET address_city = address_commune_code,
                   address_country = 'BJ',
                   address_line = COALESCE(NULLIF(address_line, ''), NULLIF(address_landmark, ''), '-');");

        migrationBuilder.AlterColumn<string>(
            name: "address_line", schema: "inventory", table: "fulfillment_locations",
            type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: "",
            oldClrType: typeof(string), oldType: "character varying(500)", oldMaxLength: 500, oldNullable: true);

        migrationBuilder.DropColumn(name: "address_commune_code", schema: "inventory", table: "fulfillment_locations");
        migrationBuilder.DropColumn(name: "address_quartier", schema: "inventory", table: "fulfillment_locations");
        migrationBuilder.DropColumn(name: "address_landmark", schema: "inventory", table: "fulfillment_locations");
        migrationBuilder.DropColumn(name: "address_country_code", schema: "inventory", table: "fulfillment_locations");
    }
}
