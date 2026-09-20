using FluentAssertions;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Application.Notifications;
using Marketplace.Modules.Notifications.Domain.Devices;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Marketplace.Modules.Notifications.Domain.Preferences;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Notifications.UnitTests;

/// <summary>
/// Ces tests couvrent le défaut qui faisait recevoir jusqu'à DIX fois « Votre paiement a
/// été reçu » à un acheteur et « Nouvelle commande à préparer » à un vendeur : les deux
/// handlers de commande passaient par `NotifyAsync`, qui crée sans condition, alors que le
/// dispatch d'événements est « au moins une fois » et que Notifications est joué AVANT
/// Settlement, Loyalty et Analytics — une panne dans l'un d'eux rejouait tout le message.
///
/// Le magasin en mémoire ci-dessous IMITE L'INDEX UNIQUE PARTIEL sur `DeduplicationKey` :
/// sans cela, les tests valideraient le seul `SELECT` préalable, c'est-à-dire la moitié
/// fragile de la garde.
/// </summary>
public class NotificationDispatcherIdempotencyTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid BuyerId = Guid.NewGuid();

    [Fact]
    public async Task Replaying_the_same_fact_notifies_only_once()
    {
        var world = new World();
        var key = NotificationDeduplicationKeys.OrderConfirmedBuyer(OrderId);

        await NotifyConfirmedAsync(world, key, BuyerId);
        await NotifyConfirmedAsync(world, key, BuyerId);

        world.Store.Committed.Should().HaveCount(1);

        // Le doublon évité n'est pas seulement une ligne en base : c'est un push et un
        // e-mail de moins chez l'utilisateur. Sortir après l'écriture ne servirait à rien.
        world.Push.SendCount.Should().Be(1);
        world.Email.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task Each_seller_of_the_same_order_is_notified()
    {
        var world = new World();
        var firstSeller = Guid.NewGuid();
        var secondSeller = Guid.NewGuid();

        await NotifyConfirmedAsync(world, NotificationDeduplicationKeys.OrderConfirmedSeller(OrderId, firstSeller), firstSeller);
        await NotifyConfirmedAsync(world, NotificationDeduplicationKeys.OrderConfirmedSeller(OrderId, secondSeller), secondSeller);

        // Dédupliquer sur la seule commande ferait taire tous les vendeurs sauf le premier.
        world.Store.Committed.Should().HaveCount(2);
    }

    [Fact]
    public async Task Notifications_without_a_key_still_repeat()
    {
        var world = new World();

        await world.Dispatcher.NotifyAsync(BuyerId, "Nouveau message", "…", "Message", Guid.NewGuid(), default);
        await world.Dispatcher.NotifyAsync(BuyerId, "Nouveau message", "…", "Message", Guid.NewGuid(), default);

        // La déduplication est un choix explicite par appel : les faits légitimement
        // répétables ne doivent surtout pas tomber sous la contrainte d'unicité.
        world.Store.Committed.Should().HaveCount(2);
    }

    /// <summary>
    /// Course perdue sur l'index unique : le `SELECT` préalable n'a rien vu (ici, il est
    /// aveuglé volontairement), l'écriture échoue. L'exception DOIT remonter — c'est elle
    /// qui fait rejouer le message — mais le contexte doit rester utilisable, sans quoi
    /// un seul conflit priverait de notification tous les vendeurs suivants de la boucle
    /// de `SellerOrderConfirmedNotificationHandler`.
    /// </summary>
    [Fact]
    public async Task A_lost_race_does_not_poison_the_following_notifications()
    {
        var world = new World();
        var key = NotificationDeduplicationKeys.OrderConfirmedBuyer(OrderId);

        await NotifyConfirmedAsync(world, key, BuyerId);
        world.Store.BlindToExistingKeys = true;

        var conflicting = async () => await NotifyConfirmedAsync(world, key, BuyerId);
        await conflicting.Should().ThrowAsync<InvalidOperationException>();

        world.Store.Committed.Should().HaveCount(1);

        // Le vendeur suivant, lui, doit être prévenu normalement.
        await NotifyConfirmedAsync(world, NotificationDeduplicationKeys.OrderConfirmedSeller(OrderId, Guid.NewGuid()), Guid.NewGuid());
        world.Store.Committed.Should().HaveCount(2);
    }

    private static Task NotifyConfirmedAsync(World world, string key, Guid recipientId)
        => world.Dispatcher.NotifyOnceAsync(
            key, recipientId, "Commande confirmée", "Votre paiement a été reçu.", "Order", OrderId, default,
            alsoEmail: true);

    // ── Décor minimal : aucune infrastructure, uniquement des doublures en mémoire ────

    private sealed class World
    {
        public FakeStore Store { get; } = new();
        public FakePushSender Push { get; } = new();
        public FakeEmailSender Email { get; } = new();

        public NotificationDispatcher Dispatcher { get; }

        public World()
        {
            Dispatcher = new NotificationDispatcher(
                Store, Store, new FakeDeviceTokens(), new FakePreferences(), Push, Email, new FakeIdentity(),
                NullLogger<NotificationDispatcher>.Instance);
        }
    }

    private sealed class FakeStore : INotificationRepository, INotificationsUnitOfWork
    {
        private readonly List<Notification> _committed = new();
        private readonly List<Notification> _pending = new();

        /// <summary>Aveugle le `SELECT` préalable pour reproduire une course concurrente.</summary>
        public bool BlindToExistingKeys { get; set; }

        public IReadOnlyList<Notification> Committed => _committed;

        public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            _pending.Add(notification);
            return Task.CompletedTask;
        }

        public void Remove(Notification notification)
        {
            _pending.Remove(notification);
            _committed.Remove(notification);
        }

        public Task<Notification?> GetByIdAsync(NotificationId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_committed.FirstOrDefault(n => n.Id.Equals(id)));

        public Task<bool> ExistsByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken = default)
            => Task.FromResult(!BlindToExistingKeys && _committed.Any(n => n.DeduplicationKey == deduplicationKey));

        // `skip` suit l'interface, qui pagine désormais : sans lui, ce faux n'implémente
        // plus le membre et la solution entière ne compile pas (CS0535).
        public Task<IReadOnlyList<Notification>> ListByRecipientAsync(Guid recipientUserId, int take, int skip, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Notification>>(
                _committed.Where(n => n.RecipientUserId == recipientUserId).Skip(skip).Take(take).ToList());

        public Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default)
            => Task.FromResult(_committed.Count(n => n.RecipientUserId == recipientUserId && n.Status != NotificationStatus.Read));

        public Task<IReadOnlyList<Notification>> ListUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Notification>>(
                _committed.Where(n => n.RecipientUserId == recipientUserId && n.Status != NotificationStatus.Read).ToList());

        public Task<IReadOnlyList<Notification>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Notification>>(_committed.ToList());

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Imite l'index UNIQUE PARTIEL : contrainte posée uniquement sur les lignes
            // qui portent une clé, les autres restant librement répétables.
            foreach (var pending in _pending)
            {
                if (pending.DeduplicationKey is not null
                    && _committed.Any(c => c.DeduplicationKey == pending.DeduplicationKey))
                {
                    throw new InvalidOperationException(
                        $"Violation d'unicité simulée sur DeduplicationKey « {pending.DeduplicationKey} ».");
                }
            }

            var written = _pending.Count;
            _committed.AddRange(_pending);
            _pending.Clear();
            return Task.FromResult(written);
        }
    }

    private sealed class FakeDeviceTokens : IDeviceTokenRepository
    {
        public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
            => Task.FromResult<DeviceToken?>(null);

        public Task AddAsync(DeviceToken device, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(DeviceToken device) { }

        public Task<IReadOnlyList<DeviceToken>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeviceToken>>(new[] { DeviceToken.Create(userId, "jeton-de-test", "android") });

        /// <summary>
        /// Audience d'une diffusion. Ce faux n'en sert AUCUNE : ces tests-ci portent sur
        /// l'idempotence du dispatcher pour un destinataire donné, pas sur la diffusion.
        /// Rendre une liste vide est le comportement le plus honnête — il ne fait passer
        /// aucun test par accident.
        /// </summary>
        public Task<IReadOnlyList<Guid>> ListUserIdsAsync(string? platform, int max, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task RemoveByTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakePreferences : INotificationPreferenceRepository
    {
        public Task<NotificationPreference?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<NotificationPreference?>(null);

        public Task AddAsync(NotificationPreference preference, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakePushSender : IPushSender
    {
        public int SendCount { get; private set; }

        public Task<PushSendResult> SendAsync(
            IReadOnlyCollection<string> tokens, PushMessage message, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult(new PushSendResult(Array.Empty<string>()));
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public int SendCount { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeIdentity : IIdentityModuleApi
    {
        public Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserSummary?>(new UserSummary(
                userId, "Awa", "Koffi", "awa@example.test", "+22900000000", "Active", true, false, Array.Empty<Guid>()));

        public Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult<UserSummary?>(null);

        public Task<UserPayoutAccount?> GetDefaultPayoutAccountAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<UserPayoutAccount?>(null);
    }
}
