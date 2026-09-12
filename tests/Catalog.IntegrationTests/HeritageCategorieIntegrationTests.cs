using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Categories;
using Marketplace.Modules.Catalog.Application.Products;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Infrastructure;
using Marketplace.Modules.Catalog.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Catalog.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// L'HÉRITAGE D'ATTRIBUTS, DE BOUT EN BOUT, SUR UN VRAI POSTGRESQL.
///
/// Les tests unitaires vérifient la COMPOSITION ; ceux-ci vérifient ce que les tests
/// unitaires ne peuvent pas voir, et c'est là que se cachent les pannes de ce genre de
/// dispositif :
///
///   • que la remontée des ancêtres par le chemin matérialisé produise vraiment la
///     bonne branche, dans le bon ORDRE, une fois traduite en SQL ;
///   • que le contrôle des produits lise bien le schéma COMPOSÉ et refuse une fiche à
///     laquelle il manque un attribut d'un étage supérieur ;
///   • qu'un produit ne puisse pas naître dans une catégorie intermédiaire ;
///   • qu'un schéma ne puisse pas se durcir contre des fiches déjà en base.
///
/// Le dernier point est le seul qui ne peut PAS être vérifié autrement : il lit les
/// attributs des produits à travers un convertisseur de valeur vers une colonne jsonb,
/// et c'est exactement le genre de projection qui compile, passe les tests à faux
/// dépôts, et échoue à l'exécution.
///
/// Docker requis (Testcontainers), comme les autres tests de ce projet.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// UN SEUL CONTENEUR POUR TOUTE LA CLASSE. xUnit construit une instance de classe de
/// test PAR TEST : sans fixture partagée, ces dix tests démarreraient dix PostgreSQL,
/// soit plusieurs minutes de CI pour vérifier des règles qui n'écrivent presque rien.
/// L'arbre de l'énoncé est semé une fois, en fixture ; les tests qui créent des fiches
/// n'en retirent jamais, et aucun ne dépend de l'absence de ce que les autres ajoutent.
/// </remarks>
public sealed class HeritageCategorieFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("marketplace")
        .Build();

    public ServiceProvider Services { get; private set; } = default!;

    public Guid Informatique { get; private set; }
    public Guid Ordinateurs { get; private set; }
    public Guid Portables { get; private set; }

    private const string SchemaInformatique = """
    {
      "attributes": [
        { "key": "marque",   "label": "Marque",          "type": "text",   "required": true, "max": 60 },
        { "key": "garantie", "label": "Garantie (mois)", "type": "number", "min": 0, "max": 60 }
      ]
    }
    """;

    private const string SchemaOrdinateurs = """
    { "attributes": [ { "key": "processeur", "label": "Processeur", "type": "text", "required": true } ] }
    """;

    private const string SchemaPortables = """
    { "attributes": [ { "key": "pouces", "label": "Taille d'écran", "type": "number", "min": 10, "max": 20 } ] }
    """;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Marketplace"] = _postgres.GetConnectionString(),
                ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-bytes-long!!"
            })
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBuildingBlocksInfrastructure();
        new CatalogModuleInstaller().Install(serviceCollection, configuration);

        // MEDIATR N'EST CÂBLÉ QUE DANS LES HÔTES (`Program.cs`), pas dans l'installateur
        // de module. Sans cette ligne, `ISender` ne se résout pas et les tests qui font
        // passer une COMMANDE — donc ceux qui vérifient la répercussion des chemins et
        // l'éviction de cache — échoueraient sur une erreur d'injection, pas sur la
        // règle qu'ils testent.
        serviceCollection.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(
                typeof(Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory
                    .UpdateCategoryCommand).Assembly));

        Services = serviceCollection.BuildServiceProvider();

        await using (var scope = Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        Informatique = await CreerCategorieAsync("Informatique", null, SchemaInformatique);
        Ordinateurs = await CreerCategorieAsync("Ordinateurs", Informatique, SchemaOrdinateurs);
        Portables = await CreerCategorieAsync("Ordinateurs portables", Ordinateurs, SchemaPortables);
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public async Task<Guid> CreerCategorieAsync(string nom, Guid? parentId, string? schema)
    {
        await using var scope = Services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>();

        string? parentPath = null;
        if (parentId is { } id)
        {
            parentPath = (await categories.GetByIdAsync(new CategoryId(id)))!.Path;
        }

        var categorie = Category.Create(nom, parentId, parentPath, imageUrl: null, attributeSchema: schema).Value;
        await categories.AddAsync(categorie);
        await unitOfWork.SaveChangesAsync();

        return categorie.Id.Value;
    }

    /// <summary>
    /// Crée une fiche avec ses attributs. Le NOM porte un suffixe unique : le slug en
    /// dérive, et deux fiches homonymes dans un conteneur partagé se marcheraient dessus.
    /// </summary>
    public async Task CreerProduitAsync(Guid categoryId, Dictionary<string, string> attributs)
    {
        await using var scope = Services.CreateAsyncScope();
        var produits = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>();

        var produit = Product.Create(
            Guid.NewGuid(),
            categoryId,
            $"ThinkPad {Guid.NewGuid():N}",
            "Ordinateur portable",
            attributes: attributs).Value;

        await produits.AddAsync(produit);
        await unitOfWork.SaveChangesAsync();
    }
}

public sealed class HeritageCategorieIntegrationTests : IClassFixture<HeritageCategorieFixture>
{
    private readonly HeritageCategorieFixture _fixture;

    public HeritageCategorieIntegrationTests(HeritageCategorieFixture fixture) => _fixture = fixture;

    private ServiceProvider _services => _fixture.Services;
    private Guid _informatique => _fixture.Informatique;
    private Guid _ordinateurs => _fixture.Ordinateurs;
    private Guid _portables => _fixture.Portables;

    // ─────────────────────────────────────────────────────────────────────────────
    // La remontée par le chemin matérialisé, traduite en SQL
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Les_ancetres_remontent_dans_lordre_racine_vers_parent()
    {
        await using var scope = _services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();

        var feuille = (await categories.GetByIdAsync(new CategoryId(_portables)))!;
        var ancetres = await categories.ListAncestorsAsync(feuille.Path);

        // Tableau explicite : une raison passée après les éléments serait prise pour un
        // élément attendu de plus (voir la note dans les tests unitaires).
        ancetres.Select(a => a.Name).Should().Equal(
            new[] { "Informatique", "Ordinateurs" },
            "l'ordre est contractuel : il décide quel étage l'emporte sur une clé déclarée deux fois.");
    }

    [Fact]
    public async Task Une_branche_voisine_nest_pas_prise_pour_un_ancetre()
    {
        // « Informatique de gestion » commence par le même préfixe qu'« Informatique »
        // sans en être un descendant. C'est le piège classique du chemin matérialisé,
        // et celui que le séparateur final évite.
        var voisine = await CreerCategorieAsync("Informatique de gestion", null, null);
        var sousVoisine = await CreerCategorieAsync("Progiciels", voisine, null);

        await using var scope = _services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();

        var enfant = (await categories.GetByIdAsync(new CategoryId(sousVoisine)))!;
        var ancetres = await categories.ListAncestorsAsync(enfant.Path);

        ancetres.Select(a => a.Name).Should().Equal("Informatique de gestion");
        ancetres.Should().NotContain(a => a.Name == "Informatique");
    }

    [Fact]
    public async Task Une_feuille_est_reconnue_comme_telle_et_pas_ses_parents()
    {
        await using var scope = _services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();

        (await categories.HasChildrenAsync(_informatique)).Should().BeTrue();
        (await categories.HasChildrenAsync(_ordinateurs)).Should().BeTrue();
        (await categories.HasChildrenAsync(_portables)).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Le contrôle des produits lit le schéma composé
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_produit_de_la_feuille_doit_porter_les_attributs_des_trois_etages()
    {
        await using var scope = _services.CreateAsyncScope();
        var garde = scope.ServiceProvider.GetRequiredService<ProductAttributeGuard>();

        var incomplet = await garde.ResolveAsync(
            _portables, new Dictionary<string, string> { ["pouces"] = "14" }, default, forCreation: true);

        incomplet.IsFailure.Should().BeTrue(
            "« marque » vient d'Informatique et « processeur » d'Ordinateurs : la feuille "
            + "ne les déclare pas, et rien ne les contraignait avant l'héritage.");
        incomplet.Error.Message.Should().Contain("Marque").And.Contain("Processeur");

        var complet = await garde.ResolveAsync(
            _portables,
            new Dictionary<string, string>
            {
                ["marque"] = "Lenovo",
                ["processeur"] = "Ryzen 7",
                ["pouces"] = "14",
            },
            default,
            forCreation: true);

        complet.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Une_borne_declaree_sur_la_racine_sapplique_dans_la_feuille()
    {
        await using var scope = _services.CreateAsyncScope();
        var garde = scope.ServiceProvider.GetRequiredService<ProductAttributeGuard>();

        var resultat = await garde.ResolveAsync(
            _portables,
            new Dictionary<string, string>
            {
                ["marque"] = "Lenovo",
                ["processeur"] = "Ryzen 7",
                ["garantie"] = "120",
            },
            default,
            forCreation: true);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Message.Should().Contain("60");
    }

    [Fact]
    public async Task Un_produit_ne_peut_pas_naitre_dans_une_categorie_intermediaire()
    {
        await using var scope = _services.CreateAsyncScope();
        var garde = scope.ServiceProvider.GetRequiredService<ProductAttributeGuard>();

        var attributs = new Dictionary<string, string> { ["marque"] = "Lenovo", ["processeur"] = "Ryzen 7" };

        var creation = await garde.ResolveAsync(_ordinateurs, attributs, default, forCreation: true);
        creation.IsFailure.Should().BeTrue();
        creation.Error.Code.Should().Be("catalog.product.category_not_leaf");

        // La MODIFICATION, elle, reste possible : des fiches existent dans des
        // catégories devenues intermédiaires, et les enfermer punirait le vendeur
        // d'une décision de l'administration.
        var modification = await garde.ResolveAsync(_ordinateurs, attributs, default, forCreation: false);
        modification.IsSuccess.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Écriture : clés héritées et compatibilité avec les fiches en base
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Redeclarer_une_cle_dun_ancetre_est_refuse()
    {
        await using var scope = _services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var resolver = scope.ServiceProvider.GetRequiredService<CategorySchemaResolver>();

        var parent = (await categories.GetByIdAsync(new CategoryId(_ordinateurs)))!;
        var voulu = CategoryAttributeSchema.Parse("""
        { "attributes": [ { "key": "marque", "label": "Marque", "type": "text" } ] }
        """).Value;

        var resultat = await resolver.EnsureNoAncestorKeyClashAsync(parent.Path, voulu, default);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Message.Should().Contain("Informatique");
    }

    [Fact]
    public async Task Durcir_un_schema_contre_des_fiches_existantes_est_refuse()
    {
        // Une fiche dans la FEUILLE, sans « garantie ».
        await CreerProduitAsync(_portables, new Dictionary<string, string>
        {
            ["marque"] = "Lenovo",
            ["processeur"] = "Ryzen 7",
        });

        await using var scope = _services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var garde = scope.ServiceProvider.GetRequiredService<CategorySchemaCompatibilityGuard>();

        // On durcit sur la RACINE : la fiche est trois étages plus bas, et c'est
        // précisément la portée que ce contrôle doit couvrir.
        var racine = (await categories.GetByIdAsync(new CategoryId(_informatique)))!;

        var resultat = await garde.EnsureCompatibleAsync(
            racine,
            racine.ParseSchema().Value,
            CategoryAttributeSchema.Parse("""
            {
              "attributes": [
                { "key": "marque",   "label": "Marque", "type": "text", "required": true },
                { "key": "garantie", "label": "Garantie (mois)", "type": "number", "required": true }
              ]
            }
            """).Value,
            default);

        resultat.IsFailure.Should().BeTrue(
            "la fiche ne porte pas « garantie » : la rendre obligatoire la rendrait "
            + "impossible à enregistrer, y compris pour en corriger le prix.");
        resultat.Error.Code.Should().Be("catalog.category.schema_breaks_products");
    }

    [Fact]
    public async Task Elargir_un_schema_reste_possible_avec_des_fiches_en_base()
    {
        await CreerProduitAsync(_portables, new Dictionary<string, string>
        {
            ["marque"] = "Lenovo",
            ["processeur"] = "Ryzen 7",
        });

        await using var scope = _services.CreateAsyncScope();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryRepository>();
        var garde = scope.ServiceProvider.GetRequiredService<CategorySchemaCompatibilityGuard>();

        var racine = (await categories.GetByIdAsync(new CategoryId(_informatique)))!;

        var resultat = await garde.EnsureCompatibleAsync(
            racine,
            racine.ParseSchema().Value,
            CategoryAttributeSchema.Parse("""
            {
              "attributes": [
                { "key": "marque",   "label": "Marque", "type": "text", "required": true },
                { "key": "garantie", "label": "Garantie (mois)", "type": "number", "min": 0, "max": 120 },
                { "key": "origine",  "label": "Pays de fabrication", "type": "text" }
              ]
            }
            """).Value,
            default);

        resultat.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Renommer_un_ancetre_repercute_les_chemins_de_toute_la_branche()
    {
        // Une branche à part, pour ne pas déplacer l'arbre que les autres tests lisent.
        var racine = await CreerCategorieAsync("Electromenager", null, null);
        var milieu = await CreerCategorieAsync("Froid", racine, null);
        var feuille = await CreerCategorieAsync("Refrigerateurs", milieu, null);

        await using (var scope = _services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
            var resultat = await sender.Send(
                new Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory
                    .UpdateCategoryCommand(racine, "Gros electromenager", null, null));

            resultat.IsSuccess.Should().BeTrue();
        }

        await using var lecture = _services.CreateAsyncScope();
        var categories = lecture.ServiceProvider.GetRequiredService<ICategoryRepository>();

        var apresFeuille = (await categories.GetByIdAsync(new CategoryId(feuille)))!;
        apresFeuille.Path.Should().Be("/gros-electromenager/froid/refrigerateurs",
            "sans répercussion, la feuille reste sur un chemin dont plus aucun ancêtre n'existe — "
            + "et toutes ses contraintes héritées disparaissent en silence.");

        var ancetres = await categories.ListAncestorsAsync(apresFeuille.Path);
        ancetres.Select(a => a.Name).Should().Equal(
            new[] { "Gros electromenager", "Froid" },
            "la branche doit se retrouver entière après le renommage.");
    }

    [Fact]
    public async Task Ajouter_un_attribut_sur_un_ancetre_est_visible_immediatement_sur_la_feuille()
    {
        var racine = await CreerCategorieAsync("Jardin", null, null);
        var feuille = await CreerCategorieAsync("Tondeuses", racine, null);

        await using (var scope = _services.CreateAsyncScope())
        {
            var api = scope.ServiceProvider.GetRequiredService<
                Marketplace.Modules.Catalog.Contracts.ICatalogModuleApi>();

            // On PEUPLE le cache de la feuille avant la modification du parent : c'est
            // exactement la situation où une éviction incomplète sert un schéma périmé.
            var avant = (await api.GetCategoryAsync(feuille))!;
            avant.AttributeSchema.Should().BeEmpty();
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<MediatR.ISender>();
            var resultat = await sender.Send(
                new Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory
                    .UpdateCategoryCommand(racine, "Jardin", null,
                        "{ \"attributes\": [ { \"key\": \"largeur_coupe\", \"label\": \"Largeur de coupe\", \"type\": \"number\" } ] }"));

            resultat.IsSuccess.Should().BeTrue();
        }

        await using var apres = _services.CreateAsyncScope();
        var apiApres = apres.ServiceProvider.GetRequiredService<
            Marketplace.Modules.Catalog.Contracts.ICatalogModuleApi>();

        var feuilleApres = (await apiApres.GetCategoryAsync(feuille))!;
        feuilleApres.InheritedAttributes!.Select(a => a.Key).Should().Contain("largeur_coupe",
            "le cache d'une feuille porte le schéma de ses ancêtres : ne pas l'évincer ferait "
            + "remplir au vendeur un formulaire auquel manque le champ que le serveur exigera.");
    }

    [Fact]
    public async Task Le_resume_separe_les_attributs_herites_des_propres()
    {
        await using var scope = _services.CreateAsyncScope();
        var api = scope.ServiceProvider.GetRequiredService<Marketplace.Modules.Catalog.Contracts.ICatalogModuleApi>();

        var feuille = (await api.GetCategoryAsync(_portables))!;

        feuille.OwnAttributes!.Select(a => a.Key).Should().Equal("pouces");
        feuille.InheritedAttributes!.Select(a => a.Key).Should().Equal("marque", "garantie", "processeur");
        feuille.AttributeSchema!.Select(a => a.Key).Should().Equal("marque", "garantie", "processeur", "pouces");
        feuille.IsLeaf.Should().BeTrue();

        feuille.InheritedAttributes!.Single(a => a.Key == "processeur")
            .InheritedFrom.Should().Be("Ordinateurs");

        var intermediaire = (await api.GetCategoryAsync(_ordinateurs))!;
        intermediaire.IsLeaf.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────

    private Task<Guid> CreerCategorieAsync(string nom, Guid? parentId, string? schema)
        => _fixture.CreerCategorieAsync(nom, parentId, schema);

    private Task CreerProduitAsync(Guid categoryId, Dictionary<string, string> attributs)
        => _fixture.CreerProduitAsync(categoryId, attributs);
}
