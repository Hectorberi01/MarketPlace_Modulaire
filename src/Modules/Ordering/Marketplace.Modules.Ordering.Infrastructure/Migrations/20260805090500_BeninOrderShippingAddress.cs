using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Ordering.Infrastructure.Migrations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// INSTANTANÉ D'ADRESSE DE LA COMMANDE, ALIGNÉ SUR LE MODÈLE BÉNINOIS.
///
/// Mêmes colonnes que le carnet d'adresses, mêmes précautions — avec une différence de
/// nature : ici on touche à de l'HISTORIQUE. Une commande dit où un colis a été envoyé ;
/// on peut enrichir cette trace, on ne la réécrit pas.
///
/// D'où l'ordre : ajouter, rattraper depuis « ShipToCity », puis seulement supprimer.
/// Ce qui ne se rattrape pas reste NULL — une commande livrée reste livrée, et personne
/// n'a besoin qu'on lui invente rétroactivement une commune.
///
/// « ShipToLandmark » restera vide sur toutes les commandes antérieures : le point de
/// repère n'existait pas, il n'y a rien à en tirer. C'est visible et assumé, plutôt que
/// comblé par une valeur fabriquée.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public partial class BeninOrderShippingAddress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ShipToCommuneCode", schema: "ordering", table: "orders",
            type: "character varying(40)", maxLength: 40, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToQuartier", schema: "ordering", table: "orders",
            type: "character varying(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToLandmark", schema: "ordering", table: "orders",
            type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToCountryCode", schema: "ordering", table: "orders",
            type: "character varying(2)", maxLength: 2, nullable: true);

        // Téléphone : normaliser AVANT de rétrécir la colonne (30 → 20). Sans cela, un
        // seul instantané contenant deux numéros séparés par « / » suffit à faire échouer
        // la migration — donc, puisqu'elles s'appliquent au démarrage, à empêcher l'API
        // de démarrer. Même règle que BeninGeography.NormalizePhone ; ce qui ne s'y plie
        // pas est tronqué, jamais inventé.
        migrationBuilder.Sql(@"
            UPDATE ordering.orders
               SET ""ShipToPhone"" = CASE
                       WHEN regexp_replace(
                                regexp_replace(COALESCE(""ShipToPhone"", ''), '[^0-9]', '', 'g'),
                                '^(?:00229|229)(?=[0-9]{10}$)', '') ~ '^[0-9]{10}$'
                       THEN '+229' || regexp_replace(
                                regexp_replace(COALESCE(""ShipToPhone"", ''), '[^0-9]', '', 'g'),
                                '^(?:00229|229)(?=[0-9]{10}$)', '')
                       ELSE left(COALESCE(""ShipToPhone"", ''), 20)
                   END
             WHERE ""ShipToPhone"" IS NOT NULL;");

        migrationBuilder.AlterColumn<string>(
            name: "ShipToPhone", schema: "ordering", table: "orders",
            type: "character varying(20)", maxLength: 20, nullable: true,
            oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30, oldNullable: true);

        // Rattrapage des communes — table de correspondance et normalisation IDENTIQUES
        // à celles du carnet d'adresses (migration BeninAddressModel). Les deux doivent
        // rester alignées : une même ville doit donner la même commune des deux côtés.
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
            UPDATE ordering.orders AS o
               SET ""ShipToCommuneCode"" = m.code
              FROM mapping AS m
             WHERE o.""ShipToCommuneCode"" IS NULL
               AND o.""ShipToCity"" IS NOT NULL
               AND btrim(regexp_replace(
                       translate(lower(o.""ShipToCity""),
                                 'àáâãäåçèéêëìíîïñòóôõöùúûüýÿÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝ',
                                 'aaaaaaceeeeiiiinooooouuuuyyaaaaaaceeeeiiiinooooouuuuy'),
                       '[^a-z0-9]+', ' ', 'g')) = m.normalized;");

        migrationBuilder.Sql(@"
            UPDATE ordering.orders
               SET ""ShipToCountryCode"" = 'BJ'
             WHERE ""ShipToCity"" IS NOT NULL OR ""ShipToLine1"" IS NOT NULL;");

        migrationBuilder.DropColumn(name: "ShipToCity", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToCountry", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToLine2", schema: "ordering", table: "orders");
    }

    /// <summary>
    /// Retour arrière structurel. « ShipToCity » est reconstruite depuis le CODE de la
    /// commune, pas depuis le libellé d'origine ; quartier et repère sont perdus, aucune
    /// colonne ne les attend. Sauvegarder avant d'exécuter.
    /// </summary>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ShipToCity", schema: "ordering", table: "orders",
            type: "character varying(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToCountry", schema: "ordering", table: "orders",
            type: "character varying(80)", maxLength: 80, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ShipToLine2", schema: "ordering", table: "orders",
            type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.Sql(@"
            UPDATE ordering.orders
               SET ""ShipToCity"" = ""ShipToCommuneCode"",
                   ""ShipToCountry"" = ""ShipToCountryCode"";");

        migrationBuilder.AlterColumn<string>(
            name: "ShipToPhone", schema: "ordering", table: "orders",
            type: "character varying(30)", maxLength: 30, nullable: true,
            oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20, oldNullable: true);

        migrationBuilder.DropColumn(name: "ShipToCommuneCode", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToQuartier", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToLandmark", schema: "ordering", table: "orders");
        migrationBuilder.DropColumn(name: "ShipToCountryCode", schema: "ordering", table: "orders");
    }
}
