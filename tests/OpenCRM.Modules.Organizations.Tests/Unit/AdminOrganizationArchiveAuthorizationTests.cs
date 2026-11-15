using Microsoft.AspNetCore.Authorization;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Modules.Organizations.Tests.Unit;

public sealed class AdminOrganizationArchiveAuthorizationTests
{
    [Theory]
    [InlineData("Archive")]
    [InlineData("Restore")]
    public void ArchiveMutationsRequireUserManagementPermission(string actionName)
    {
        var action = typeof(OrganizationsApiController).GetMethod(actionName);

        Assert.NotNull(action);
        var authorization = Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(AdminAuthorizationPolicies.ManageUsers, authorization.Policy);
    }
}
