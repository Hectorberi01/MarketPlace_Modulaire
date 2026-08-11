using FluentAssertions;
using HBA.Deliveries.Domain.Webhooks;
using Xunit;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES WEBHOOKS SORTANTS.
///
/// Deux sujets très différents cohabitent ici, et c'est voulu : la SIGNATURE, qui
/// est un contrat cryptographique qu'un tiers doit pouvoir reproduire, et la
/// FILE, qui est une machine à réessayer. La première ne doit jamais changer sans
/// version ; la seconde peut être réglée librement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class WebhookTests
{
    private const string Secret = "un-secret-de-webhook-de-trente-deux-caracteres";
    private const string Payload = """{"deliveryId":"abc","reference":"SHIP-1"}""";

    // ── La signature ────────────────────────────────────────────────────────

    /// <summary>
    /// Instant de référence des tests de signature, et son horodatage Unix.
    ///
    /// ⚠️ LES DEUX SONT ÉCRITS EN DUR, ET C'EST VOULU.
    ///
    /// Un horodatage recalculé par le test rendrait celui-ci tautologique : il
    /// vérifierait que le code est d'accord avec lui-même. Une valeur figée est ce
    /// qu'un partenaire peut copier dans SA suite de tests pour vérifier son
    /// implémentation — c'est un vecteur de test, pas un détail.
    ///
    /// La constante ci-dessous a d'ailleurs été FAUSSE de quatre jours à la
    /// première écriture, et c'est ce test qui l'a rattrapée.
    /// </summary>
    private static readonly DateTime Instant = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    private const long InstantUnix = 1786449600;

    [Fact]
    public void La_signature_a_la_forme_attendue_par_les_integrateurs()
    {
        var header = WebhookSignature.Build(Payload, Secret, Instant);

        header.Should().StartWith("t=");
        header.Should().Contain(",v1=");

        // Le préfixe de version existe pour pouvoir introduire un « v2 » sans
        // casser les intégrateurs qui lisent encore le premier.
        var parts = header.Split(',');
        parts.Should().HaveCount(2);
        parts[0].Should().Be($"t={InstantUnix}");
        parts[1].Should().MatchRegex("^v1=[0-9a-f]{64}$");
    }

    [Fact]
    public void Un_partenaire_peut_refaire_exactement_le_calcul()
    {
        // C'est la promesse : le partenaire recalcule et compare. Si ce test
        // devenait faux, toutes les intégrations existantes cesseraient de
        // vérifier — sans erreur de notre côté.
        var attendu = WebhookSignature.Compute(Payload, Secret, InstantUnix);
        var header = WebhookSignature.Build(Payload, Secret, Instant);

        header.Should().EndWith($"v1={attendu}");
    }

    [Fact]
    public void L_horodatage_fait_partie_de_ce_qui_est_signe()
    {
        // ⚠️ C'EST CE QUI REND LE REJEU INOPÉRANT.
        //
        // Signer le seul corps produirait une signature valable éternellement : un
        // appel intercepté une fois pourrait être rejoué des mois plus tard,
        // toujours parfaitement signé.
        var a = WebhookSignature.Compute(Payload, Secret, InstantUnix);
        var b = WebhookSignature.Compute(Payload, Secret, InstantUnix + 60);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Le_separateur_empeche_deux_appels_de_se_confondre()
    {
        // Sans le point entre horodatage et corps, « 1786795200 » + « 42… » et
        // « 178679520 » + « 042… » produiraient la même chaîne signée : deux
        // appels différents, indiscernables.
        var a = WebhookSignature.Compute("42abc", Secret, 1786795200);
        var b = WebhookSignature.Compute("042abc", Secret, 178679520);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Un_corps_modifie_invalide_la_signature()
    {
        var original = WebhookSignature.Compute(Payload, Secret, InstantUnix);
        var altere = WebhookSignature.Compute(Payload.Replace("SHIP-1", "SHIP-2"), Secret, InstantUnix);

        altere.Should().NotBe(original);
    }

    [Fact]
    public void Un_autre_secret_produit_une_autre_signature()
    {
        // Le fond du sujet : sans signature vérifiable, l'URL de rappel d'un
        // partenaire est un endpoint public. Un concurrent y annonce « livrée » et
        // le marchand clôt une vente qui n'a jamais eu lieu.
        var legitime = WebhookSignature.Compute(Payload, Secret, InstantUnix);
        var usurpe = WebhookSignature.Compute(Payload, "secret-de-quelqu-un-d-autre-32-car", InstantUnix);

        usurpe.Should().NotBe(legitime);
    }

    // ── La file ─────────────────────────────────────────────────────────────

    private static WebhookDelivery Enqueued()
        => WebhookDelivery.Enqueue(Guid.NewGuid(), Guid.NewGuid(), "delivery.completed", Payload).Value;

    [Fact]
    public void Un_envoi_est_du_immediatement_a_sa_mise_en_file()
    {
        var webhook = Enqueued();

        webhook.Status.Should().Be(WebhookStatus.Pending);
        webhook.Attempts.Should().Be(0);
        webhook.NextAttemptAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Le_recul_entre_deux_tentatives_croit()
    {
        // Un intervalle fixe court transformerait une panne de deux heures chez le
        // partenaire en plusieurs milliers de requêtes inutiles — c'est nous qui
        // l'empêcherions de redémarrer.
        var webhook = Enqueued();
        var attentes = new List<TimeSpan>();

        for (var i = 0; i < 3; i++)
        {
            var avant = DateTime.UtcNow;
            webhook.MarkFailed(503, "indisponible");
            attentes.Add(webhook.NextAttemptAtUtc - avant);
        }

        attentes[1].Should().BeGreaterThan(attentes[0]);
        attentes[2].Should().BeGreaterThan(attentes[1]);
    }

    [Fact]
    public void Apres_le_plafond_l_envoi_est_abandonne_et_conserve()
    {
        var webhook = Enqueued();

        for (var i = 0; i < WebhookDelivery.MaxAttempts; i++)
        {
            webhook.MarkFailed(500, "erreur serveur");
        }

        webhook.Status.Should().Be(WebhookStatus.Abandoned);
        webhook.Attempts.Should().Be(WebhookDelivery.MaxAttempts);

        // La ligne SURVIT avec sa dernière erreur : c'est le seul moyen de
        // répondre à « pourquoi n'ai-je jamais reçu la notification ? ».
        webhook.LastError.Should().NotBeNullOrWhiteSpace();
        webhook.LastStatusCode.Should().Be(500);
    }

    [Fact]
    public void Un_envoi_abandonne_ou_remis_ne_se_rejoue_pas()
    {
        var remis = Enqueued();
        remis.MarkDelivered(200);

        remis.MarkFailed(500, "trop tard").IsFailure.Should().BeTrue();
        remis.MarkDelivered(200).IsFailure.Should().BeTrue();
        remis.Attempts.Should().Be(1);
    }

    [Fact]
    public void L_identifiant_d_evenement_ne_change_jamais_entre_les_tentatives()
    {
        // C'est ce qui permet au partenaire de DÉDUPLIQUER. Un webhook réessayé
        // après un délai dépassé arrive deux fois alors que le premier avait bien
        // été traité — sans identifiant stable, le partenaire expédierait deux
        // fois la même commande.
        var eventId = Guid.NewGuid();
        var webhook = WebhookDelivery.Enqueue(Guid.NewGuid(), eventId, "delivery.completed", Payload).Value;

        webhook.MarkFailed(503, "indisponible");
        webhook.MarkFailed(503, "indisponible");

        webhook.EventId.Should().Be(eventId);
    }

    [Fact]
    public void Le_corps_est_fige_a_la_mise_en_file()
    {
        // Il n'y a AUCUN moyen de le modifier après coup, et c'est délibéré : le
        // corps signé et le corps envoyé doivent être le même octet. Un espace de
        // différence rendrait la signature invérifiable.
        var webhook = Enqueued();
        webhook.MarkFailed(500, "erreur");

        webhook.Payload.Should().Be(Payload);
        typeof(WebhookDelivery).GetProperty(nameof(WebhookDelivery.Payload))!
            .SetMethod!.IsPublic.Should().BeFalse();
    }

    [Fact]
    public void Un_envoi_sans_partenaire_est_refuse()
    {
        WebhookDelivery.Enqueue(Guid.Empty, Guid.NewGuid(), "delivery.completed", Payload)
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Le_message_d_erreur_est_borne()
    {
        // Un serveur qui renvoie une page HTML d'erreur de 40 Ko remplirait la
        // colonne — et la table — à chaque tentative de chaque envoi.
        var webhook = Enqueued();

        webhook.MarkFailed(500, new string('x', 5000));

        webhook.LastError!.Length.Should().BeLessThanOrEqualTo(500);
    }
}
