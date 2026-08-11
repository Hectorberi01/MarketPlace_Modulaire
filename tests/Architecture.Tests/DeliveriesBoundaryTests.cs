using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES FRONTIÈRES DE HBA DELIVERY.
///
/// POURQUOI CE FICHIER DEVAIT EXISTER SÉPARÉMENT
///
/// Les tests d'architecture de ce dépôt désignent les modules UN PAR UN, par leur
/// type racine. Aucun ne balaie l'ensemble du dossier <c>src/Modules</c>. Un
/// module nouveau n'est donc couvert par rien tant que quelqu'un ne l'a pas
/// nommé — et jusqu'ici, le plus gros module de la v2 n'était nommé nulle part.
///
/// CE QUI SE JOUE ICI N'EST PAS DE L'HYGIÈNE, C'EST LE PRODUIT
///
/// HBA Delivery est censé être vendu à des marchands tiers. Sa valeur tient
/// entièrement à une propriété : le moteur ignore ce qu'il transporte. Il connaît
/// une référence opaque, deux points, un colis. Le jour où une règle de dispatch
/// demande « est-ce un repas ? » ou « quel vendeur ? », le produit cesse d'être
/// vendable à l'extérieur — et cela se produira par une seule ligne, ajoutée de
/// bonne foi, un jour où quelqu'un aura besoin d'un raccourci.
///
/// Ces tests sont donc plus STRICTS que ceux des autres modules : Deliveries n'a
/// le droit de dépendre d'aucun autre module, pas même de leurs Contracts.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class DeliveriesBoundaryTests
{
    private static readonly Assembly Domain =
        typeof(HBA.Deliveries.Domain.Deliveries.Delivery).Assembly;

    private static readonly Assembly Application =
        typeof(HBA.Deliveries.Application.Deliveries.Commands.CreateDeliveryCommand).Assembly;

    private static readonly Assembly Contracts =
        typeof(HBA.Deliveries.Contracts.IDeliveryModuleApi).Assembly;

    private static readonly Assembly Infrastructure =
        typeof(HBA.Deliveries.Infrastructure.DeliveriesModuleInstaller).Assembly;

    /// <summary>Les préfixes de TOUS les autres modules du monolithe.</summary>
    private const string OtherModulesPrefix = "Marketplace.Modules";

    [Fact]
    public void Le_domaine_ignore_la_persistance_et_le_transport()
    {
        Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "StackExchange.Redis",
                "Microsoft.AspNetCore",
                "Npgsql",
                "MediatR")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void L_application_ignore_l_infrastructure()
    {
        Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "StackExchange.Redis",
                "Npgsql",
                "HBA.Deliveries.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Deliveries_ne_connait_AUCUN_autre_module_pas_meme_leurs_Contracts()
    {
        // ⚠️ RÈGLE DÉLIBÉRÉMENT PLUS DURE QUE POUR LES AUTRES MODULES.
        //
        // Ailleurs, dépendre des Contracts d'un voisin est admis : c'est ainsi que
        // Ordering parle à Payments. Ici, non. Un moteur logistique qui référence
        // « Marketplace.Modules.Ordering.Contracts », même pour une seule ligne,
        // ne peut plus être extrait ni vendu séparément — et la dépendance se
        // paiera au moment précis où l'on voudra en faire un service autonome.
        //
        // La communication se fait dans l'AUTRE SENS : les modules marchands
        // appellent IDeliveryModuleApi. Deliveries, lui, ne rappelle personne.
        AssertNoDependencyOnOtherModules(Domain, nameof(Domain));
        AssertNoDependencyOnOtherModules(Application, nameof(Application));
        AssertNoDependencyOnOtherModules(Contracts, nameof(Contracts));
        AssertNoDependencyOnOtherModules(Infrastructure, nameof(Infrastructure));
    }

    [Fact]
    public void Les_contrats_publics_ne_dependent_de_rien_d_interne()
    {
        // Les Contracts sont ce que les autres modules référencent. S'ils tiraient
        // le Domain derrière eux, chaque module du monolithe se retrouverait
        // couplé aux entrailles de Deliveries sans l'avoir demandé.
        Types.InAssembly(Contracts).ShouldNot()
            .HaveDependencyOnAny(
                "HBA.Deliveries.Domain",
                "HBA.Deliveries.Application",
                "HBA.Deliveries.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Le_moteur_logistique_ignore_le_vocabulaire_marchand()
    {
        // ─────────────────────────────────────────────────────────────────────
        // LE PRINCIPE DIRECTEUR DU CAHIER, RENDU EXÉCUTABLE.
        //
        // Un test de dépendances ne l'attrape pas : on peut parfaitement écrire
        // une classe « MealDeliveryRule » sans référencer aucun autre module. Le
        // couplage commence dans le VOCABULAIRE, avant d'apparaître dans les
        // références.
        //
        // La liste vise des concepts COMMERCIAUX, pas logistiques. « Package »,
        // « Stop », « Driver », « Zone » sont légitimes ; « Product », « Seller »,
        // « Restaurant » ne le sont pas.
        // ─────────────────────────────────────────────────────────────────────
        string[] motsInterdits =
        [
            "Product", "Catalog", "Cart", "Basket", "Sku", "Variant",
            "Seller", "Merchant", "Vendor", "Shop", "Store",
            "Restaurant", "Meal", "Food", "Dish", "Menu",
            "Invoice", "Payment", "Checkout", "Coupon", "Promotion"
        ];

        var fautifs = Domain.GetTypes()
            .Where(t => t.IsPublic || t.IsNestedPublic)
            .Select(t => t.Name)
            .Where(name => motsInterdits.Any(mot => name.Contains(mot, StringComparison.Ordinal)))
            .Distinct()
            .ToList();

        fautifs.Should().BeEmpty(
            "le domaine de HBA Delivery doit rester ignorant de ce qu'il transporte — "
            + "c'est ce qui permet de le vendre à des marchands tiers");
    }

    [Fact]
    public void Aucun_autre_module_ne_traverse_les_contrats_de_Deliveries()
    {
        // Le sens inverse de la règle précédente. Les modules marchands ont le
        // droit d'appeler Deliveries — mais SEULEMENT par ses Contracts. Un
        // « using HBA.Deliveries.Domain » dans Ordering donnerait accès à
        // l'agrégat Delivery et à ses transitions, et personne ne le remarquerait
        // avant le jour de l'extraction.
        string[] interditsAuxAutres =
        [
            "HBA.Deliveries.Domain",
            "HBA.Deliveries.Application",
            "HBA.Deliveries.Infrastructure"
        ];

        Assembly[] modulesMarchands =
        [
            typeof(Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder.PlaceOrderCommand).Assembly,
            typeof(Marketplace.Modules.Shipping.Application.Shipments.Commands.MarkShipmentShippedCommand).Assembly,
            typeof(Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment.InitiatePaymentCommand).Assembly,
            typeof(Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller.RegisterSellerCommand).Assembly
        ];

        foreach (var module in modulesMarchands)
        {
            Types.InAssembly(module).ShouldNot()
                .HaveDependencyOnAny(interditsAuxAutres)
                .GetResult().IsSuccessful.Should().BeTrue(
                    $"« {module.GetName().Name} » ne doit atteindre Deliveries que par ses Contracts");
        }
    }

    private static void AssertNoDependencyOnOtherModules(Assembly assembly, string couche)
    {
        var result = Types.InAssembly(assembly).ShouldNot()
            .HaveDependencyOn(OtherModulesPrefix)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"la couche {couche} de HBA Delivery ne doit référencer aucun autre module ; "
            + $"types fautifs : {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
