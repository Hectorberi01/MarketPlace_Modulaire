using FluentAssertions;
using Marketplace.Modules.Catalog.Application.Categories;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;
using Xunit;

namespace Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// UNE CATÉGORIE HÉRITE DES ATTRIBUTS DE TOUTE SA BRANCHE ASCENDANTE.
///
/// Le schéma n'était lu que sur la catégorie du produit. Un article rangé dans
/// « Informatique › Ordinateurs › Ordinateurs portables » n'était donc contraint que
/// par les attributs de la feuille — alors que les attributs COMMUNS (marque,
/// garantie, pays de fabrication) se posent justement sur les étages du haut.
///
/// Rien n'échouait : les fiches passaient, simplement sans les champs généraux. La
/// perte ne se voyait qu'à la recherche à facettes, des mois plus tard, quand
/// « filtrer par marque » ne ramenait rien sur la moitié du rayon.
///
/// L'HÉRITAGE N'EST PAS RECOPIÉ EN BASE. Chaque catégorie ne stocke que ce qu'elle
/// déclare ; la composition se refait à la lecture depuis le chemin matérialisé. Ces
/// tests vérifient la composition ET ce qui en découle : ordre, collisions, produits
/// existants, feuilles.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class HeritageAttributsCategorieTests
{
    // ── L'arbre de l'énoncé ───────────────────────────────────────────────────────
    // Informatique          → marque (obligatoire), garantie (nombre, 0-60 mois)
    //   └── Ordinateurs     → processeur (obligatoire)
    //       └── Portables   → pouces (nombre 10-20), retroeclairage (booléen)

    private const string SchemaInformatique = """
    {
      "attributes": [
        { "key": "marque",   "label": "Marque",             "type": "text",   "required": true, "max": 60 },
        { "key": "garantie", "label": "Garantie (mois)",    "type": "number", "min": 0, "max": 60, "unit": "mois" }
      ]
    }
    """;

    private const string SchemaOrdinateurs = """
    {
      "attributes": [
        { "key": "processeur", "label": "Processeur", "type": "text", "required": true, "max": 80 }
      ]
    }
    """;

    private const string SchemaPortables = """
    {
      "attributes": [
        { "key": "pouces",         "label": "Taille d'écran", "type": "number",  "min": 10, "max": 20, "unit": "\"" },
        { "key": "retroeclairage", "label": "Clavier rétroéclairé", "type": "boolean" }
      ]
    }
    """;

    private static Category Categorie(string nom, Category? parent, string? schema = null)
        => Category.Create(nom, parent?.Id.Value, parent?.Path, imageUrl: null, attributeSchema: schema).Value;

    private static (Category Informatique, Category Ordinateurs, Category Portables) Arbre()
    {
        var informatique = Categorie("Informatique", null, SchemaInformatique);
        var ordinateurs = Categorie("Ordinateurs", informatique, SchemaOrdinateurs);
        var portables = Categorie("Ordinateurs portables", ordinateurs, SchemaPortables);
        return (informatique, ordinateurs, portables);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Chemins d'ancêtres — la brique sur laquelle tout repose
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Les_chemins_dancetres_vont_de_la_racine_au_parent_direct()
    {
        Category.AncestorPathsOf("/informatique/ordinateurs/ordinateurs-portables")
            .Should().Equal("/informatique", "/informatique/ordinateurs");
    }

    [Theory]
    [InlineData("/informatique")]
    [InlineData("informatique")]
    [InlineData("/")]
    [InlineData("")]
    [InlineData(null)]
    public void Une_racine_ou_un_chemin_vide_na_aucun_ancetre(string? chemin)
        => Category.AncestorPathsOf(chemin).Should().BeEmpty();

    // ─────────────────────────────────────────────────────────────────────────────
    // Composition
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_feuille_recoit_les_attributs_des_trois_etages_sans_doublon()
    {
        var (informatique, ordinateurs, portables) = Arbre();

        var resolu = ResolvedCategorySchema.Compose(new[] { informatique, ordinateurs }, portables).Value;

        // `Equal(...)` en FluentAssertions 6 n'a que deux surcharges utiles ici :
        // `Equal(params T[])` et `Equal(IEnumerable<T>, string because, ...)`. Une
        // raison passée à la suite des éléments tombe sur la PREMIÈRE et devient un
        // élément attendu de plus — le test échoue en annonçant six éléments. D'où le
        // tableau explicite.
        resolu.All.Select(a => a.Definition.Key)
            .Should().Equal(
                new[] { "marque", "garantie", "processeur", "pouces", "retroeclairage" },
                "l'ordre va de la racine à la feuille : le vendeur lit le général avant le particulier.");

        resolu.All.Should().OnlyHaveUniqueItems(a => a.Definition.Key);
    }

    [Fact]
    public void Les_attributs_herites_et_propres_sont_separes_et_portent_leur_origine()
    {
        var (informatique, ordinateurs, portables) = Arbre();

        var resolu = ResolvedCategorySchema.Compose(new[] { informatique, ordinateurs }, portables).Value;

        resolu.Own.Select(a => a.Definition.Key).Should().Equal("pouces", "retroeclairage");
        resolu.Inherited.Select(a => a.Definition.Key).Should().Equal("marque", "garantie", "processeur");

        // C'est cette information qui permet à l'administration de dire OÙ modifier un
        // attribut hérité, plutôt que de laisser chercher dans tout l'arbre.
        resolu.Inherited.Single(a => a.Definition.Key == "marque")
            .OwnerCategoryName.Should().Be("Informatique");
        resolu.Inherited.Single(a => a.Definition.Key == "processeur")
            .OwnerCategoryName.Should().Be("Ordinateurs");
    }

    [Fact]
    public void Une_categorie_intermediaire_nherite_que_de_ce_qui_est_au_dessus_delle()
    {
        var (informatique, ordinateurs, _) = Arbre();

        var resolu = ResolvedCategorySchema.Compose(new[] { informatique }, ordinateurs).Value;

        resolu.All.Select(a => a.Definition.Key).Should().Equal("marque", "garantie", "processeur");
        resolu.All.Should().NotContain(a => a.Definition.Key == "pouces",
            "les attributs d'une FEUILLE ne remontent pas vers son parent.");
    }

    [Fact]
    public void Une_racine_na_que_ses_propres_attributs()
    {
        var (informatique, _, _) = Arbre();

        var resolu = ResolvedCategorySchema.Compose(Array.Empty<Category>(), informatique).Value;

        resolu.Inherited.Should().BeEmpty();
        resolu.Own.Should().HaveCount(2);
    }

    [Fact]
    public void Le_schema_effectif_contraint_reellement_un_produit_de_la_feuille()
    {
        var (informatique, ordinateurs, portables) = Arbre();
        var resolu = ResolvedCategorySchema.Compose(new[] { informatique, ordinateurs }, portables).Value;

        // Il manque « marque » (Informatique) ET « processeur » (Ordinateurs) : deux
        // étages que la feuille ne déclare pas, et que rien ne contraignait avant.
        var incomplet = resolu.Effective.Validate(new Dictionary<string, string> { ["pouces"] = "14" });
        incomplet.IsFailure.Should().BeTrue();
        incomplet.Error.Message.Should().Contain("Marque").And.Contain("Processeur");

        var complet = resolu.Effective.Validate(new Dictionary<string, string>
        {
            ["marque"] = "Lenovo",
            ["processeur"] = "Ryzen 7",
            ["pouces"] = "14",
        });
        complet.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Les_bornes_dun_attribut_herite_sappliquent_dans_la_feuille()
    {
        var (informatique, ordinateurs, portables) = Arbre();
        var resolu = ResolvedCategorySchema.Compose(new[] { informatique, ordinateurs }, portables).Value;

        // « garantie » est déclarée sur Informatique avec un maximum de 60 mois.
        var trop = resolu.Effective.Validate(new Dictionary<string, string>
        {
            ["marque"] = "Lenovo",
            ["processeur"] = "Ryzen 7",
            ["garantie"] = "120",
        });

        trop.IsFailure.Should().BeTrue();
        trop.Error.Message.Should().Contain("60");
    }

    [Fact]
    public void Les_cles_sont_normalisees_sur_celles_du_schema_herite()
    {
        var (informatique, ordinateurs, portables) = Arbre();
        var resolu = ResolvedCategorySchema.Compose(new[] { informatique, ordinateurs }, portables).Value;

        var normalise = resolu.Effective.Normalize(new Dictionary<string, string>
        {
            ["MARQUE"] = "Lenovo",
            ["Processeur"] = "Ryzen 7",
        });

        normalise.Should().ContainKey("marque").And.ContainKey("processeur");
        normalise.Should().NotContainKey("MARQUE");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Collisions de clé — l'ancêtre gagne, et on le dit
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Une_cle_redeclaree_par_un_enfant_est_ignoree_au_profit_de_lancetre()
    {
        var informatique = Categorie("Informatique", null, SchemaInformatique);

        // Donnée antérieure au contrôle d'écriture : l'enfant redéclare « marque » en
        // FACULTATIF. Le laisser gagner lui permettrait d'affaiblir son parent.
        var ordinateurs = Categorie("Ordinateurs", informatique, """
        { "attributes": [ { "key": "marque", "label": "Marque libre", "type": "text" } ] }
        """);

        var resolu = ResolvedCategorySchema.Compose(new[] { informatique }, ordinateurs).Value;

        var marque = resolu.All.Single(a => a.Definition.Key == "marque");
        marque.Definition.Required.Should().BeTrue("la définition de l'ancêtre l'emporte.");
        marque.OwnerCategoryName.Should().Be("Informatique");

        // Et la collision est SIGNALÉE : c'est ce qui permet de la nettoyer.
        resolu.Conflicts.Should().ContainSingle()
            .Which.Should().Match<CategoryAttributeConflict>(c =>
                c.Key == "marque" && c.IgnoredCategoryName == "Ordinateurs" && c.WinningCategoryName == "Informatique");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Attributs hors schéma : le plus strict de la branche l'emporte
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_parent_qui_ferme_son_jeu_dattributs_ferme_toute_la_branche()
    {
        var ferme = Categorie("Informatique", null, """
        { "allowUnknown": false, "attributes": [ { "key": "marque", "label": "Marque", "type": "text" } ] }
        """);
        var enfant = Categorie("Ordinateurs", ferme, """
        { "allowUnknown": true, "attributes": [] }
        """);

        var resolu = ResolvedCategorySchema.Compose(new[] { ferme }, enfant).Value;

        resolu.AllowUnknownAttributes.Should().BeFalse(
            "sinon il suffirait de créer une sous-catégorie pour contourner la règle du parent.");

        resolu.Effective.Validate(new Dictionary<string, string> { ["fantaisie"] = "x" })
            .IsFailure.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Un étage illisible arrête toute la branche, et le message le nomme
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_schema_dancetre_illisible_fait_echouer_la_composition_en_nommant_letage()
    {
        var casse = Categorie("Informatique", null);

        // On force une donnée qu'AUCUNE écriture ne produirait — `Create` et `Update`
        // refusent un JSON mal formé. C'est le cas d'une colonne modifiée à la main en
        // base, que le domaine doit refuser lisiblement plutôt que lever.
        // `SetValue` seul ne passerait pas : le setter est privé, il faut le demander.
        typeof(Category).GetProperty(nameof(Category.AttributeSchema))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(casse, new object[] { "{ \"attributes\": [ " });

        var enfant = Categorie("Ordinateurs", casse, SchemaOrdinateurs);

        var resultat = ResolvedCategorySchema.Compose(new[] { casse }, enfant);

        resultat.IsFailure.Should().BeTrue(
            "composer en sautant l'étage donnerait un schéma silencieusement amputé.");
        resultat.Error.Message.Should().Contain("Informatique");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Écriture : une clé héritée ne se redéclare pas
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Declarer_une_cle_deja_portee_par_un_ancetre_est_refuse()
    {
        var (informatique, ordinateurs, _) = Arbre();
        var resolver = new CategorySchemaResolver(new FauxCategories(informatique, ordinateurs));

        var voulu = CategoryAttributeSchema.Parse("""
        { "attributes": [ { "key": "marque", "label": "Marque", "type": "text" } ] }
        """).Value;

        var resultat = await resolver.EnsureNoAncestorKeyClashAsync(ordinateurs.Path, voulu, default);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.category.attribute_key_inherited");
        resultat.Error.Message.Should().Contain("Informatique",
            "le refus doit dire OÙ la clé est déclarée, sinon il faut chercher dans tout l'arbre.");
    }

    [Fact]
    public async Task Une_cle_nouvelle_sous_le_meme_parent_est_acceptee()
    {
        var (informatique, ordinateurs, _) = Arbre();
        var resolver = new CategorySchemaResolver(new FauxCategories(informatique, ordinateurs));

        var voulu = CategoryAttributeSchema.Parse("""
        { "attributes": [ { "key": "pouces", "label": "Taille d'écran", "type": "number" } ] }
        """).Value;

        (await resolver.EnsureNoAncestorKeyClashAsync(ordinateurs.Path, voulu, default))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Une_racine_peut_declarer_nimporte_quelle_cle()
    {
        var resolver = new CategorySchemaResolver(new FauxCategories());
        var voulu = CategoryAttributeSchema.Parse(SchemaInformatique).Value;

        (await resolver.EnsureNoAncestorKeyClashAsync(parentPath: null, voulu, default))
            .IsSuccess.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Écriture : on ne durcit pas un schéma contre des fiches existantes
    // ─────────────────────────────────────────────────────────────────────────────

    private static CategorySchemaCompatibilityGuard Garde(
        Category categorie, params Dictionary<string, string>[] fiches)
        => new(new FauxCategories(categorie), new FauxProduits(fiches));

    [Fact]
    public async Task Retirer_un_attribut_renseigne_sur_des_fiches_est_refuse()
    {
        var (informatique, _, _) = Arbre();
        var garde = Garde(informatique, new Dictionary<string, string> { ["marque"] = "Lenovo" });

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""{ "attributes": [] }""").Value,
            default);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("catalog.category.schema_breaks_products");
        resultat.Error.Message.Should().Contain("Marque");
    }

    [Fact]
    public async Task Rendre_obligatoire_un_attribut_que_des_fiches_ne_portent_pas_est_refuse()
    {
        var (informatique, _, _) = Arbre();
        var garde = Garde(informatique, new Dictionary<string, string> { ["marque"] = "Lenovo" });

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""
            {
              "attributes": [
                { "key": "marque",   "label": "Marque", "type": "text", "required": true },
                { "key": "garantie", "label": "Garantie (mois)", "type": "number", "required": true }
              ]
            }
            """).Value,
            default);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Message.Should().Contain("Garantie")
            .And.Contain("facultatif", "le message doit indiquer le chemin en deux temps.");
    }

    [Fact]
    public async Task Retirer_une_valeur_de_liste_en_usage_est_refuse()
    {
        var couleurs = Categorie("Couleurs", null, """
        { "attributes": [ { "key": "couleur", "label": "Couleur", "type": "enum",
          "values": ["rouge", "bleu", "noir"] } ] }
        """);

        var garde = Garde(couleurs, new Dictionary<string, string> { ["couleur"] = "bleu" });

        var resultat = await garde.EnsureCompatibleAsync(
            couleurs,
            couleurs.ParseSchema().Value,
            CategoryAttributeSchema.Parse("""
            { "attributes": [ { "key": "couleur", "label": "Couleur", "type": "enum",
              "values": ["rouge", "noir"] } ] }
            """).Value,
            default);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Message.Should().Contain("bleu");
    }

    [Fact]
    public async Task Resserrer_une_borne_sous_une_valeur_en_usage_est_refuse()
    {
        var (informatique, _, _) = Arbre();
        var garde = Garde(informatique, new Dictionary<string, string>
        {
            ["marque"] = "Lenovo",
            ["garantie"] = "48",
        });

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""
            {
              "attributes": [
                { "key": "marque",   "label": "Marque", "type": "text", "required": true },
                { "key": "garantie", "label": "Garantie (mois)", "type": "number", "min": 0, "max": 24 }
              ]
            }
            """).Value,
            default);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Message.Should().Contain("48");
    }

    [Fact]
    public async Task Elargir_un_schema_passe_toujours()
    {
        var (informatique, _, _) = Arbre();
        var garde = Garde(informatique, new Dictionary<string, string> { ["marque"] = "Lenovo" });

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""
            {
              "attributes": [
                { "key": "marque",   "label": "Marque", "type": "text" },
                { "key": "garantie", "label": "Garantie (mois)", "type": "number", "min": 0, "max": 120 },
                { "key": "poids",    "label": "Poids", "type": "number" }
              ]
            }
            """).Value,
            default);

        resultat.IsSuccess.Should().BeTrue(
            "rendre facultatif, desserrer une borne et ajouter un champ non obligatoire "
            + "ne cassent aucune fiche existante.");
    }

    [Fact]
    public async Task Fermer_le_jeu_dattributs_contre_des_fiches_qui_en_portent_est_refuse()
    {
        var (informatique, _, _) = Arbre();

        // Une fiche porte une caractéristique LIBRE, hors schéma. Fermer le jeu
        // d'attributs la rendrait instantanément invalide — c'est le durcissement le
        // plus brutal qui existe, et c'est celui qui passait à travers les deux boucles
        // du contrôle, qui ne regardent que les clés DU schéma.
        var garde = Garde(informatique, new Dictionary<string, string>
        {
            ["marque"] = "Lenovo",
            ["couleur_boitier"] = "gris sidéral",
        });

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""
            {
              "allowUnknown": false,
              "attributes": [
                { "key": "marque",   "label": "Marque", "type": "text", "required": true },
                { "key": "garantie", "label": "Garantie (mois)", "type": "number" }
              ]
            }
            """).Value,
            default);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Message.Should().Contain("couleur_boitier");
    }

    [Fact]
    public async Task Fermer_le_jeu_dattributs_passe_si_aucune_fiche_ne_porte_de_cle_libre()
    {
        var (informatique, _, _) = Arbre();
        var garde = Garde(informatique, new Dictionary<string, string> { ["marque"] = "Lenovo" });

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""
            {
              "allowUnknown": false,
              "attributes": [
                { "key": "marque",   "label": "Marque", "type": "text", "required": true },
                { "key": "garantie", "label": "Garantie (mois)", "type": "number" }
              ]
            }
            """).Value,
            default);

        resultat.IsSuccess.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Renommer un ancêtre déplace toute la branche
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rebaser_un_descendant_suit_le_renommage_de_son_ancetre()
    {
        var (informatique, ordinateurs, portables) = Arbre();

        portables.RebasePath(informatique.Path, "/informatique-reseaux").IsSuccess.Should().BeTrue();
        portables.Path.Should().Be("/informatique-reseaux/ordinateurs/ordinateurs-portables");

        ordinateurs.RebasePath(informatique.Path, "/informatique-reseaux").IsSuccess.Should().BeTrue();
        ordinateurs.Path.Should().Be("/informatique-reseaux/ordinateurs");
    }

    [Fact]
    public void Rebaser_une_categorie_hors_de_la_branche_est_refuse()
    {
        var (informatique, _, _) = Arbre();
        var voisine = Categorie("Informatique de gestion", null);

        // Piège classique du chemin matérialisé : « /informatique-de-gestion » commence
        // par les mêmes lettres que « /informatique » sans en être un descendant. C'est
        // le séparateur final qui les sépare.
        var resultat = voisine.RebasePath(informatique.Path, "/informatique-reseaux");

        resultat.IsFailure.Should().BeTrue();
        voisine.Path.Should().Be("/informatique-de-gestion", "un refus ne doit rien écrire.");
    }

    [Fact]
    public void Un_renommage_non_repercute_ferait_perdre_lheritage()
    {
        var (informatique, ordinateurs, portables) = Arbre();

        // On renomme l'ancêtre SANS répercuter : c'est l'état d'avant la correction.
        informatique.Update("Informatique et reseaux", null, null, SchemaInformatique)
            .IsSuccess.Should().BeTrue();

        // Le dépôt retrouve les ancêtres PAR LE CHEMIN : celui de la feuille pointe
        // désormais vers un chemin que plus personne ne porte.
        Category.AncestorPathsOf(portables.Path)
            .Should().NotContain(informatique.Path,
                "c'est exactement la rupture que la répercussion des chemins évite.");

        // Et la composition ne LÈVE PAS : elle compose avec moins d'étages. C'est ce
        // silence qui rend le défaut coûteux.
        var ampute = ResolvedCategorySchema.Compose(new[] { ordinateurs }, portables).Value;
        ampute.All.Should().NotContain(a => a.Definition.Key == "marque");
    }

    [Fact]
    public async Task Sans_aucune_fiche_le_durcissement_le_plus_brutal_est_permis()
    {
        var (informatique, _, _) = Arbre();
        var garde = Garde(informatique); // aucune fiche

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""{ "allowUnknown": false, "attributes": [] }""").Value,
            default);

        resultat.IsSuccess.Should().BeTrue("c'est exactement le moment où l'on veut pouvoir le faire.");
    }

    [Fact]
    public async Task Un_attribut_renseigne_avec_une_autre_casse_compte_comme_utilise()
    {
        var (informatique, _, _) = Arbre();
        // Fiche antérieure à la normalisation des clés : « Marque » et non « marque ».
        var garde = Garde(informatique, new Dictionary<string, string> { ["Marque"] = "Lenovo" });

        var resultat = await garde.EnsureCompatibleAsync(
            informatique,
            CategoryAttributeSchema.Parse(SchemaInformatique).Value,
            CategoryAttributeSchema.Parse("""{ "attributes": [] }""").Value,
            default);

        resultat.IsFailure.Should().BeTrue(
            "la traiter comme absente autoriserait un retrait qui perd la donnée.");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Faux dépôts — juste ce qu'il faut, en mémoire
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class FauxCategories : ICategoryRepository
    {
        private readonly List<Category> _categories;

        public FauxCategories(params Category[] categories) => _categories = categories.ToList();

        public Task<IReadOnlyList<Category>> ListAncestorsAsync(string path, CancellationToken ct = default)
        {
            var chemins = Category.AncestorPathsOf(path);
            IReadOnlyList<Category> trouves = _categories
                .Where(c => chemins.Contains(c.Path))
                .OrderBy(c => c.Path, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(trouves);
        }

        public Task<IReadOnlyList<Category>> ListDescendantsAsync(string path, CancellationToken ct = default)
        {
            var prefixe = path.TrimEnd('/') + "/";
            IReadOnlyList<Category> trouves = _categories.Where(c => c.Path.StartsWith(prefixe, StringComparison.Ordinal)).ToList();
            return Task.FromResult(trouves);
        }

        public Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken ct = default)
            => Task.FromResult(_categories.Any(c => c.ParentId == categoryId));

        public Task<Category?> GetByIdAsync(CategoryId id, CancellationToken ct = default)
            => Task.FromResult(_categories.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<Category>> ListAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Category>>(_categories);

        public Task<bool> PathExistsAsync(string path, Guid? excludeId = null, CancellationToken ct = default)
            => Task.FromResult(_categories.Any(c => c.Path == path && c.Id.Value != excludeId));

        public Task AddAsync(Category category, CancellationToken ct = default)
        {
            _categories.Add(category);
            return Task.CompletedTask;
        }

        public void Remove(Category category) => _categories.Remove(category);
    }

    private sealed class FauxProduits : IProductRepository
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, string>> _attributs;

        public FauxProduits(params Dictionary<string, string>[] fiches)
            => _attributs = fiches.Select(f => (IReadOnlyDictionary<string, string>)f).ToList();

        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ListAttributesByCategoriesAsync(
            IReadOnlyCollection<Guid> categoryIds, CancellationToken ct = default)
            => Task.FromResult(_attributs);

        // Le reste du port n'est pas sollicité par ce contrôle.
        public Task AddAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(Product product) { }
        public Task<Product?> GetByIdAsync(ProductId id, CancellationToken ct = default) => Task.FromResult<Product?>(null);
        public Task<IReadOnlyList<Product>> ListBySellerAsync(Guid sellerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Product>>(Array.Empty<Product>());
        public Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(Guid sellerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Product>>(Array.Empty<Product>());
        public Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Product>>(Array.Empty<Product>());
        public Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
            int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<Product>, int, IReadOnlyDictionary<string, int>)>(
                (Array.Empty<Product>(), 0, new Dictionary<string, int>()));
        public Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
        public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) => Task.FromResult(false);
    }
}
