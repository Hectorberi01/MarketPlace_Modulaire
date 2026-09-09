using FluentAssertions;
using Marketplace.Modules.Shipping.Application.Zones;
using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Zones;
using Xunit;

namespace Shipping.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LE PRIX DE LA LIVRAISON DÉPEND DE LA DESTINATION — ET NE DOIT JAMAIS DISPARAÎTRE.
///
/// Deux garanties, également importantes et opposées.
///
/// La PREMIÈRE : Cotonou et Karimama ne coûtent plus le même prix. C'était le défaut
/// d'origine — une grille codée en dur dans le BFF, aveugle à sept cents kilomètres.
///
/// La SECONDE : quoi qu'il arrive à la grille — vide, incomplète, zone désactivée,
/// tarif effacé — l'acheteur reçoit un prix. Une base sans zones ne doit pas se
/// traduire par « aucune livraison possible » au moment de payer. C'est la moitié
/// des tests ci-dessous, et c'est celle qu'on oublie d'écrire.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ZonesDeLivraisonTests
{
    // ── Le résolveur : quelle zone pour quelle commune ? ─────────────────────────

    [Fact]
    public void La_zone_qui_rattache_explicitement_la_commune_gagne_sur_le_repli()
    {
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);
        var national = ZoneDeRepli();

        var resolue = ShippingZoneResolver.Resolve([cotonou, national], "cotonou");

        resolue.Should().BeSameAs(cotonou);
    }

    [Fact]
    public void Une_commune_non_rattachee_tombe_sur_le_repli()
    {
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);
        var national = ZoneDeRepli();

        var resolue = ShippingZoneResolver.Resolve([cotonou, national], "karimama");

        resolue.Should().BeSameAs(national);
    }

    [Fact]
    public void Deux_zones_couvrant_la_meme_commune_sont_departagees_par_la_plus_specifique()
    {
        var sud = Zone("sud", "Sud", communes: ["cotonou", "porto-novo", "ouidah"]);
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);

        // Rien en base n'empêche deux administrateurs de cocher Cotonou dans deux
        // zones. Laisser l'ordre de lecture décider ferait varier le tarif d'une
        // requête à l'autre sans que personne ne comprenne pourquoi.
        ShippingZoneResolver.Resolve([sud, cotonou], "cotonou").Should().BeSameAs(cotonou);
        ShippingZoneResolver.Resolve([cotonou, sud], "cotonou").Should().BeSameAs(cotonou);
    }

    [Fact]
    public void Une_zone_desactivee_ne_s_applique_plus()
    {
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);
        cotonou.Deactivate();
        var national = ZoneDeRepli();

        ShippingZoneResolver.Resolve([cotonou, national], "cotonou").Should().BeSameAs(national);
    }

    [Fact]
    public void Sans_repli_ni_rattachement_le_resolveur_ne_renvoie_rien()
    {
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);

        // Le résolveur DIT qu'il ne sait pas, il n'invente pas un tarif. C'est à
        // l'appelant de décider quoi faire — et c'est ce que teste la suite.
        ShippingZoneResolver.Resolve([cotonou], "karimama").Should().BeNull();
    }

    [Fact]
    public void Les_communes_non_couvertes_sont_listables()
    {
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);
        var national = ZoneDeRepli();

        var manquantes = ShippingZoneResolver.UncoveredCommunes(
            [cotonou, national], ["cotonou", "karimama", "natitingou"]);

        // Le repli ne « couvre » rien explicitement : ses communes ne doivent pas
        // masquer le fait que personne ne les a rattachées.
        manquantes.Should().BeEquivalentTo(new[] { "karimama", "natitingou" });
    }

    // ── La requête : un prix, toujours ──────────────────────────────────────────

    [Fact]
    public async Task Une_base_sans_aucune_zone_renvoie_les_forfaits_de_secours()
    {
        var options = await ResoudreAsync([], "cotonou");

        options.Should().NotBeEmpty();
        options.Should().OnlyContain(o => o.ZoneCode == null);
        options.Select(o => o.ServiceCode).Should().Contain("standard");
    }

    [Fact]
    public async Task Une_zone_sans_aucun_tarif_retombe_sur_le_secours()
    {
        // Zone créée en administration, tarifs jamais saisis. Renvoyer une liste vide
        // se lirait « aucune livraison possible » à l'écran, pour une zone qui existe.
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);

        var options = await ResoudreAsync([cotonou], "cotonou");

        options.Should().NotBeEmpty();
        options.Should().OnlyContain(o => o.ZoneCode == null);
    }

    [Fact]
    public async Task Deux_communes_eloignees_ne_coutent_plus_le_meme_prix()
    {
        var cotonou = Zone("cotonou", "Cotonou", communes: ["cotonou"]);
        cotonou.SetRate("standard", "Standard", "24h", 1_000m, "XOF");

        var national = ZoneDeRepli();
        national.SetRate("standard", "Standard", "72h", 4_500m, "XOF");

        var proche = await ResoudreAsync([cotonou, national], "cotonou");
        var lointain = await ResoudreAsync([cotonou, national], "karimama");

        // Le défaut d'origine, en une assertion.
        proche[0].Amount.Should().Be(1_000m);
        lointain[0].Amount.Should().Be(4_500m);
        proche[0].Amount.Should().NotBe(lointain[0].Amount);
    }

    [Fact]
    public async Task Une_commune_ecrite_autrement_designe_la_meme_zone()
    {
        var zone = Zone("grand-popo", "Grand-Popo", communes: ["grand-popo"]);
        zone.SetRate("standard", "Standard", "48h", 2_000m, "XOF");
        var national = ZoneDeRepli();
        national.SetRate("standard", "Standard", "72h", 4_500m, "XOF");

        // « Grand-Popo » saisi avec majuscules doit donner le tarif de sa zone, pas
        // le tarif national : sinon l'écart ne se verrait que sur la facture.
        var options = await ResoudreAsync([zone, national], "Grand-Popo");

        options[0].Amount.Should().Be(2_000m);
        options[0].ZoneCode.Should().Be("grand-popo");
    }

    [Fact]
    public async Task Les_options_sont_triees_du_moins_cher_au_plus_cher()
    {
        var zone = ZoneDeRepli();
        zone.SetRate("express", "Express", "3h", 3_000m, "XOF");
        zone.SetRate("standard", "Standard", "48h", 1_500m, "XOF");

        var options = await ResoudreAsync([zone], "cotonou");

        // Le BFF retient la PREMIÈRE option quand le code de service est inconnu :
        // ce tri est ce qui garantit qu'une faute de frappe côté client ne fait pas
        // payer l'option la plus chère.
        options.Select(o => o.Amount).Should().BeInAscendingOrder();
        options[0].ServiceCode.Should().Be("standard");
    }

    [Fact]
    public async Task Une_commune_absente_donne_le_repli_et_non_une_erreur()
    {
        var national = ZoneDeRepli();
        national.SetRate("standard", "Standard", "72h", 4_500m, "XOF");

        // L'acheteur consulte les tarifs avant d'avoir choisi son adresse.
        var options = await ResoudreAsync([national], null);

        options.Should().ContainSingle();
        options[0].Amount.Should().Be(4_500m);
    }

    // ── Montage ─────────────────────────────────────────────────────────────────

    private static ShippingZone Zone(string code, string nom, IEnumerable<string> communes)
    {
        var zone = ShippingZone.Create(code, nom, isFallback: false).Value;
        zone.SetCommunes(communes).IsSuccess.Should().BeTrue();
        return zone;
    }

    private static ShippingZone ZoneDeRepli()
        => ShippingZone.Create("national", "National", isFallback: true).Value;

    private static async Task<IReadOnlyList<ShippingRateOption>> ResoudreAsync(
        IReadOnlyList<ShippingZone> zones, string? commune)
    {
        var handler = new GetShippingRatesQueryHandler(new DepotFactice(zones));
        var result = await handler.Handle(new GetShippingRatesQuery(commune), default);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private sealed class DepotFactice : IShippingZoneRepository
    {
        private readonly IReadOnlyList<ShippingZone> _zones;

        public DepotFactice(IReadOnlyList<ShippingZone> zones) => _zones = zones;

        public Task AddAsync(ShippingZone zone, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Remove(ShippingZone zone)
        {
        }

        public Task<ShippingZone?> GetByIdAsync(ShippingZoneId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_zones.FirstOrDefault(z => z.Id == id));

        public Task<ShippingZone?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(_zones.FirstOrDefault(z => z.Code == code));

        public Task<IReadOnlyList<ShippingZone>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_zones);
    }
}
