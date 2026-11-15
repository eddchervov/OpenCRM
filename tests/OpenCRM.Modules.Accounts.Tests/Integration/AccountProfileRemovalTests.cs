using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Accounts;
using OpenCRM.WebApp.Controllers.Auth;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class AccountProfileRemovalTests
{
    [Theory]
    [InlineData("GET", "/api/v1/account-profile")]
    [InlineData("PUT", "/api/v1/account-profile")]
    [InlineData("GET", "/api/v1/admin/account-profiles/scenarios")]
    [InlineData("GET", "/api/v1/admin/account-profiles/features")]
    public async Task RemovedProfileRoutesReturnNotFound(string method, string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);

        await using var app = builder.Build();
        app.UseRouting();
        app.MapControllers();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public void AddOpenCRMAccountsDoesNotRegisterAccountProfileServices()
    {
        var services = new ServiceCollection();
        services.AddOpenCRMAccounts();

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Namespace?.StartsWith("OpenCRM.Services.Interfaces.AccountProfiles") == true);
    }
}