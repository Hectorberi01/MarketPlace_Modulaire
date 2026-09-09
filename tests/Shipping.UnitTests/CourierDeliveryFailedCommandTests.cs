using FluentAssertions;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using Marketplace.Modules.Shipping.Domain.Shipments;
using Marketplace.Modules.Shipping.Domain.Shipments.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Shipping.UnitTests;

/// <summary>
/// Le handler qui ferme le trou : une course échouée doit débloquer la commande côté
/// marketplace, sans payer personne et sans reprévenir dix fois le même vendeur.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI SE PASSAIT AVANT, ET QUE CES TESTS INTERDISENT DE REVOIR.
///
/// La route `POST /api/delivery/courses/failed` n'existait pas. Le drain du service
/// coursier recevait des 404, réessayait dix fois, abandonnait — et l'expédition restait
/// « en préparation » indéfiniment sur une commande DÉJÀ PAYÉE, sans que rien ne le
/// signale : pas d'erreur, pas de compteur, pas de statut anormal à lire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class CourierDeliveryFailedCommandTests
{
    private static Shipment PreparedShipment(Guid orderId)
    {
        var shipment = Shipment.Create(
            orderId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new[] { new ShipmentLineDraft("SKU-1", 1) }).Value;

        shipment.MarkPreparing();
        shipment.ClearDomainEvents();
        return shipment;
    }

    private static MarkOrderCourierDeliveryFailedCommandHandler Handler(
        FakeShipmentRepository repository, FakeUnitOfWork unitOfWork)
        => new(repository, unitOfWork,
            NullLogger<MarkOrderCourierDeliveryFailedCommandHandler>.Instance);

    private static MarkOrderCourierDeliveryFailedCommand Command(Guid orderId, Guid jobId)
        => new(Guid.NewGuid(), orderId, jobId, "Acheteur injoignable.", DateTime.UtcNow);

    [Fact]
    public async Task Toutes_les_parts_en_preparation_sont_annotees_et_restent_relancables()
    {
        var orderId = Guid.NewGuid();
        var repository = new FakeShipmentRepository(PreparedShipment(orderId), PreparedShipment(orderId));
        var unitOfWork = new FakeUnitOfWork();
        var jobId = Guid.NewGuid();

        var result = await Handler(repository, unitOfWork).Handle(Command(orderId, jobId), default);

        result.IsSuccess.Should().BeTrue();
        unitOfWork.SaveCount.Should().Be(1);

        repository.Shipments.Should().OnlyContain(s =>
            s.Status == ShipmentStatus.Preparing && s.FailedDeliveryJobId == jobId);

        // UN ÉVÉNEMENT PAR PART, donc un vendeur prévenu par part. Une commande
        // multi-vendeurs immobilise plusieurs boutiques, pas une seule.
        repository.Shipments.Should().OnlyContain(s => s.DomainEvents.Count == 1);
        repository.Shipments[0].DomainEvents.Single().Should().BeOfType<CourierDeliveryFailedDomainEvent>();
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// LE REJEU NE DOIT NI ÉCRIRE NI RENOTIFIER. NE PAS ASSOUPLIR.
    ///
    /// Le drain du service coursier rejoue jusqu'à dix fois. Compter les succès du
    /// domaine plutôt que de détecter le rejeu AVANT l'appel republierait l'événement à
    /// chaque tentative : le vendeur recevrait dix fois la même mauvaise nouvelle, et un
    /// vendeur noyé sous les doublons est un vendeur qui cesse de lire ses alertes.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task Le_rejeu_de_la_meme_course_ne_sauve_rien_et_ne_republie_rien()
    {
        var orderId = Guid.NewGuid();
        var repository = new FakeShipmentRepository(PreparedShipment(orderId));
        var unitOfWork = new FakeUnitOfWork();
        var jobId = Guid.NewGuid();
        var handler = Handler(repository, unitOfWork);

        await handler.Handle(Command(orderId, jobId), default);
        repository.Shipments[0].ClearDomainEvents();

        var rejeu = await handler.Handle(Command(orderId, jobId), default);

        rejeu.IsSuccess.Should().BeTrue("le drain doit s'arrêter, pas réessayer");
        unitOfWork.SaveCount.Should().Be(1, "rien n'a changé la seconde fois");
        repository.Shipments[0].DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_commande_inconnue_echoue_pour_que_l_incoherence_se_voie()
    {
        var repository = new FakeShipmentRepository();
        var unitOfWork = new FakeUnitOfWork();

        // Le coursier annonce l'échec d'une course sur une commande dont la marketplace
        // ne connaît aucune part : c'est une incohérence entre les deux systèmes. La
        // taire la rendrait invisible ; l'échec finit en lettre morte, journalisée.
        var result = await Handler(repository, unitOfWork)
            .Handle(Command(Guid.NewGuid(), Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shipping.order_has_no_shipment");
        unitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Les_parts_deja_confiees_a_un_transporteur_sont_ignorees_pas_bloquantes()
    {
        var orderId = Guid.NewGuid();
        var chezUnTiers = PreparedShipment(orderId);
        chezUnTiers.MarkShipped("DHL", "TRACK-1");
        chezUnTiers.ClearDomainEvents();

        var portee = PreparedShipment(orderId);
        var repository = new FakeShipmentRepository(chezUnTiers, portee);
        var unitOfWork = new FakeUnitOfWork();
        var jobId = Guid.NewGuid();

        var result = await Handler(repository, unitOfWork).Handle(Command(orderId, jobId), default);

        // Faire échouer l'annonce à cause de la part partie chez un tiers empêcherait de
        // débloquer celle que le coursier portait réellement.
        result.IsSuccess.Should().BeTrue();
        chezUnTiers.FailedDeliveryJobId.Should().BeNull();
        portee.FailedDeliveryJobId.Should().Be(jobId);
    }

    [Fact]
    public async Task Une_commande_dont_plus_aucune_part_n_est_en_preparation_echoue()
    {
        var orderId = Guid.NewGuid();
        var annulee = PreparedShipment(orderId);
        annulee.Cancel();

        var repository = new FakeShipmentRepository(annulee);
        var unitOfWork = new FakeUnitOfWork();

        var result = await Handler(repository, unitOfWork)
            .Handle(Command(orderId, Guid.NewGuid()), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shipping.no_shipment_to_fail");
    }

    private sealed class FakeShipmentRepository : IShipmentRepository
    {
        public FakeShipmentRepository(params Shipment[] shipments) => Shipments = shipments.ToList();

        public List<Shipment> Shipments { get; }

        public Task AddAsync(Shipment shipment, CancellationToken cancellationToken = default)
        {
            Shipments.Add(shipment);
            return Task.CompletedTask;
        }

        public Task<Shipment?> GetByIdAsync(ShipmentId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Shipments.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<Shipment>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shipment>>(Shipments.Where(s => s.OrderId == orderId).ToList());

        public Task<IReadOnlyList<Shipment>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shipment>>(Shipments.Where(s => s.SellerId == sellerId).ToList());

        public Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(Shipments.Any(s => s.OrderId == orderId));
    }

    /// <summary>Compte les commits : c'est ce qui prouve qu'un rejeu n'écrit rien.</summary>
    private sealed class FakeUnitOfWork : IShippingUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
