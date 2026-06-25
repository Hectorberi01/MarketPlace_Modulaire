using FluentAssertions;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Xunit;

namespace Notifications.UnitTests;

public class NotificationAggregateTests
{
    private static Notification NewNotification()
        => Notification.Create(Guid.NewGuid(), NotificationChannel.InApp, "Commande confirmée", "Merci !", "Order", Guid.NewGuid()).Value;

    [Fact]
    public void Created_notification_is_pending()
        => NewNotification().Status.Should().Be(NotificationStatus.Pending);

    [Fact]
    public void MarkSent_sets_status_and_timestamp()
    {
        var notification = NewNotification();

        notification.MarkSent();

        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.SentAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void MarkRead_sets_read_status()
    {
        var notification = NewNotification();
        notification.MarkSent();

        notification.MarkRead().IsSuccess.Should().BeTrue();
        notification.Status.Should().Be(NotificationStatus.Read);
        notification.ReadAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Create_without_subject_fails()
    {
        var result = Notification.Create(Guid.NewGuid(), NotificationChannel.InApp, "  ", "body", "Order", null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notifications.subject_required");
    }
}
