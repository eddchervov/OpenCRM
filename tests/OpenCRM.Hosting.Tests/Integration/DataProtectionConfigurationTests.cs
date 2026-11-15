using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCRM.Hosting.Tests.Fixtures;
using OpenCRM.WebApp.Extensions;

namespace OpenCRM.Hosting.Tests.Integration;

public sealed class DataProtectionConfigurationTests
{
    [Theory]
    [InlineData(null, "OpenCRM.WebApp")]
    [InlineData("Consumer.WebApp", "Consumer.WebApp")]
    public void ApplicationDiscriminatorPreservesDefaultAndSupportsConsumerOverride(string? configuredName, string expectedName)
    {
        using var root = new EmptyContentRootFixture();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root.ContentRoot,
            EnvironmentName = "Testing"
        });
        builder.Configuration["DataProtection:KeysDirectory"] = Path.Combine(root.ContentRoot, "keys");
        builder.Configuration["DataProtection:ApplicationName"] = configuredName;
        builder.Configuration["Notifications:Transport"] = "Disabled";
        builder.Configuration["PaymentsService:Enabled"] = "false";
        builder.Services.AddOpenCRMInfrastructure(builder.Configuration);

        using var services = builder.Services.BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        Assert.Equal(expectedName, options.ApplicationDiscriminator);
    }
}
