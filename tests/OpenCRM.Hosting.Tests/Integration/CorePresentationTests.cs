using System.Reflection;
using Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.WebApp.Controllers.Auth;
using OpenCRM.WebApp.Controllers.Pages;
using OpenCRM.WebApp.Extensions;
using OpenCRM.WebApp.Models.Organizations;
using OpenCRM.Hosting.Tests.Fixtures;

namespace OpenCRM.Hosting.Tests.Integration;

public sealed class CorePresentationTests(EmptyContentRootFixture contentRootFixture)
    : IClassFixture<EmptyContentRootFixture>
{
    private static readonly ProjectBrand s_brand = new("Hosting Test", publicBaseUrl: "https://hosting.test");

    [Fact]
    public async Task CompiledProjectAttributionRendersFromSharedRazorClassLibrary()
    {
        await using var app = await this.CreateApplicationAsync();
        using var client = app.GetTestClient();

        var response = await client.GetAsync("/__tests/project-attribution");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("На базе", html);
        Assert.Contains("href=\"https://github.com/eddchervov/OpenCRM\"", html);
        Assert.Contains(">OpenCRM</a>", html);
    }

    [Fact]
    public async Task CoreControllersAreDiscoveredAndWikiProviderIsEmpty()
    {
        await using var app = await this.CreateApplicationAsync();
        var partManager = app.Services.GetRequiredService<ApplicationPartManager>();
        var feature = new ControllerFeature();
        partManager.PopulateFeature(feature);

        Assert.Contains(typeof(HomeController).GetTypeInfo(), feature.Controllers);
        Assert.Contains(typeof(AuthController).GetTypeInfo(), feature.Controllers);

        var wikiContent = app.Services.GetRequiredService<IOrganizationWikiContentProvider>();
        Assert.False(wikiContent.HasContent);
        Assert.Empty(wikiContent.Search("CRM"));
    }

    private async Task<WebApplication> CreateApplicationAsync()
    {
        Assert.Empty(Directory.EnumerateFileSystemEntries(contentRootFixture.ContentRoot));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRootFixture.ContentRoot,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenCRMPresentation(s_brand);
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(ProjectAttributionProbeController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        return app;
    }
}
