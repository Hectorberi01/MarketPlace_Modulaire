using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Identity.Infrastructure.Migrations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// PASSAGE DE L'ADRESSE AU MODÈLE BÉNINOIS.
///
/// Avant : Line1 + Line2 + City + Country, en texte libre. Après : commune normalisée
/// (77 valeurs), quartier, point de repère, téléphone au format +229.
///
/// TROIS PRÉCAUTIONS, DANS CET ORDRE
///
/// 1. Les colonnes arrivent NULLABLES. Les adresses déjà saisies n'ont ni repère ni
///    commune normalisée : les déclarer NOT NULL obligerait à inventer une valeur pour
///    chacune, c'est-à-dire à envoyer des coursiers à des adresses fabriquées.
///
/// 2. On RATTRAPE ce qui est rattrapable. « Cotonou », « cotonou », « COTONOU » et
///    « Abomey Calavi » désignent tous une commune connue : la correspondance ci-dessous
///    reproduit exactement la normalisation de BeninGeography (minuscules, sans accent,
///    ponctuation repliée en espace unique). Ce qui ne correspond à rien reste NULL —
///    on ne devine pas.
///
/// 3. On ne DÉTRUIT rien tant que le rattrapage n'a pas eu lieu. City et Country sont
///    supprimées APRÈS le backfill ; Line2 l'est aussi, mais elle est vide par
///    construction : aucune surface ne l'a jamais écrite (l'app acheteur omettait le
///    paramètre dans ses deux appels).
///
/// Ce que le rattrapage NE FAIT PAS : inventer un point de repère, ni réparer un
/// téléphone absent. Ces adresses restent lisibles et modifiables, « Address.IsComplete »
/// les signale, et le checkout les refuse jusqu'à ce que l'acheteur les complète.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public partial class BeninAddressModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CommuneCode", schema: "identity", table: "addresses",
            type: "character varying(40)", maxLength: 40, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Quartier", schema: "identity", table: "addresses",
            type: "character varying(120)", maxLength: 120, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Landmark", schema: "identity", table: "addresses",
            type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CountryCode", schema: "identity", table: "addresses",
            type: "character varying(2)", maxLength: 2, nullable: false, defaultValue: "BJ");

        // Line1 devient FACULTATIVE : au Bénin, beaucoup de rues n'ont ni nom ni numéro.
        // C'est le point de repère qui porte l'information utile au livreur.
        migrationBuilder.AlterColumn<string>(
            name: "Line1", schema: "identity", table: "addresses",
            type: "character varying(200)", maxLength: 200, nullable: true,
            oldClrType: typeof(string), oldType: "character varying(200)", oldMaxLength: 200, oldNullable: false);

        // ── Téléphones : NORMALISER D'ABORD, RÉTRÉCIR ENSUITE ───────────────────────
        //
        // L'ordre n'est pas cosmétique. La colonne passe de varchar(30) à varchar(20), et
        // PostgreSQL REFUSE le changement de type si une seule valeur dépasse la nouvelle
        // taille. Un numéro comme « 97 12 34 56 / 96 18 68 06 » (25 caractères) suffisait
        // à faire échouer la migration — donc, puisqu'elles s'appliquent au démarrage, à
        // empêcher l'API de démarrer. En staging on l'aurait vu ; en production, c'est une
        // panne totale déclenchée par une donnée qu'on n'avait pas regardée.
        //
        // On applique donc la même règle que BeninGeography.NormalizePhone :
        //   chiffres seuls → on retire 00229 ou 229 en tête → exactement 10 chiffres
        //   donne « +229XXXXXXXXXX ».
        //
        // Ce qui ne s'y plie pas (ancien numéro à 8 chiffres d'avant la migration de 2024,
        // deux numéros dans le même champ, saisie fantaisiste) est TRONQUÉ à 20 plutôt que
        // corrigé. La valeur reste visible, `Address.IsComplete` la rejette, et l'acheteur
        // devra saisir un vrai numéro à sa prochaine modification. On ne fabrique pas un
        // numéro de téléphone : appeler un inconnu à la place du destinataire serait pire
        // que ne pas appeler.
        // Écrit à plat, sans jointure ni sous-requête : l'expression est répétée deux fois
        // plutôt que factorisée. C'est verbeux, et c'est délibéré — une migration qui
        // tourne une seule fois et qu'on ne peut pas rejouer se relit, elle ne s'admire pas.
        //
        // L'ancrage `(?=[0-9]{10}$)` évite le piège du numéro à 13 chiffres commençant par
        // 229 sans être un indicatif : le préfixe n'est retiré que s'il reste EXACTEMENT
        // 10 chiffres derrière.
        migrationBuilder.Sql(@"
            UPDATE identity.addresses
               SET ""Phone"" = CASE
                       WHEN regexp_replace(
                                regexp_replace(COALESCE(""Phone"", ''), '[^0-9]', '', 'g'),
                                '^(?:00229|229)(?=[0-9]{10}$)', '') ~ '^[0-9]{10}$'
                       THEN '+229' || regexp_replace(
                                regexp_replace(COALESCE(""Phone"", ''), '[^0-9]', '', 'g'),
                                '^(?:00229|229)(?=[0-9]{10}$)', '')
                       ELSE left(COALESCE(""Phone"", ''), 20)
                   END;");

        // Le téléphone passe de 30 à 20 : la forme stockée est désormais « +229 » suivi
        // de 10 chiffres, soit 13 caractères. 20 laisse de la marge sans mentir.
        migrationBuilder.AlterColumn<string>(
            name: "Phone", schema: "identity", table: "addresses",
            type: "character varying(20)", maxLength: 20, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30, oldNullable: false);

        // ── Rattrapage des villes saisies en texte libre ────────────────────────────
        //
        // La normalisation SQL est le MIROIR de BeninGeography.Normalize :
        //   repli des accents → minuscules → tout ce qui n'est pas alphanumérique devient
        //   un espace → espaces repliés → trim.
        //
        // `translate` plutôt que l'extension `unaccent` : une migration qui exige une
        // extension échoue sur une base gérée qui ne l'autorise pas, et bloque alors le
        // démarrage de l'API — les migrations s'appliquent au boot. `translate` est du
        // SQL standard, disponible partout, et la table de repli couvre les caractères
        // qui apparaissent réellement dans les 77 libellés et en français.
        //
        // Sans repli d'accents, la regex ASCII avalerait les lettres accentuées :
        // « Zè » deviendrait « z » et « Pobè » « pob ». Des clés d'un ou trois caractères
        // qu'une saisie sans rapport pourrait percuter.
        //
        // Les 82 clés ci-dessous sont ENGENDRÉES depuis BeninGeography : libellé accentué,
        // code, et les alias réellement rencontrés (« Calavi », « Sèmè-Kpodji »…). Elles
        // ont été vérifiées sans collision, et chaque commune se retrouve depuis son
        // libellé comme depuis son code.
        //
        // Ce qui ne correspond à AUCUNE clé reste NULL. On ne devine pas : une adresse
        // « Lomé » n'est pas une adresse béninoise, et lui attribuer Cotonou enverrait un
        // coursier à 150 km de la bonne maison.
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
            UPDATE identity.addresses AS a
               SET ""CommuneCode"" = m.code
              FROM mapping AS m
             WHERE a.""CommuneCode"" IS NULL
               AND a.""City"" IS NOT NULL
               AND btrim(regexp_replace(
                       translate(lower(a.""City""),
                                 'àáâãäåçèéêëìíîïñòóôõöùúûüýÿ',
                                 'aaaaaaceeeeiiiinooooouuuuyy'),
                       '[^a-z0-9]+', ' ', 'g')) = m.normalized;");

        // Le pays devient un code ISO : « Bénin », « BENIN » et « BJ » convergent.
        migrationBuilder.Sql(@"UPDATE identity.addresses SET ""CountryCode"" = 'BJ';");

        // ── Suppression, APRÈS rattrapage ───────────────────────────────────────────
        migrationBuilder.DropColumn(name: "City", schema: "identity", table: "addresses");
        migrationBuilder.DropColumn(name: "Country", schema: "identity", table: "addresses");
        migrationBuilder.DropColumn(name: "Line2", schema: "identity", table: "addresses");

        migrationBuilder.CreateIndex(
            name: "IX_addresses_CommuneCode", schema: "identity", table: "addresses", column: "CommuneCode");
    }

    /// <summary>
    /// Retour arrière STRUCTUREL, pas informationnel.
    ///
    /// Les colonnes reviennent, mais City est reconstruite depuis le CODE de la commune
    /// (« abomey-calavi »), pas depuis son libellé d'origine (« Abomey Calavi ») : la
    /// casse et les accents saisis par l'utilisateur sont définitivement perdus. Quartier
    /// et point de repère le sont aussi — aucune colonne d'accueil ne les attend.
    ///
    /// Autrement dit : ce Down remet l'application en marche, il ne restaure pas les
    /// données. Avant de l'exécuter en production, faire une sauvegarde.
    /// </summary>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_addresses_CommuneCode", schema: "identity", table: "addresses");

        migrationBuilder.AddColumn<string>(
            name: "City", schema: "identity", table: "addresses",
            type: "character varying(120)", maxLength: 120, nullable: false, defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Country", schema: "identity", table: "addresses",
            type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Line2", schema: "identity", table: "addresses",
            type: "character varying(200)", maxLength: 200, nullable: true);

        migrationBuilder.Sql(@"
            UPDATE identity.addresses
               SET ""City"" = COALESCE(""CommuneCode"", ''),
                   ""Country"" = 'BJ';");

        // Line1 redevient obligatoire : les adresses sans rue reçoivent le repère, à
        // défaut le quartier. Sans quoi la remontée violerait la contrainte NOT NULL.
        migrationBuilder.Sql(@"
            UPDATE identity.addresses
               SET ""Line1"" = COALESCE(NULLIF(""Line1"", ''), NULLIF(""Landmark"", ''), NULLIF(""Quartier"", ''), '-')
             WHERE ""Line1"" IS NULL OR ""Line1"" = '';");

        migrationBuilder.AlterColumn<string>(
            name: "Line1", schema: "identity", table: "addresses",
            type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: "",
            oldClrType: typeof(string), oldType: "character varying(200)", oldMaxLength: 200, oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Phone", schema: "identity", table: "addresses",
            type: "character varying(30)", maxLength: 30, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20, oldNullable: false);

        migrationBuilder.DropColumn(name: "CommuneCode", schema: "identity", table: "addresses");
        migrationBuilder.DropColumn(name: "Quartier", schema: "identity", table: "addresses");
        migrationBuilder.DropColumn(name: "Landmark", schema: "identity", table: "addresses");
        migrationBuilder.DropColumn(name: "CountryCode", schema: "identity", table: "addresses");
    }
}
