using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests;

/// <summary>
/// Frontières du module Delivery — et une règle de plus que les autres.
///
/// Ce module est mince PAR CONCEPTION : il porte l'identité du coursier côté
/// marketplace, pas son activité. Le dispatch, la géolocalisation et la disponibilité
/// vivent dans le service autonome (docs/CDC_Service_Livraison_Coursier.md).
///
/// Rien dans le compilateur n'empêche d'y glisser peu à peu une position, une zone, un
/// taux d'acceptation — et le jour où cela arriverait, il y aurait deux vérités sur la
/// même personne sans que personne ne l'ait décidé. Le dernier test ci-dessous rend ce
/// glissement visible.
/// </summary>
public class DeliveryBoundaryTests
{
    private static readonly Assembly Domain =
        typeof(Marketplace.Modules.Delivery.Domain.Couriers.Courier).Assembly;

    private static readonly Assembly Application =
        typeof(Marketplace.Modules.Delivery.Application.Couriers.RegisterCourierCommand).Assembly;

    private static readonly Assembly Contracts =
        typeof(Marketplace.Modules.Delivery.Contracts.IDeliveryModuleApi).Assembly;

    [Fact]
    public void Domain_ne_doit_pas_dependre_de_EFCore()
        => Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult().IsSuccessful.Should().BeTrue();

    [Fact]
    public void Application_ne_doit_dependre_ni_de_EFCore_ni_de_son_Infrastructure()
        => Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Marketplace.Modules.Delivery.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

    [Fact]
    public void Contracts_ne_doit_exposer_aucun_interne()
        => Types.InAssembly(Contracts).ShouldNot()
            .HaveDependencyOnAny(
                "Marketplace.Modules.Delivery.Domain",
                "Marketplace.Modules.Delivery.Application",
                "Marketplace.Modules.Delivery.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

    [Fact]
    public void Delivery_ne_depend_d_AUCUN_autre_module()
    {
        // Y COMPRIS SETTLEMENT, ET C'EST LE POINT. La dépendance va dans un seul sens :
        // Settlement lit les coordonnées du coursier pour le payer. Si Delivery se
        // mettait à appeler Settlement, les deux modules deviendraient un seul, et
        // l'ordre des appels au moment d'un versement cesserait d'être évident.
        var forbidden = new[]
        {
            "Marketplace.Modules.Settlement",
            "Marketplace.Modules.Ordering",
            "Marketplace.Modules.Shipping",
            "Marketplace.Modules.Identity",
            "Marketplace.Modules.Sellers",
            "Marketplace.Modules.Payments"
        };

        Types.InAssembly(Domain).ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult().IsSuccessful.Should().BeTrue();

        Types.InAssembly(Application).ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Le_module_reste_MINCE_le_dispatch_n_entre_pas_ici()
    {
        // Le service autonome porte la course, le dispatch, la position et la zone.
        // Voir un de ces noms apparaître ici signifie qu'on a commencé à recopier son
        // domaine — la duplication commence toujours par une seule classe utile.
        var intrus = new[] { "DeliveryJob", "DispatchOffer", "PickupPoint", "CourierPosition", "CourierZone" };

        var noms = Types.InAssembly(Domain).GetTypes().Select(t => t.Name).ToList();

        noms.Should().NotIntersectWith(
            intrus,
            "le dispatch et la géolocalisation appartiennent au service coursier autonome, " +
            "pas au monolithe — voir docs/CDC_Service_Livraison_Coursier.md");
    }
}
