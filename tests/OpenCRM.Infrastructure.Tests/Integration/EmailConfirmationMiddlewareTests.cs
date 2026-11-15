using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using OpenCRM.Domain.Identity;
using OpenCRM.WebApp.Infrastructure.Security;
using Moq;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class EmailConfirmationMiddlewareTests
{
    private const string SigningKey = "email-confirmation-integration-signing-key-2026-long";
    private const string CookieScheme = "Identity.Application";
    private const string JwtScheme = JwtBearerDefaults.AuthenticationScheme;

    [Fact]
    public async Task UnconfirmedCookieCannotExecutePostAndIsRedirectedToVerificationPage()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(emailConfirmed: false);
        var cookie = await testApp.SignInWithCookieAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/update");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var response = await testApp.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/verify-email", response.Headers.Location?.OriginalString);
        Assert.Equal(0, testApp.ProtectedActionCount);
    }

    [Fact]
    public async Task UnconfirmedJwtCannotExecuteApiPostAndReturnsConfirmationError()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(emailConfirmed: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/update");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            JwtScheme,
            EmailConfirmationMiddlewareTests.CreateToken(testApp.User.Id));
        using var response = await testApp.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);
        Assert.Equal("email_confirmation_required", json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("message").GetString()));
        Assert.Equal("/auth/verify-email", json.RootElement.GetProperty("confirmationUrl").GetString());
        Assert.Equal(0, testApp.ProtectedActionCount);
    }

    [Fact]
    public async Task ExistingCookieIsAllowedAfterEmailConfirmationChangesInStore()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(emailConfirmed: false);
        var cookie = await testApp.SignInWithCookieAsync();

        using var blockedRequest = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/account/current",
            cookie);
        using var blockedResponse = await testApp.Client.SendAsync(blockedRequest);
        Assert.Equal(HttpStatusCode.Redirect, blockedResponse.StatusCode);

        testApp.User.EmailConfirmed = true;
        using var allowedRequest = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/account/current",
            cookie);
        using var allowedResponse = await testApp.Client.SendAsync(allowedRequest);

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        Assert.Equal(1, testApp.ProtectedActionCount);
    }

    [Fact]
    public async Task MetadataAllowsVerificationAndStaticEndpointsButNotAdminEndpoints()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(emailConfirmed: false);
        var cookie = await testApp.SignInWithCookieAsync();

        using var verificationRequest = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/auth/verify-email",
            cookie);
        using var verificationResponse = await testApp.Client.SendAsync(verificationRequest);
        Assert.Equal(HttpStatusCode.OK, verificationResponse.StatusCode);

        using var staticRequest = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/assets/site.css",
            cookie);
        using var staticResponse = await testApp.Client.SendAsync(staticRequest);
        Assert.Equal(HttpStatusCode.OK, staticResponse.StatusCode);

        foreach (var path in new[] { "/admin-panel", "/hangfire" })
        {
            using var adminRequest = EmailConfirmationMiddlewareTests.CreateCookieRequest(
                HttpMethod.Get,
                path,
                cookie);
            using var adminResponse = await testApp.Client.SendAsync(adminRequest);

            Assert.Equal(HttpStatusCode.Redirect, adminResponse.StatusCode);
            Assert.Equal("/auth/verify-email", adminResponse.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task AllowAnonymousMetadataDoesNotBypassEmailConfirmation()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(emailConfirmed: false);
        var cookie = await testApp.SignInWithCookieAsync();
        using var request = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/anonymous-protected-page",
            cookie);

        using var response = await testApp.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/verify-email", response.Headers.Location?.OriginalString);
        Assert.Equal(0, testApp.ProtectedActionCount);
    }

    [Fact]
    public async Task UnconfirmedCookieIsRedirectedBeforeCustomRolePolicyCanDenyIt()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(emailConfirmed: false);
        var cookie = await testApp.SignInWithCookieAsync();
        using var request = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/admin/restricted",
            cookie);

        using var response = await testApp.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/verify-email", response.Headers.Location?.OriginalString);
        Assert.Equal(0, testApp.ProtectedActionCount);
    }

    [Fact]
    public async Task ConfirmedCookieWithoutRequiredRoleGetsNormalAccessDenied()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(emailConfirmed: true);
        var cookie = await testApp.SignInWithCookieAsync();
        using var request = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/admin/restricted",
            cookie);

        using var response = await testApp.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/access-denied", response.Headers.Location?.AbsolutePath);
        Assert.Equal(0, testApp.ProtectedActionCount);
    }

    [Fact]
    public async Task UnconfirmedCookieCanUseProtectedEndpointWhenConfirmationIsDisabled()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(
            emailConfirmed: false,
            requireEmailConfirmation: false);
        var cookie = await testApp.SignInWithCookieAsync();
        using var request = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/account/current",
            cookie);

        using var response = await testApp.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, testApp.ProtectedActionCount);
    }

    [Fact]
    public async Task DeletedAccountRemainsDeniedWhenConfirmationIsDisabled()
    {
        await using var testApp = await EmailConfirmationMiddlewareTests.CreateAppAsync(
            emailConfirmed: false,
            requireEmailConfirmation: false);
        testApp.User.IsDeleted = true;
        var cookie = await testApp.SignInWithCookieAsync();
        using var request = EmailConfirmationMiddlewareTests.CreateCookieRequest(
            HttpMethod.Get,
            "/account/current",
            cookie);

        using var response = await testApp.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, testApp.ProtectedActionCount);
    }

    private static async Task<TestApp> CreateAppAsync(bool emailConfirmed, bool requireEmailConfirmation = true)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName ="contact@example.invalid",
            Email = "contact@example.invalid",
            EmailConfirmed = emailConfirmed
        };
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [EmailConfirmationPolicy.ConfigurationKey] = requireEmailConfirmation.ToString()
        });
        builder.Services.AddSingleton(userManager.Object);
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieScheme;
                options.DefaultChallengeScheme = CookieScheme;
                options.DefaultScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, options => options.AccessDeniedPath = "/auth/access-denied")
            .AddJwtBearer(JwtScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "OpenCRM.IntegrationTests",
                    ValidateAudience = true,
                    ValidAudience = "OpenCRM.WebApp",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(EmailConfirmationMiddlewareTests.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        builder.Services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(CookieScheme, JwtScheme)
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
        });

        var app = builder.Build();
        var protectedActionCount = 0;
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<EmailConfirmationMiddleware>();
        app.UseAuthorization();
        app.MapGet("/test/sign-in", async (HttpContext context) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)],
                CookieScheme));
            await context.SignInAsync(CookieScheme, principal);

            return Results.NoContent();
        }).AllowAnonymous();
        app.MapGet("/auth/verify-email", () => Results.Ok("Подтвердите адрес электронной почты."))
            .WithMetadata(new AllowUnconfirmedEmailAttribute())
            .AllowAnonymous();
        app.MapGet("/assets/site.css", () => Results.Text("body {}"))
            .WithMetadata(new AllowUnconfirmedEmailAttribute())
            .AllowAnonymous();
        app.MapGet("/account/current", () =>
        {
            Interlocked.Increment(ref protectedActionCount);

            return Results.Ok();
        }).RequireAuthorization();
        app.MapPost("/account/update", () =>
        {
            Interlocked.Increment(ref protectedActionCount);

            return Results.Ok();
        }).RequireAuthorization();
        app.MapPost("/api/account/update", () =>
        {
            Interlocked.Increment(ref protectedActionCount);

            return Results.Ok();
        }).RequireAuthorization();
        app.MapGet("/anonymous-protected-page", () =>
        {
            Interlocked.Increment(ref protectedActionCount);

            return Results.Ok();
        }).AllowAnonymous();
        app.MapGet("/admin-panel", () => Results.Ok())
            .WithMetadata(new AllowUnconfirmedEmailAttribute())
            .AllowAnonymous();
        app.MapGet("/hangfire", () => Results.Ok())
            .WithMetadata(new AllowUnconfirmedEmailAttribute())
            .AllowAnonymous();
        app.MapGet("/admin/restricted", () =>
        {
            Interlocked.Increment(ref protectedActionCount);

            return Results.Ok();
        }).RequireAuthorization("AdminOnly");

        await app.StartAsync();

        return new TestApp(app, user, () => Volatile.Read(ref protectedActionCount));
    }

    private static HttpRequestMessage CreateCookieRequest(HttpMethod method, string path, string cookie)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);

        return request;
    }

    private static string CreateToken(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(EmailConfirmationMiddlewareTests.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "OpenCRM.IntegrationTests",
            audience: "OpenCRM.WebApp",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class TestApp(
        WebApplication app,
        ApplicationUser user,
        Func<int> getProtectedActionCount) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public ApplicationUser User { get; } = user;

        public int ProtectedActionCount => getProtectedActionCount();

        public async Task<string> SignInWithCookieAsync()
        {
            using var response = await this.Client.GetAsync("/test/sign-in");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var identityCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

            return identityCookie[..identityCookie.IndexOf(';')];
        }

        public async ValueTask DisposeAsync()
        {
            this.Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
