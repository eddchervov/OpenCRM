using Moq;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

internal static class OrganizationInvitationTestServices
{
    internal static Mock<IOrganizationSubscriptionService> CreateSubscription()
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.GetMembersLimitAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(5);

        return subscriptions;
    }
}
