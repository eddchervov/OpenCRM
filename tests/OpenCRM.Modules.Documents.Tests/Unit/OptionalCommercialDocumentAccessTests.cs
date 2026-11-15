using Moq;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class OptionalCommercialDocumentAccessTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task ExportsIgnorePaidGateOnlyInNeutralModeAndStillRequirePermission(bool commercialEnabled, bool denyRead, bool expected)
    {
        var organizationId = Guid.NewGuid();
        var organizations = new Mock<IOrganizationSubscriptionService>();
        organizations.Setup(service => service.HasFeatureAsync("user", organizationId, It.IsAny<string>())).ReturnsAsync(!commercialEnabled);
        organizations.Setup(service => service.EnsureWorkspaceCanReadAsync("user", organizationId, OrganizationPermissionCodes.DocumentsExport))
            .Returns(denyRead ? Task.FromException(new InvalidOperationException("Доступ запрещён.")) : Task.CompletedTask);
        var access = new DocumentExportAccess(Mock.Of<IFeatureAccessService>(), organizations.Object,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == commercialEnabled));

        Assert.Equal(expected, await access.HasWordAccessAsync("user", organizationId, true));
        Assert.Equal(expected, await access.HasPdfAccessAsync("user", organizationId, true));
        if (!commercialEnabled)
        {
            Assert.Equal(expected, await access.HasIntakeHistoryAccessAsync("user", organizationId));
            organizations.Verify(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Exactly(2));
        }
    }
}
