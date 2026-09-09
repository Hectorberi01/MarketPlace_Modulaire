using FluentAssertions;
using Marketplace.Modules.Catalog.Domain.Categories;
using Xunit;

namespace Catalog.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// CHAQUE CATÉGORIE DÉFINIT SON PROPRE SCHÉMA D'ATTRIBUTS.
///
/// `AttributeSchema` existait depuis l'origine — colonne jsonb, champ dans les
/// formulaires d'administration, transporté par les commandes — et n'était LU nulle
/// part. Quinze occurrences, toutes en écriture. Les attributs des produits n'étaient
/// donc contraints par rien, et le champ n'était même pas validé comme JSON avant
/// d'atteindre la colonne : une saisie fautive donnait un 500 opaque.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class SchemaAttributsCategorieTests
{
    private const string SchemaChaussures = """
    {
      "allowUnknown": false,
      "attributes": [
        { "key": "pointure", "label": "Pointure", "type": "number", "required": true, "min": 16, "max": 50 },
        { "key": "couleur",  "label": "Couleur",  "type": "enum",   "required": true,
          "values": ["rouge", "bleu", "noir"] },
        { "key": "cuir",     "label": "Cuir véritable", "type": "boolean" },
        { "key": "modele",   "label": "Modèle",   "type": "text", "max": 40 }
      ]
    }
    """;

    private static CategoryAttributeSchema Chaussures() => CategoryAttributeSchema.Parse(SchemaChaussures).Value;

    // ─────────────────────────────────────────────────────────────────────────────
    // Lecture du schéma
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Un_schema_absent_ne_contraint_rien(string? json)
    {
        var schema = CategoryAttributeSchema.Parse(json).Value;

        schema.IsEmpty.Should().BeTrue();
        // Le point qui compte : aucun produit existant ne devient invalide.
        schema.Validate(new Dictionary<string, string> { ["n'importe quoi"] = "vraiment" })
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_json_mal_forme_est_refuse_et_non_leve()
    {
        var result = CategoryAttributeSchema.Parse("{ \"attributes\": [ ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.category.schema_invalid_json");
    }

    [Fact]
    public void Un_type_inconnu_est_refuse()
    {
        var result = CategoryAttributeSchema.Parse(
            """{ "attributes": [ { "key": "poids", "type": "kilogrammes" } ] }""");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.category.schema_type_invalid");
    }

    [Fact]
    public void Un_enum_sans_valeurs_est_refuse()
    {
        var result = CategoryAttributeSchema.Parse(
            """{ "attributes": [ { "key": "couleur", "type": "enum" } ] }""");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.category.schema_values_required");
    }

    [Fact]
    public void Deux_cles_identiques_a_la_casse_pres_sont_refusees()
    {
        // Sinon le schéma déclarerait lui-même les deux facettes qu'il est censé éviter.
        var result = CategoryAttributeSchema.Parse(
            """{ "attributes": [ { "key": "couleur" }, { "key": "Couleur" } ] }""");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.category.schema_key_duplicate");
    }

    [Fact]
    public void Un_minimum_superieur_au_maximum_est_refuse()
    {
        var result = CategoryAttributeSchema.Parse(
            """{ "attributes": [ { "key": "poids", "type": "number", "min": 10, "max": 2 } ] }""");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.category.schema_bounds_invalid");
    }

    [Fact]
    public void Le_schema_lu_porte_ce_qu_il_faut_au_formulaire()
    {
        var schema = Chaussures();

        schema.Attributes.Should().HaveCount(4);
        schema.AllowUnknownAttributes.Should().BeFalse();

        var couleur = schema.Attributes.Single(a => a.Key == "couleur");
        couleur.Label.Should().Be("Couleur");
        couleur.Type.Should().Be(CategoryAttributeType.Enum);
        couleur.Required.Should().BeTrue();
        couleur.Values.Should().BeEquivalentTo(new[] { "rouge", "bleu", "noir" });
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Contrôle des attributs d'un produit
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Un_produit_conforme_passe()
    {
        var result = Chaussures().Validate(new Dictionary<string, string>
        {
            ["pointure"] = "42",
            ["couleur"] = "bleu",
            ["cuir"] = "true",
            ["modele"] = "Sahel"
        });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_attribut_obligatoire_manquant_est_refuse()
    {
        var result = Chaussures().Validate(new Dictionary<string, string> { ["pointure"] = "42" });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.product.attributes_invalid");
        result.Error.Message.Should().Contain("Couleur");
    }

    [Fact]
    public void Tous_les_defauts_sont_rassembles_en_un_seul_message()
    {
        // Un formulaire qui refuse une erreur à la fois oblige à autant d'allers-retours
        // — et sur une fiche produit, un aller-retour veut dire retéléverser les images.
        var result = Chaussures().Validate(new Dictionary<string, string>
        {
            ["pointure"] = "quarante-deux",
            ["couleur"] = "turquoise"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Pointure").And.Contain("Couleur");
    }

    [Fact]
    public void Une_valeur_hors_bornes_est_refusee()
    {
        var result = Chaussures().Validate(new Dictionary<string, string>
        {
            ["pointure"] = "150",
            ["couleur"] = "noir"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Pointure");
    }

    [Fact]
    public void Une_valeur_hors_liste_est_refusee_et_le_message_donne_la_liste()
    {
        var result = Chaussures().Validate(new Dictionary<string, string>
        {
            ["pointure"] = "42",
            ["couleur"] = "turquoise"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("rouge").And.Contain("bleu").And.Contain("noir");
    }

    [Fact]
    public void Un_attribut_hors_schema_est_refuse_quand_la_categorie_ferme_sa_liste()
    {
        var result = Chaussures().Validate(new Dictionary<string, string>
        {
            ["pointure"] = "42",
            ["couleur"] = "noir",
            ["parfum"] = "vanille"
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("parfum");
    }

    [Fact]
    public void Un_attribut_hors_schema_passe_quand_la_categorie_ne_ferme_pas_sa_liste()
    {
        var schema = CategoryAttributeSchema.Parse(
            """{ "attributes": [ { "key": "couleur", "type": "text" } ] }""").Value;

        schema.Validate(new Dictionary<string, string> { ["parfum"] = "vanille" })
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void La_casse_de_la_cle_saisie_ne_fait_pas_echouer_le_controle()
    {
        var result = Chaussures().Validate(new Dictionary<string, string>
        {
            ["Pointure"] = "42",
            ["COULEUR"] = "noir"
        });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_nombre_se_lit_en_culture_invariante()
    {
        // « 1.5 » doit être accepté partout : la valeur est stockée telle quelle et
        // relue par n'importe quel hôte, dont la culture ambiante n'est pas garantie.
        var schema = CategoryAttributeSchema.Parse(
            """{ "attributes": [ { "key": "poids", "type": "number" } ] }""").Value;

        schema.Validate(new Dictionary<string, string> { ["poids"] = "1.5" }).IsSuccess.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Normalisation — la moitié utile que la validation seule ne fait pas
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Les_cles_sont_ramenees_a_celles_du_schema()
    {
        var normalise = Chaussures().Normalize(new Dictionary<string, string>
        {
            ["Pointure"] = "42",
            ["COULEUR"] = " noir "
        });

        normalise.Should().ContainKey("pointure").And.ContainKey("couleur");
        normalise.Should().NotContainKey("Pointure").And.NotContainKey("COULEUR");
        normalise["couleur"].Should().Be("noir");
    }

    [Fact]
    public void Une_cle_hors_schema_est_conservee_telle_quelle()
    {
        var schema = CategoryAttributeSchema.Parse(
            """{ "attributes": [ { "key": "couleur", "type": "text" } ] }""").Value;

        var normalise = schema.Normalize(new Dictionary<string, string> { ["Parfum"] = "vanille" });

        normalise.Should().ContainKey("Parfum");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // La catégorie refuse un schéma illisible à la saisie, pas plus tard
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Creer_une_categorie_avec_un_schema_mal_forme_est_refuse()
    {
        var result = Category.Create("Chaussures", null, null, null, "{ pas du json ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.category.schema_invalid_json");
    }

    [Fact]
    public void Modifier_une_categorie_avec_un_schema_mal_forme_est_refuse()
    {
        var category = Category.Create("Chaussures", null, null, null, SchemaChaussures).Value;

        var result = category.Update("Chaussures", null, null, "{ pas du json ");

        result.IsFailure.Should().BeTrue();
        // Et l'ancien schéma est intact.
        category.ParseSchema().Value.Attributes.Should().HaveCount(4);
    }

    [Fact]
    public void Une_categorie_relit_le_schema_qu_elle_a_enregistre()
    {
        var category = Category.Create("Chaussures", null, null, null, SchemaChaussures).Value;

        var schema = category.ParseSchema();

        schema.IsSuccess.Should().BeTrue();
        schema.Value.Attributes.Select(a => a.Key)
            .Should().BeEquivalentTo(new[] { "pointure", "couleur", "cuir", "modele" });
    }
}
