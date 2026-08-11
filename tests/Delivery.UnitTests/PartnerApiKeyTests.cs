using FluentAssertions;
using HBA.Deliveries.Domain.Partners;
using Xunit;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CLÉS D'API — LA PORTE DE L'API PUBLIQUE.
///
/// C'est le seul endroit du module où une erreur ne coûte pas une course mal
/// tarifée mais l'accès aux données d'un tiers. Ces tests portent donc autant sur
/// ce que le code FAIT que sur ce qu'il NE DOIT PAS faire : ne pas conserver le
/// secret, ne pas accepter une clé révoquée, ne pas laisser deviner si un préfixe
/// existe.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class PartnerApiKeyTests
{
    private static Partner ActivePartner()
    {
        var partner = Partner.Register("Boutique Tiers", "contact@boutique.bj", dailyQuota: 100).Value;
        partner.Activate();
        return partner;
    }

    [Fact]
    public void Une_cle_emise_est_immediatement_utilisable()
    {
        var partner = ActivePartner();

        var issued = partner.IssueApiKey("live", "intégration principale").Value;

        partner.FindActiveKey(issued.Prefix).Should().NotBeNull();
        partner.FindActiveKey(issued.Prefix)!.Matches(issued.Key).Should().BeTrue();
    }

    [Fact]
    public void Le_secret_n_est_conserve_nulle_part()
    {
        // Le cœur de la promesse : un partenaire qui perd sa clé en obtient une
        // nouvelle, il ne la « récupère » pas. Si le secret réapparaissait dans
        // l'entité, une fuite de la base deviendrait une fuite de toutes les clés.
        var partner = ActivePartner();
        var issued = partner.IssueApiKey("live").Value;
        var stored = partner.FindActiveKey(issued.Prefix)!;

        stored.Hash.Should().NotContain(issued.Key);
        issued.Key.Should().NotContain(stored.Hash);

        // Le préfixe, lui, est public par construction — c'est ce qui permet de
        // journaliser « quelle clé » sans journaliser la clé.
        issued.Key.Should().Contain(issued.Prefix);
    }

    [Fact]
    public void Deux_cles_emises_ne_se_ressemblent_pas()
    {
        var partner = ActivePartner();

        var a = partner.IssueApiKey("live").Value;
        var b = partner.IssueApiKey("live").Value;

        a.Key.Should().NotBe(b.Key);
        a.Prefix.Should().NotBe(b.Prefix);
    }

    [Fact]
    public void Une_cle_revoquee_ne_vaut_plus_rien()
    {
        var partner = ActivePartner();
        var issued = partner.IssueApiKey("live").Value;
        var key = partner.FindActiveKey(issued.Prefix)!;

        partner.RevokeApiKey(key.Id).IsSuccess.Should().BeTrue();

        key.IsActive.Should().BeFalse();
        key.Matches(issued.Key).Should().BeFalse();

        // Et elle disparaît de la recherche : le filtre d'authentification
        // n'aura même pas de quoi comparer.
        partner.FindActiveKey(issued.Prefix).Should().BeNull();
    }

    [Fact]
    public void La_revocation_conserve_l_horodatage_initial()
    {
        // Révoquer deux fois ne doit pas repousser la date : c'est elle qui dit
        // depuis quand la porte est fermée, et c'est la première question posée
        // lors d'un incident.
        var partner = ActivePartner();
        var issued = partner.IssueApiKey("live").Value;
        var key = partner.ApiKeys.Single(k => k.Prefix == issued.Prefix);

        key.Revoke();
        var premier = key.RevokedAtUtc;
        key.Revoke();

        key.RevokedAtUtc.Should().Be(premier);
    }

    [Fact]
    public void Une_cle_d_un_autre_partenaire_ne_correspond_pas()
    {
        var a = ActivePartner();
        var b = ActivePartner();

        var cleDeA = a.IssueApiKey("live").Value;
        var cleDeB = b.IssueApiKey("live").Value;

        b.FindActiveKey(cleDeA.Prefix).Should().BeNull();
        a.FindActiveKey(cleDeA.Prefix)!.Matches(cleDeB.Key).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hba_live")]                         // trop de parties manquantes
    [InlineData("hba_live_court_secret")]            // préfixe de mauvaise longueur
    [InlineData("autre_live_123456789012_secret")]   // mauvais domaine
    [InlineData("hba_live_123456789012_a_b")]        // une partie de trop
    public void Une_cle_mal_formee_est_rejetee_sans_toucher_la_base(string? candidate)
    {
        // C'est ce qui empêche un flot de requêtes bidons de se traduire en
        // requêtes PostgreSQL : la forme est vérifiée avant toute lecture.
        PartnerApiKey.ExtractPrefix(candidate).Should().BeNull();
    }

    [Fact]
    public void Le_prefixe_extrait_est_celui_qui_a_ete_emis()
    {
        var partner = ActivePartner();
        var issued = partner.IssueApiKey("test").Value;

        PartnerApiKey.ExtractPrefix(issued.Key).Should().Be(issued.Prefix);
    }

    [Fact]
    public void Le_nombre_de_cles_actives_est_plafonne()
    {
        // Au-delà de cinq, ce ne sont plus des clés de rotation mais un oubli de
        // révocation. Chaque clé active est une porte.
        var partner = ActivePartner();

        for (var i = 0; i < 5; i++)
        {
            partner.IssueApiKey("live", $"clé {i}").IsSuccess.Should().BeTrue();
        }

        var refus = partner.IssueApiKey("live", "la sixième");
        refus.IsFailure.Should().BeTrue();
        refus.Error.Code.Should().Be("partner.too_many_keys");

        // Mais révoquer libère une place : la rotation reste possible sans coupure.
        partner.RevokeApiKey(partner.ApiKeys.First().Id);
        partner.IssueApiKey("live", "la remplaçante").IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Un_partenaire_suspendu_n_emet_plus_de_cle()
    {
        var partner = ActivePartner();
        partner.Suspend();

        partner.IssueApiKey("live").IsFailure.Should().BeTrue();
        partner.CanCreateDeliveries.Should().BeFalse();
    }

    [Fact]
    public void Un_partenaire_nait_inactif()
    {
        // Créer un partenaire ne l'autorise pas à livrer : il faut un geste
        // explicite. L'inverse ferait d'une erreur de saisie un accès ouvert.
        var partner = Partner.Register("Nouveau", "contact@nouveau.bj").Value;

        partner.Status.Should().Be(PartnerStatus.Pending);
        partner.CanCreateDeliveries.Should().BeFalse();
    }

    [Fact]
    public void L_usage_n_est_horodate_qu_une_fois_par_heure()
    {
        // Horodater chaque appel transformerait la table des clés en journal
        // d'accès, avec une écriture par requête partenaire.
        var partner = ActivePartner();
        var issued = partner.IssueApiKey("live").Value;
        var key = partner.FindActiveKey(issued.Prefix)!;

        key.TouchIfStale().Should().BeTrue();   // première fois : on écrit
        key.TouchIfStale().Should().BeFalse();  // juste après : on n'écrit pas

        key.LastUsedAtUtc.Should().NotBeNull();
    }

    // ── Webhooks ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://boutique.bj/hooks")]     // en clair
    [InlineData("ftp://boutique.bj/hooks")]
    [InlineData("pas-une-url")]
    public void Un_rappel_non_HTTPS_est_refuse(string url)
    {
        // Un webhook porte l'état d'une commande et une référence client. En
        // clair, il est lisible ET modifiable par tout intermédiaire réseau.
        var result = ActivePartner().ConfigureWebhook(url, new string('s', 32));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("partner.webhook_https_required");
    }

    [Fact]
    public void Un_secret_de_signature_trop_court_est_refuse()
    {
        var result = ActivePartner().ConfigureWebhook("https://boutique.bj/hooks", "trop-court");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("partner.webhook_secret_weak");
    }

    [Fact]
    public void Effacer_l_URL_efface_aussi_le_secret()
    {
        // Sinon un secret orphelin survit en base, sans usage et sans expiration —
        // exactement le genre de donnée qu'une fuite rend embarrassante.
        var partner = ActivePartner();
        partner.ConfigureWebhook("https://boutique.bj/hooks", new string('s', 32));

        partner.ConfigureWebhook(null, null).IsSuccess.Should().BeTrue();

        partner.WebhookUrl.Should().BeNull();
        partner.WebhookSecret.Should().BeNull();
    }
}
