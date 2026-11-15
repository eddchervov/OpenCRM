using OpenCRM.Modules.Notifications;

namespace OpenCRM.Modules.Notifications.Tests.Unit;

public sealed class SiteNotificationTextTests
{
    [Theory]
    [InlineData("organization-invitation")]
    [InlineData("invitation-accepted")]
    [InlineData("organization-subscription-expiring")]
    [InlineData("organization-subscription-expired")]
    [InlineData("welcome")]
    public void AutomaticTypesHaveNonemptyDefaultText(string typeCode)
    {
        var (title, body) = SiteNotificationPublisher.GetDefaultText(typeCode);

        Assert.False(string.IsNullOrWhiteSpace(title));
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact]
    public void UnknownAutomaticTypeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SiteNotificationPublisher.GetDefaultText("unknown"));
    }
}
