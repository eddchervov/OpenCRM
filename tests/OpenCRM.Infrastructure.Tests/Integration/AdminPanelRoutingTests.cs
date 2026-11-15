using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Infrastructure;
using OpenCRM.WebApp.Infrastructure.Security;

namespace OpenCRM.Infrastructure.Tests.Integration;

public class AdminPanelRoutingTests
{
    [Theory]
    [InlineData("/admin-panel", true, HttpStatusCode.OK)]
    [InlineData("/admin-panel/index.html", true, HttpStatusCode.OK)]
    [InlineData("/admin-panel?desktop=1", true, HttpStatusCode.OK)]
    [InlineData("/admin-panel/assets/missing.js", true, HttpStatusCode.NotFound)]
    [InlineData("/admin-panel", false, HttpStatusCode.NotFound)]
    public async Task AdminEntryAndMissingAssetsDoNotBecomePermanentRedirects(
        string path,
        bool hasEntry,
        HttpStatusCode expectedStatus)
    {
        var root = Path.Combine(Path.GetTempPath(), "opencrm-admin-routing-" + Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(Path.Combine(webRoot, "admin-panel"));
        if (hasEntry)
        {
            await File.WriteAllTextAsync(Path.Combine(webRoot, "admin-panel", "index.html"), "<head><title>Администрирование</title></head>");
        }

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = root,
                WebRootPath = webRoot,
                EnvironmentName = Environments.Production
            });

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["AppSettings:BaseUrl"] = "https://opencrm.example.invalid";
            var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
            authorization.Setup(value => value.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), null, AdminAuthorizationPolicies.Access))
                .ReturnsAsync(AuthorizationResult.Success());
            builder.Services.AddSingleton(authorization.Object);
            builder.Services.AddSingleton(new Helpers.ProjectBrand("Север CRM"));
            await using var app = builder.Build();
            app.UseStatusCodePagesWithReExecute("/error/{0}");
            app.UseRouting();
            app.UseWhen(context => context.Request.Path.StartsWithSegments("/admin-panel"),
                branch => branch.UseMiddleware<AdminPanelAuthorizationMiddleware>());
            app.UseStaticFiles();
            app.MapGet("/Error/{status:int}", (int status) => Results.StatusCode(status));
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
            client.DefaultRequestHeaders.Host = "opencrm.example.invalid";
            using var response = await client.GetAsync(path);

            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Null(response.Headers.Location);
            if (hasEntry && expectedStatus == HttpStatusCode.OK)
            {
                Assert.Contains("name=\"project-brand\" content=\"", await response.Content.ReadAsStringAsync());
            }

            await app.StopAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
