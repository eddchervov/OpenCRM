using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminAuthorizationHttpTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    private const string SigningKey = "admin-authorization-integration-signing-key-2026-strong";

    [Fact]
    public async Task AdminPoliciesAcceptCookieAndBearerAndUseCurrentDatabasePermissions()
    {
        await using var context = fixture.CreateContext();
        var accessRole = AdminAuthorizationHttpTests.CreateRole("AdminAuthAccess");
        var emptyRole = AdminAuthorizationHttpTests.CreateRole("AdminAuthEmpty");
        var systemRole = AdminAuthorizationHttpTests.CreateRole("System");
        var ordinaryUser = AdminAuthorizationHttpTests.CreateUser("ordinary");
        var adminUser = AdminAuthorizationHttpTests.CreateUser("admin");
        var emptyAdminUser = AdminAuthorizationHttpTests.CreateUser("no-access");
        var systemUser = AdminAuthorizationHttpTests.CreateUser("system");
        var deletedAdminUser = AdminAuthorizationHttpTests.CreateUser("deleted");
        deletedAdminUser.IsDeleted = true;

        context.Roles.AddRange(accessRole, emptyRole, systemRole);
        context.Users.AddRange(ordinaryUser, adminUser, emptyAdminUser, systemUser, deletedAdminUser);
        context.UserRoles.AddRange(
            AdminAuthorizationHttpTests.CreateUserRole(ordinaryUser, emptyRole),
            AdminAuthorizationHttpTests.CreateUserRole(adminUser, accessRole),
            AdminAuthorizationHttpTests.CreateUserRole(emptyAdminUser, emptyRole),
            AdminAuthorizationHttpTests.CreateUserRole(systemUser, systemRole),
            AdminAuthorizationHttpTests.CreateUserRole(deletedAdminUser, systemRole));
        context.AdminRolePermissions.Add(new AdminRolePermission
        {
            RoleId = accessRole.Id,
            PermissionCode = AdminAuthorizationPolicies.Access
        });
        await context.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped(_ => fixture.CreateContext());
        builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            })
            .AddCookie(IdentityConstants.ApplicationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "OpenCRM.IntegrationTests",
                    ValidateAudience = true,
                    ValidAudience = "OpenCRM.Admin",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        builder.Services.AddOpenCRMAdminModule();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/api/admin/current-user", (ClaimsPrincipal user) =>
                Results.Text(user.FindFirstValue(ClaimTypes.NameIdentifier)))
            .RequireAuthorization(AdminAuthorizationPolicies.Access);
        app.MapGet("/api/admin/manage-permissions", () => Results.Ok())
            .RequireAuthorization(AdminAuthorizationPolicies.ManagePermissions);
        app.MapGet("/test/login", async (HttpContext httpContext) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, adminUser.Id)],
                IdentityConstants.ApplicationScheme));
            await httpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
            return Results.Ok();
        });
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var anonymousResponse = await client.GetAsync("/api/admin/current-user");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var ordinaryResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/current-user",
            ordinaryUser.Id,
            "Customer");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, ordinaryResponse.StatusCode);

        using var adminResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/current-user",
            adminUser.Id,
            accessRole.Name!);
        Assert.Equal(System.Net.HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.Equal(adminUser.Id, await adminResponse.Content.ReadAsStringAsync());

        using var loginResponse = await client.GetAsync("/test/login");
        Assert.Equal(System.Net.HttpStatusCode.OK, loginResponse.StatusCode);
        var cookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var cookieRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/current-user");
        cookieRequest.Headers.Add("Cookie", cookie);
        using var cookieResponse = await client.SendAsync(cookieRequest);
        Assert.Equal(System.Net.HttpStatusCode.OK, cookieResponse.StatusCode);

        await context.UserRoles
            .Where(userRole => userRole.UserId == adminUser.Id && userRole.RoleId == accessRole.Id)
            .ExecuteDeleteAsync();
        using var revokedAdminResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/current-user",
            adminUser.Id,
            accessRole.Name!);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, revokedAdminResponse.StatusCode);

        using var noAccessResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/current-user",
            emptyAdminUser.Id,
            emptyRole.Name!);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, noAccessResponse.StatusCode);

        using var systemAccessResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/current-user",
            systemUser.Id,
            systemRole.Name!);
        Assert.Equal(System.Net.HttpStatusCode.OK, systemAccessResponse.StatusCode);

        using var systemPermissionsResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/manage-permissions",
            systemUser.Id,
            systemRole.Name!);
        Assert.Equal(System.Net.HttpStatusCode.OK, systemPermissionsResponse.StatusCode);

        await context.UserRoles
            .Where(userRole => userRole.UserId == systemUser.Id && userRole.RoleId == systemRole.Id)
            .ExecuteDeleteAsync();
        using var revokedSystemResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/manage-permissions",
            systemUser.Id,
            systemRole.Name!);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, revokedSystemResponse.StatusCode);

        using var deletedAccessResponse = await AdminAuthorizationHttpTests.GetWithBearerAsync(
            client,
            "/api/admin/current-user",
            deletedAdminUser.Id,
            systemRole.Name!);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, deletedAccessResponse.StatusCode);

        await app.StopAsync();
    }

    private static IdentityRole CreateRole(string name)
    {
        return new IdentityRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            NormalizedName = name.ToUpperInvariant()
        };
    }

    private static ApplicationUser CreateUser(string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@example.test";
        return new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };
    }

    private static IdentityUserRole<string> CreateUserRole(ApplicationUser user, IdentityRole role)
    {
        return new IdentityUserRole<string>
        {
            UserId = user.Id,
            RoleId = role.Id
        };
    }

    private static async Task<HttpResponseMessage> GetWithBearerAsync(
        HttpClient client,
        string path,
        string userId,
        string roleName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            AdminAuthorizationHttpTests.CreateToken(userId, roleName));

        return await client.SendAsync(request);
    }

    private static string CreateToken(string userId, string roleName)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, roleName)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AdminAuthorizationHttpTests.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "OpenCRM.IntegrationTests",
            audience: "OpenCRM.Admin",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
