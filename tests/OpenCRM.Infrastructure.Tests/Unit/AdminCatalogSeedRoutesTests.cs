using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Infrastructure.Tests.Unit;

public class AdminCatalogSeedRoutesTests
{
    [Fact]
    public void SeedVehicleCatalogRequiresPostAndManageContentPermission()
    {
        var method = typeof(VehicleCatalogAdminApiController).GetMethod("Seed");

        Assert.NotNull(method);
        var post = Assert.IsType<HttpPostAttribute>(Attribute.GetCustomAttribute(method, typeof(HttpPostAttribute)));
        Assert.Equal("seed", post.Template);
        Assert.Null(Attribute.GetCustomAttribute(method, typeof(HttpGetAttribute)));
        var route = Assert.IsType<RouteAttribute>(
            Attribute.GetCustomAttribute(typeof(VehicleCatalogAdminApiController), typeof(RouteAttribute)));
        Assert.Equal("api/v1/admin/catalog", route.Template);
        var authorization = Assert.IsType<AuthorizeAttribute>(
            Attribute.GetCustomAttribute(typeof(VehicleCatalogAdminApiController), typeof(AuthorizeAttribute)));
        Assert.Equal(AdminAuthorizationPolicies.ManageContent, authorization.Policy);
    }
}
