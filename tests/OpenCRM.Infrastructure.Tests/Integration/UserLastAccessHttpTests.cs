using System.Net;
using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Controllers.Api.V1;
using OpenCRM.WebApp.Controllers.Auth;
using Moq;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class UserLastAccessHttpTests
{
    [Theory]
    [InlineData(true, "GET", "text/html", "document", 200, true, true)]
    [InlineData(false, "GET", "text/html", "document", 200, true, false)]
    [InlineData(true, "POST", "text/html", "document", 200, true, false)]
    [InlineData(true, "GET", "application/json", "empty", 200, true, false)]
    [InlineData(true, "GET", "text/html", "iframe", 200, true, false)]
    [InlineData(true, "GET", "text/html", "document", 302, true, false)]
    [InlineData(true, "GET", "text/html", "document", 500, true, false)]
    [InlineData(true, "GET", "text/html", "document", 200, false, false)]
    public async Task SavedCookieRecordsOnlySuccessfulPageNavigations(
        bool authenticated, string method, string accept, string destination, int status, bool isPage, bool expected)
    {
        var users = new Mock<IUsersService>();
        users.Setup(service => service.RecordLastAccessAsync("visitor", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("test-cookie").AddCookie("test-cookie");
        builder.Services.AddSingleton(users.Object);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<UserLastAccessMiddleware>();
        app.MapGet("/login", async context => await context.SignInAsync("test-cookie",
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "visitor")], "test-cookie"))));
        var endpoint = app.MapMethods("/page", [method], context =>
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        });
        if (isPage)
        {
            endpoint.WithMetadata(new ControllerActionDescriptor());
        }

        await app.StartAsync();
        using var client = app.GetTestClient();
        using var login = await client.GetAsync("/login");
        using var request = new HttpRequestMessage(new HttpMethod(method), "/page");
        if (authenticated)
        {
            request.Headers.Add("Cookie", login.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        }

        request.Headers.Add("Accept", accept);
        request.Headers.Add("Sec-Fetch-Dest", destination);
        using var response = await client.SendAsync(request);

        Assert.Equal(status, (int)response.StatusCode);
        users.Verify(service => service.RecordLastAccessAsync("visitor", It.IsAny<CancellationToken>()),
            expected ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData("127.0.0.1", "192.0.2.1")]
    [InlineData("192.0.2.1", "192.0.2.1")]
    public async Task IpUsesForwardedHeaderOnlyFromTrustedProxy(string peer, string expected)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDataProtection();
        builder.Services.AddScoped<IUserAccessInfoAccessor, HttpUserAccessInfoAccessor>();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            await next(context);
        });
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor, ForwardLimit = 1 };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        app.UseForwardedHeaders(options);
        app.MapGet("/snapshot", (IUserAccessInfoAccessor accessor) => accessor.GetCurrent()!.IpAddress!);
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.1, 192.0.2.1");

        Assert.Equal(expected, await client.GetStringAsync("/snapshot"));
    }

    [Fact]
    public void SnapshotBoundsHeadersNormalizesIpAndExcludesSecrets()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.1");
        context.Request.Headers.UserAgent = new string('a', 3000);
        context.Request.Headers.AcceptLanguage = new string('b', 1000);
        context.Request.Headers["Sec-CH-UA"] = new string('c', 2000);
        context.Request.Headers["Sec-CH-UA-Platform"] = new string('d', 500);
        context.Request.Headers["Sec-CH-UA-Mobile"] = "?1";
        context.Request.Headers.Cookie = "session=secret";
        context.Request.Headers.Authorization = "Bearer secret";
        context.Request.QueryString = new QueryString("?token=secret");
        var accessor = new HttpUserAccessInfoAccessor(new HttpContextAccessor { HttpContext = context }, new EphemeralDataProtectionProvider());

        var result = accessor.GetCurrent();

        Assert.NotNull(result);
        Assert.Equal("192.0.2.1", result.IpAddress);
        Assert.Equal(2048, result.UserAgent!.Length);
        Assert.Equal(512, result.Language!.Length);
        Assert.Equal(1024, result.ClientBrands!.Length);
        Assert.Equal(256, result.Platform!.Length);
        Assert.True(result.IsMobile);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void ProtectedImpersonationSkipsSnapshotButForgedCookieDoesNot()
    {
        var provider = new EphemeralDataProtectionProvider();
        var context = new DefaultHttpContext();
        var accessor = new HttpUserAccessInfoAccessor(new HttpContextAccessor { HttpContext = context }, provider);
        context.Request.Headers.Cookie = "ImpersonatorId=" + provider.CreateProtector("OpenCRM.Auth.Impersonation.v1").Protect("admin");
        Assert.Null(accessor.GetCurrent());

        context.Request.Headers.Cookie = "ImpersonatorId=forged";
        Assert.NotNull(accessor.GetCurrent());

        context.Request.Headers.Cookie = "ImpersonatorId=" + provider.CreateProtector("OpenCRM.Auth.Impersonation.v1").Protect("admin");
        context.Items[nameof(HttpUserAccessInfoAccessor)] = true;
        Assert.NotNull(accessor.GetCurrent());
    }

    [Theory]
    [InlineData(typeof(AuthApiController), "Bearer")]
    [InlineData(typeof(AuthController), "Identity.Application")]
    public void LoginSchemeComesFromTrustedEndpointEvenBeforePrincipalIsSet(Type controller, string expected)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(null,
            new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerTypeInfo = controller.GetTypeInfo() }), "login"));
        var accessor = new HttpUserAccessInfoAccessor(new HttpContextAccessor { HttpContext = context }, new EphemeralDataProtectionProvider());

        Assert.Equal(expected, accessor.GetCurrent()!.AuthenticationScheme);
    }
}
