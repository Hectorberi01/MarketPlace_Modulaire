using FluentAssertions;
using Marketplace.Modules.Shipping.Domain.Shipments;
using Xunit;

namespace Shipping.UnitTests;

public class ShipmentAggregateTests
{
    private static Shipment NewShipment()
        => Shipment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new[] { new ShipmentLineDraft("SKU-1", 2) }).Value;

    [Fact]
    public void Create_starts_pending_with_items()
    {
        var shipment = NewShipment();

        shipment.Status.Should().Be(ShipmentStatus.Pending);
        shipment.Items.Should().HaveCount(1);
        shipment.Items.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public void Create_without_lines_fails()
    {
        var result = Shipment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Array.Empty<ShipmentLineDraft>());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shipping.no_lines");
    }

    /// <summary>Repli transporteur tiers : préparée, confiée, livrée.</summary>
    [Fact]
    public void Chemin_transporteur_preparer_expedier_livrer()
    {
        var shipment = NewShipment();

        shipment.MarkPreparing().IsSuccess.Should().BeTrue();
        shipment.MarkShipped("DHL", "TRACK-123").IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Shipped);
        shipment.TrackingNumber.Should().Be("TRACK-123");
        shipment.DeliveryMode.Should().Be(ShipmentDeliveryMode.Carrier);

        shipment.MarkDelivered().IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
    }

    /// <summary>
    /// Chemin NOMINAL : le coursier de la plateforme, qui ne passe jamais par « en
    /// transit ». Il ramasse et il dépose dans la même course.
    /// </summary>
    [Fact]
    public void Chemin_coursier_preparer_puis_livrer_sans_passer_par_en_transit()
    {
        var shipment = NewShipment();
        var course = Guid.NewGuid();

        shipment.MarkPreparing().IsSuccess.Should().BeTrue();

        shipment.MarkDeliveredByCourier(course).IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Delivered);
        shipment.DeliveryMode.Should().Be(ShipmentDeliveryMode.Courier);
        shipment.DeliveryJobId.Should().Be(course);
        shipment.Carrier.Should().BeNull("une moto en propre n'a pas de transporteur");
        shipment.TrackingNumber.Should().BeNull("ni de numéro de suivi");
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// LE TEST DE NON-RÉGRESSION DE LA RUPTURE PRINCIPALE. NE PAS ASSOUPLIR.
    ///
    /// Sans `MarkDeliveredByCourier`, une expédition portée par un coursier restait
    /// « en préparation » à jamais : `MarkDelivered` exige « en transit », et pour y
    /// arriver il fallait un transporteur et un numéro de suivi qui n'existent pas.
    /// La commande n'atteignait donc jamais « livrée », et la séquestre du vendeur
    /// n'était jamais libérée — sans la moindre erreur nulle part.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Une_expedition_en_preparation_ne_peut_PAS_etre_livree_par_le_chemin_transporteur()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();

        shipment.MarkDelivered().IsFailure.Should().BeTrue(
            "c'est ce refus qui bloquait la chaîne ; il reste juste, et c'est "
            + "MarkDeliveredByCourier qui doit être appelée à la place");
    }

    [Fact]
    public void Une_annonce_de_course_rejouee_est_un_succes_silencieux()
    {
        var shipment = NewShipment();
        var course = Guid.NewGuid();
        shipment.MarkPreparing();
        shipment.MarkDeliveredByCourier(course);
        shipment.ClearDomainEvents();

        // La file de sortie du service de dispatch réessaie : un rejeu doit passer sans
        // rien changer NI RIEN LEVER — sinon la commande serait annoncée livrée deux fois.
        shipment.MarkDeliveredByCourier(course).IsSuccess.Should().BeTrue();
        shipment.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// L'ÉCHEC NE FAIT PAS AVANCER L'EXPÉDITION, ET C'EST LE POINT. NE PAS « CORRIGER ».
    ///
    /// Le vendeur doit pouvoir repartir, et le seul état d'où `MarkShipped` accepte de
    /// confier la part à un transporteur tiers est « en préparation ». Faire basculer
    /// l'expédition ailleurs — livrée, annulée, en attente — supprimerait ce repli sur
    /// une commande DÉJÀ PAYÉE.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Une_course_en_echec_laisse_l_expedition_en_preparation_et_le_repli_transporteur_ouvert()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();

        var resultat = shipment.MarkCourierDeliveryFailed(
            Guid.NewGuid(), "Acheteur injoignable.", DateTime.UtcNow);

        resultat.IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Preparing);
        shipment.DeliveryMode.Should().BeNull("personne n'a emporté ce colis");
        shipment.CourierFailureReason.Should().Be("Acheteur injoignable.");

        // LE REPLI RESTE PRATICABLE : c'est toute la raison de ne pas bouger le statut.
        shipment.MarkShipped("DHL", "TRACK-9").IsSuccess.Should().BeTrue();
        shipment.Status.Should().Be(ShipmentStatus.Shipped);
    }

    [Fact]
    public void Une_annonce_d_echec_rejouee_est_un_succes_silencieux()
    {
        var shipment = NewShipment();
        var course = Guid.NewGuid();
        shipment.MarkPreparing();
        shipment.MarkCourierDeliveryFailed(course, "Adresse introuvable.", DateTime.UtcNow);
        shipment.ClearDomainEvents();

        // Le drain du service coursier réessaie jusqu'à dix fois : sans ce succès
        // silencieux, le vendeur recevrait dix fois la même mauvaise nouvelle.
        shipment.MarkCourierDeliveryFailed(course, "Adresse introuvable.", DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
        shipment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Un_SECOND_echec_par_une_AUTRE_course_est_bien_annonce()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();
        shipment.MarkCourierDeliveryFailed(Guid.NewGuid(), "Premier abandon.", DateTime.UtcNow);
        shipment.ClearDomainEvents();

        // Le repli a été retenté et a échoué à son tour : c'est un fait NOUVEAU. Le taire
        // laisserait le vendeur croire sa seconde tentative partie.
        var seconde = Guid.NewGuid();
        shipment.MarkCourierDeliveryFailed(seconde, "Second abandon.", DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();

        shipment.FailedDeliveryJobId.Should().Be(seconde);
        shipment.DomainEvents.Should().ContainSingle();
    }

    [Fact]
    public void Un_motif_interminable_est_tronque_et_ne_fait_pas_echouer_l_annonce()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();

        // Le service de dispatch normalise le motif mais NE LE BORNE PAS. Refuser ici
        // ferait rejouer puis abandonner l'annonce : le vendeur ne saurait rien, ce qui
        // est exactement le défaut qu'on ferme.
        shipment.MarkCourierDeliveryFailed(Guid.NewGuid(), new string('x', 5000), DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();

        shipment.CourierFailureReason!.Length.Should().Be(300);
    }

    [Fact]
    public void Un_motif_vide_devient_un_libelle_exploitable()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();

        shipment.MarkCourierDeliveryFailed(Guid.NewGuid(), "   ", DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();

        shipment.CourierFailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Une_expedition_deja_confiee_a_un_transporteur_refuse_l_echec_de_course()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();
        shipment.MarkShipped("DHL", "TRACK-1");

        // Cette part n'appartient plus au coursier : elle est en transit chez un tiers.
        shipment.MarkCourierDeliveryFailed(Guid.NewGuid(), "Abandon.", DateTime.UtcNow)
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Une_expedition_deja_livree_par_une_AUTRE_course_est_refusee()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();
        shipment.MarkDeliveredByCourier(Guid.NewGuid());

        // L'idempotence porte sur la COURSE, pas sur l'état : deux courses distinctes
        // qui prétendent avoir livré la même part est une incohérence entre les deux
        // systèmes, et elle doit se voir.
        shipment.MarkDeliveredByCourier(Guid.NewGuid()).IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// LE RACCOURCI QUI COUPAIT LA LIVRAISON. NE PAS ROUVRIR.
    ///
    /// `MarkShipped` acceptait `Pending`. Un vendeur qui expédiait sans préparer ne
    /// levait jamais `ShipmentPreparingDomainEvent`, donc aucune course n'était créée,
    /// donc la commande était invisible du service de dispatch — en silence.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Une_expedition_non_preparee_ne_peut_pas_etre_expediee()
    {
        var shipment = NewShipment();

        var result = shipment.MarkShipped("DHL", "TRACK-123");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shipping.invalid_transition");
        shipment.Status.Should().Be(ShipmentStatus.Pending);
    }

    [Fact]
    public void Ship_requires_carrier_and_tracking()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();

        shipment.MarkShipped("", "").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Delivered_shipment_cannot_be_cancelled()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();
        shipment.MarkShipped("DHL", "T1");
        shipment.MarkDelivered();

        shipment.Cancel().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void MarkPreparing_emet_l_evenement_qui_declenche_la_livraison_par_coursier()
    {
        var shipment = NewShipment();

        shipment.MarkPreparing().IsSuccess.Should().BeTrue();

        // ═══════════════════════════════════════════════════════════════════════
        // « En préparation » était le SEUL état du cycle à n'émettre aucun événement.
        //
        // C'est pourtant lui qui déclenche la course : elle n'est créée que lorsque
        // TOUTES les parts vendeur d'une commande sont prêtes. Sans cet événement, ce
        // moment n'existait nulle part, et aucun coursier n'aurait jamais été appelé.
        // ═══════════════════════════════════════════════════════════════════════
        shipment.DomainEvents.OfType<Marketplace.Modules.Shipping.Domain.Shipments.Events.ShipmentPreparingDomainEvent>()
            .Should().ContainSingle()
            .Which.OrderId.Should().Be(shipment.OrderId);
    }

    [Fact]
    public void Une_expedition_deja_en_preparation_n_emet_pas_une_seconde_fois()
    {
        var shipment = NewShipment();
        shipment.MarkPreparing();
        shipment.ClearDomainEvents();

        // Le garde d'état du domaine sert aussi de garde d'ÉMISSION : sans lui, deux
        // appels annonceraient deux fois « commande prête », et le service coursier
        // créerait deux courses pour la même commande.
        shipment.MarkPreparing().IsFailure.Should().BeTrue();
        shipment.DomainEvents.Should().BeEmpty();
    }
}
