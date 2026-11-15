using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Auth;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Auth.Dtos;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;
using OpenCRM.Testing;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Controllers.Auth;
using OpenCRM.WebApp.Controllers.Api.V1;
using OpenCRM.WebApp.Controllers.Admin.Api;
using OpenCRM.WebApp.Extensions;
using Moq;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminAuthenticationApiTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    private const string Password = "OpenCrm#Test1";
    private const string SigningKey = "admin-auth-api-integration-signing-key-2026-long";

    [Fact]
    public async Task LoginCurrentUserRefreshAndRevokeUseRealIdentityAndAdminControllers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UserTracking:Enabled"] = "true",
                ["Authentication:Jwt:Issuer"] = "OpenCRM.IntegrationTests",
                ["Authentication:Jwt:Audience"] = "OpenCRM.Admin",
                ["Authentication:Jwt:SigningKey"] = AdminAuthenticationApiTests.SigningKey,
                ["Authentication:Jwt:AccessTokenMinutes"] = "10",
                ["Authentication:Jwt:RefreshTokenDays"] = "2"
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddScoped(_ => fixture.CreateContext());
        builder.Services.AddOpenCRMIdentity(builder.Configuration);
        builder.Services.AddOpenCRMAuthInfrastructure(builder.Configuration);
        builder.Services.AddOpenCRMAdminModule();
        // Представления получают бренд через @inject; в приложении его регистрирует AddOpenCRMPresentation.
        builder.Services.AddSingleton(Helpers.ProjectBrand.Read(key => builder.Configuration[key]));
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<EmailConfirmationDelivery>();
        builder.Services.AddScoped<IUserAccessInfoAccessor, ThrowOnHeaderUserAccessInfoAccessor>();
        builder.Services.AddScoped<IUsersService, OpenCRM.Modules.Accounts.Users.UsersService>();
        builder.Services.AddScoped<IPersonalSubscriptionQueryService>(_ =>
        {
            var subscriptionService = new Mock<IPersonalSubscriptionQueryService>();
            subscriptionService.Setup(service => service.EnsureDefaultPlanAccessAsync(It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            return subscriptionService.Object;
        });
        builder.Services.AddScoped<IApplicationUnitOfWork, ApplicationUnitOfWork>();
        builder.Services.AddScoped<IEmailNotificationService>(_ => Mock.Of<IEmailNotificationService>());
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthApiController).Assembly)
            .AddApplicationPart(typeof(CurrentUserApiController).Assembly);

        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        var accounts = await AdminAuthenticationApiTests.CreateAccountsAsync(app.Services);
        using var client = app.GetTestClient();

        using var invalidPasswordResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new ApiLoginRequest { Email = accounts.Ordinary.Email!, Password = "incorrect" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidPasswordResponse.StatusCode);

        var ordinaryLogin = await AdminAuthenticationApiTests.LoginAsync(client, accounts.Ordinary.Email!);
        using var ordinaryCurrentUserResponse = await AdminAuthenticationApiTests.GetAdminCurrentUserAsync(
            client,
            ordinaryLogin.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, ordinaryCurrentUserResponse.StatusCode);

        using var ordinaryMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        ordinaryMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ordinaryLogin.AccessToken);
        using var ordinaryMeResponse = await client.SendAsync(ordinaryMeRequest);
        Assert.Equal(HttpStatusCode.OK, ordinaryMeResponse.StatusCode);

        var adminLogin = await AdminAuthenticationApiTests.LoginAsync(client, accounts.Admin.Email!);
        using var adminCurrentUserResponse = await AdminAuthenticationApiTests.GetAdminCurrentUserAsync(
            client,
            adminLogin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, adminCurrentUserResponse.StatusCode);
        var adminProfile = await adminCurrentUserResponse.Content.ReadFromJsonAsync<AdminCurrentUserDto>();
        Assert.NotNull(adminProfile);
        Assert.Equal(accounts.Admin.Id, adminProfile.Id);
        Assert.Equal(accounts.Admin.Email, adminProfile.Email);
        Assert.Contains(AdminAuthorizationPolicies.Access, adminProfile.Permissions);
        Assert.False(adminProfile.IsSystem);

        var systemLogin = await AdminAuthenticationApiTests.LoginAsync(client, accounts.System.Email!);
        using var systemCurrentUserResponse = await AdminAuthenticationApiTests.GetAdminCurrentUserAsync(
            client,
            systemLogin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, systemCurrentUserResponse.StatusCode);
        var systemProfile = await systemCurrentUserResponse.Content.ReadFromJsonAsync<AdminCurrentUserDto>();
        Assert.NotNull(systemProfile);
        Assert.True(systemProfile.IsSystem);

        client.DefaultRequestHeaders.UserAgent.ParseAdd("RefreshClient/2.0");
        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new ApiRefreshTokenRequest { UserId = adminProfile.Id, RefreshToken = adminLogin.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshedTokens = await refreshResponse.Content.ReadFromJsonAsync<ApiTokenResponse>();
        Assert.NotNull(refreshedTokens);
        Assert.NotEqual(adminLogin.RefreshToken, refreshedTokens.RefreshToken);

        DateTime? originalLastAccessAt;
        await using (var snapshotContext = fixture.CreateContext())
        {
            var snapshot = await snapshotContext.Users.AsNoTracking().SingleAsync(user => user.Id == adminProfile.Id);
            originalLastAccessAt = snapshot.LastAccessAt;
        }

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("InvalidRefreshClient/3.0");
        using var rotatedRefreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new ApiRefreshTokenRequest { UserId = adminProfile.Id, RefreshToken = adminLogin.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, rotatedRefreshResponse.StatusCode);

        await using (var snapshotContext = fixture.CreateContext())
        {
            var snapshot = await snapshotContext.Users.AsNoTracking().SingleAsync(user => user.Id == adminProfile.Id);
            Assert.Equal(originalLastAccessAt, snapshot.LastAccessAt);
            Assert.Equal("RefreshClient/2.0", snapshot.LastAccessUserAgent);
        }

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RefreshRecoveryClient/1.0");
        client.DefaultRequestHeaders.Add("X-Test-Throw-User-Access-Info", "true");
        using var recoveryRefreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new ApiRefreshTokenRequest { UserId = adminProfile.Id, RefreshToken = refreshedTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, recoveryRefreshResponse.StatusCode);
        var recoveryTokens = await recoveryRefreshResponse.Content.ReadFromJsonAsync<ApiTokenResponse>();
        Assert.NotNull(recoveryTokens);
        Assert.NotEqual(refreshedTokens.RefreshToken, recoveryTokens.RefreshToken);

        using var recoveryMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        recoveryMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recoveryTokens.AccessToken);
        using var recoveryMeResponse = await client.SendAsync(recoveryMeRequest);
        Assert.Equal(HttpStatusCode.OK, recoveryMeResponse.StatusCode);

        using var consumedRefreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new ApiRefreshTokenRequest { UserId = adminProfile.Id, RefreshToken = refreshedTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, consumedRefreshResponse.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Throw-User-Access-Info");

        await using (var snapshotContext = fixture.CreateContext())
        {
            var snapshot = await snapshotContext.Users.AsNoTracking().SingleAsync(user => user.Id == adminProfile.Id);
            Assert.Equal(originalLastAccessAt, snapshot.LastAccessAt);
            Assert.Equal("RefreshClient/2.0", snapshot.LastAccessUserAgent);
        }

        using var revokeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/revoke")
        {
            Content = JsonContent.Create(new ApiRevokeTokenRequest
            {
                UserId = adminProfile.Id,
                RefreshToken = recoveryTokens.RefreshToken
            })
        };
        revokeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", recoveryTokens.AccessToken);
        using var revokeResponse = await client.SendAsync(revokeRequest);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var revokedRefreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new ApiRefreshTokenRequest { UserId = adminProfile.Id, RefreshToken = recoveryTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRefreshResponse.StatusCode);

        await using (var verificationContext = fixture.CreateContext())
        {
            var refreshedUser = await verificationContext.Users.FindAsync(accounts.Admin.Id);
            Assert.NotNull(refreshedUser);
            Assert.Equal(1, refreshedUser.CountLogins);
            Assert.Equal("api-password", refreshedUser.LastLoginMethod);
            Assert.Equal("RefreshClient/2.0", refreshedUser.LastAccessUserAgent);
            Assert.Equal("Bearer", refreshedUser.LastAccessAuthenticationScheme);
            Assert.True(refreshedUser.LastAccessAt >= refreshedUser.LastLoginDate);
            var ordinaryUser = await verificationContext.Users.FindAsync(accounts.Ordinary.Id);
            Assert.Equal(1, ordinaryUser!.CountLogins);
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task CookieAuthorizationHidesDeniedPagesAndPreservesLockoutRoute()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:Issuer"] = "OpenCRM.IntegrationTests",
                ["Authentication:Jwt:Audience"] = "OpenCRM.Admin",
                ["Authentication:Jwt:SigningKey"] = AdminAuthenticationApiTests.SigningKey,
                ["IdentitySettings:Lockout:DefaultLockoutTimeSpan"] = "00:15:00"
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddScoped(_ => fixture.CreateContext());
        builder.Services.AddOpenCRMIdentity(builder.Configuration);
        builder.Services.AddOpenCRMAuthInfrastructure(builder.Configuration);
        builder.Services.AddOpenCRMAdminModule();
        // Представления получают бренд через @inject; в приложении его регистрирует AddOpenCRMPresentation.
        builder.Services.AddSingleton(Helpers.ProjectBrand.Read(key => builder.Configuration[key]));
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<EmailConfirmationDelivery>();
        builder.Services.AddScoped<IUsersService>(_ => Mock.Of<IUsersService>());
        builder.Services.AddScoped<IPersonalSubscriptionQueryService>(
            _ => Mock.Of<IPersonalSubscriptionQueryService>());
        builder.Services.AddScoped<IEmailNotificationService>(_ => Mock.Of<IEmailNotificationService>());
        builder.Services.AddScoped<IAccountRegistrationService>(_ => Mock.Of<IAccountRegistrationService>());
        builder.Services.AddScoped<IAccountDeletionService>(_ => Mock.Of<IAccountDeletionService>());
        builder.Services.AddScoped<IEmailCodeLoginService>(_ => Mock.Of<IEmailCodeLoginService>());
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(AuthController).Assembly);

        var adminRoleName = $"AdminAuthApi-{Guid.NewGuid():N}";
        await using var app = builder.Build();
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/api"),
            branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/test/protected-page", () => Results.Ok())
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = IdentityConstants.ApplicationScheme,
                Roles = adminRoleName
            });
        app.MapGet("/api/test/protected", () => Results.Ok())
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = IdentityConstants.ApplicationScheme,
                Roles = adminRoleName
            });
        app.MapPost("/test/sign-in/{userId}", async (
            string userId,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound();
            }

            var result = await signInManager.PasswordSignInAsync(user, AdminAuthenticationApiTests.Password, false, false);

            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        app.MapControllers();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        await app.StartAsync();

        var accounts = await AdminAuthenticationApiTests.CreateAccountsAsync(
            app.Services,
            adminRoleName,
            $"System-{Guid.NewGuid():N}");
        using var client = app.GetTestClient();

        using var anonymousPage = await client.GetAsync("/test/protected-page");
        Assert.Equal(HttpStatusCode.Redirect, anonymousPage.StatusCode);
        Assert.Equal("/auth/login", anonymousPage.Headers.Location?.AbsolutePath);

        using var signInResponse = await client.PostAsync($"/test/sign-in/{accounts.Ordinary.Id}", null);
        Assert.Equal(HttpStatusCode.NoContent, signInResponse.StatusCode);
        var identityCookie = Assert.Single(signInResponse.Headers.GetValues("Set-Cookie"));
        var cookieHeader = identityCookie[..identityCookie.IndexOf(';')];

        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, "/test/protected-page");
        pageRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        using var deniedPage = await client.SendAsync(pageRequest);
        Assert.Equal(HttpStatusCode.NotFound, deniedPage.StatusCode);
        Assert.Null(deniedPage.Headers.Location);
        var notFoundContent = await deniedPage.Content.ReadAsStringAsync();
        Assert.Contains("404 — страница не найдена", notFoundContent);
        Assert.DoesNotContain("Доступ временно ограничен", notFoundContent);

        using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/test/protected");
        apiRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        using var apiForbidden = await client.SendAsync(apiRequest);
        Assert.Equal(HttpStatusCode.Forbidden, apiForbidden.StatusCode);
        Assert.Null(apiForbidden.Headers.Location);

        using var lockoutPage = await client.GetAsync("/auth/lockout");
        Assert.Equal(HttpStatusCode.OK, lockoutPage.StatusCode);
        Assert.Contains("Доступ временно ограничен", await lockoutPage.Content.ReadAsStringAsync());

        await app.StopAsync();
    }

    private static async Task<AccountSet> CreateAccountsAsync(
        IServiceProvider services,
        string adminRoleName = "AdminAuthApi",
        string systemRoleName = "System")
    {
        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
        var adminRole = AdminAuthenticationApiTests.CreateRole(adminRoleName);
        var systemRole = AdminAuthenticationApiTests.CreateRole(systemRoleName);
        Assert.True((await roleManager.CreateAsync(adminRole)).Succeeded);
        Assert.True((await roleManager.CreateAsync(systemRole)).Succeeded);

        var ordinary = AdminAuthenticationApiTests.CreateUser("ordinary");
        var admin = AdminAuthenticationApiTests.CreateUser("admin");
        var system = AdminAuthenticationApiTests.CreateUser("system");
        Assert.True((await userManager.CreateAsync(ordinary, AdminAuthenticationApiTests.Password)).Succeeded);
        Assert.True((await userManager.CreateAsync(admin, AdminAuthenticationApiTests.Password)).Succeeded);
        Assert.True((await userManager.CreateAsync(system, AdminAuthenticationApiTests.Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(admin, adminRole.Name!)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(system, systemRole.Name!)).Succeeded);

        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = adminRole.Id,
            PermissionCode = AdminAuthorizationPolicies.Access
        });
        await context.SaveChangesAsync();

        return new AccountSet(ordinary, admin, system);
    }

    private static IdentityRole CreateRole(string name)
    {
        return new IdentityRole(name);
    }

    private static ApplicationUser CreateUser(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@example.test";
        return new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
    }

    private static async Task<ApiTokenResponse> LoginAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new ApiLoginRequest { Email = email, Password = AdminAuthenticationApiTests.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<ApiTokenResponse>();

        return Assert.IsType<ApiTokenResponse>(tokens);
    }

    private static Task<HttpResponseMessage> GetAdminCurrentUserAsync(HttpClient client, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/current-user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request);
    }

    private sealed class ThrowOnHeaderUserAccessInfoAccessor(
        IHttpContextAccessor contextAccessor,
        IDataProtectionProvider protectionProvider) : IUserAccessInfoAccessor
    {
        private readonly HttpUserAccessInfoAccessor _inner = new(contextAccessor, protectionProvider);

        public UserAccessInfoDto? GetCurrent()
        {
            if (contextAccessor.HttpContext?.Request.Headers.ContainsKey("X-Test-Throw-User-Access-Info") == true)
            {
                throw new InvalidOperationException("Тестовая ошибка сбора сведений о посещении.");
            }

            return this._inner.GetCurrent();
        }
    }

    private sealed record AccountSet(ApplicationUser Ordinary, ApplicationUser Admin, ApplicationUser System);
}
