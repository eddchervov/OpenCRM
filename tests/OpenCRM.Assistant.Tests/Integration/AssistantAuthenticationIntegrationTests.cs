using System.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class AssistantAuthenticationIntegrationTests
{
    [Fact]
    public async Task AnonymousWebNavigation_RedirectsToLoginDespiteApiBearerDefaultPolicy()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "assistant-appsettings.json"));
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, options => options.LoginPath = "/auth/login")
            .AddJwtBearer();
        builder.Services.AddAuthorization(options => options.DefaultPolicy = new AuthorizationPolicyBuilder(
            IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build());
        builder.Services.AddServerAssistant(builder.Configuration);
        builder.Services.RemoveAll<IHostedService>();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/assistant/test", () => "Готово").RequireAuthorization("AssistantWebSession");
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/assistant/test");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/auth/login?ReturnUrl=", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }
}
