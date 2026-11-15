using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.WebApp.Infrastructure.Security;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class CommercialModuleHttpTests
{
    [Theory]
    [InlineData(false, null, 401)]
    [InlineData(false, "User", 403)]
    [InlineData(false, "Admin", 404)]
    [InlineData(true, null, 401)]
    [InlineData(true, "User", 403)]
    [InlineData(true, "Admin", 200)]
    public async Task ModuleAvailabilityDoesNotReplaceAuthenticationOrRoles(bool enabled, string? role, int expectedStatus)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = enabled.ToString()
        });
        builder.Services.AddAuthentication("CommercialProbe")
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, CommercialProbeAuthenticationHandler>("CommercialProbe", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(CommercialProbeController).Assembly);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        if (role != null)
        {
            client.DefaultRequestHeaders.Add("X-Commercial-Probe-Role", role);
        }

        using var response = await client.GetAsync("/api/commercial-module-probe");

        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("SubscriptionController")]
    [InlineData("PaymentController")]
    [InlineData("YooKassaWebhookController")]
    [InlineData("NpdReceiptDownloadController")]
    [InlineData("SubscriptionsApiController")]
    [InlineData("PaymentsPurchaseApiController")]
    [InlineData("OrganizationPromoCodesApiController")]
    [InlineData("BillingApiController")]
    [InlineData("SubscriptionPlansAdminController")]
    [InlineData("PromoCodesAdminController")]
    public void CommercialSurfacesDeclareTheModuleBoundary(string name)
    {
        var controller = typeof(CommercialModuleFilter).Assembly.GetTypes().Single(type => type.Name == name);

        Assert.Contains(controller.GetCustomAttributes(true), attribute => attribute is CommercialModuleAttribute);
    }

    [Fact]
    public void CustomerOrderPaymentsRemainOutsideTheCommercialModule()
    {
        var controller = typeof(CommercialModuleFilter).Assembly.GetTypes().Single(type => type.Name == "MaintenancePaymentApiController");

        Assert.DoesNotContain(controller.GetCustomAttributes(true), attribute => attribute is CommercialModuleAttribute);
        Assert.Contains(controller.GetCustomAttributes(true), attribute => attribute is Microsoft.AspNetCore.Authorization.AuthorizeAttribute);
    }
}
