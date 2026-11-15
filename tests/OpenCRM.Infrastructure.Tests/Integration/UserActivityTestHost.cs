using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Controllers.Api.V1;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Infrastructure.UserActivity;
using Moq;

namespace OpenCRM.Infrastructure.Tests.Integration;

internal sealed class UserActivityTestHost(WebApplication application, HttpClient client, UserActivityOptions settings) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;

    public UserActivityOptions Settings { get; } = settings;

    public UserActivityBuffer Buffer => application.Services.GetRequiredService<UserActivityBuffer>();

    public UserActivityWriter CreateWriter() => ActivatorUtilities.CreateInstance<UserActivityWriter>(application.Services);

    public static async Task<UserActivityTestHost> CreateAsync(UserActivityFixture fixture)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
        builder.Services.AddAuthentication("activity-test").AddCookie("activity-test", options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = 401;

                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = 403;

                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization(options => options.AddPolicy(AdminAuthorizationPolicies.ViewUsers,
            policy => policy.RequireAuthenticatedUser().RequireClaim("permission", AdminAuthorizationPolicies.ViewUsers)));
        builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        builder.Services.AddControllers(options => options.Filters.Add<ApiAntiforgeryAuthorizationFilter>())
            .AddApplicationPart(typeof(UserActivityApiController).Assembly);

        builder.Services.AddUserActivity(new ConfigurationBuilder().Build());
        builder.Services.RemoveAll<IHostedService>();
        var settings = new UserActivityOptions { Enabled = true, RetentionDays = 90 };
        var monitor = new Mock<IOptionsMonitor<UserActivityOptions>>();
        monitor.SetupGet(options => options.CurrentValue).Returns(settings);
        builder.Services.AddSingleton(monitor.Object);
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseMiddleware<UserActivityMiddleware>();
        app.MapControllers();
        app.MapGet("/test/signin/{userId}/{admin:bool}", async (HttpContext context, string userId, bool admin, IAntiforgery antiforgery) =>
        {
            List<Claim> claims = [new(ClaimTypes.NameIdentifier, userId)];
            if (admin)
            {
                claims.Add(new Claim("permission", AdminAuthorizationPolicies.ViewUsers));
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "activity-test"));
            await context.SignInAsync("activity-test", context.User);

            return Results.Json(new { requestToken = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        app.MapPost("/api/v1/test-operation/{organizationId:guid}/{id:guid}", () => Results.Conflict());
        await app.StartAsync();
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");

        return new UserActivityTestHost(app, client, settings);
    }

    public async Task SignInAsync(string userId, bool admin = false, string consent = "all", bool impersonate = false)
    {
        this.Client.DefaultRequestHeaders.Remove("Cookie");
        this.Client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using var response = await this.Client.GetAsync($"/test/signin/{userId}/{admin}");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var cookies = response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]).ToList();
        if (!string.IsNullOrEmpty(consent))
        {
            cookies.Add($"cookieConsent={consent}");
        }

        cookies.Add($"opencrmActivitySession={Guid.NewGuid()}");
        if (impersonate)
        {
            cookies.Add("ImpersonatorId=protected-admin-id");
        }

        this.Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies));
        this.Client.DefaultRequestHeaders.Add("RequestVerificationToken", token!["requestToken"]);
    }

    public async ValueTask DisposeAsync()
    {
        this.Client.Dispose();
        await application.DisposeAsync();
    }
}
