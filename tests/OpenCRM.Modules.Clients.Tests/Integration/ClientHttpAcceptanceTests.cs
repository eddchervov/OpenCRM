using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientHttpAcceptanceTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task ClientRoutesUseModuleDiAndPreserveJsonAndAuthorization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        var route = $"/api/v1/organizations/{organization.Id}/clients";

        using var anonymous = await client.GetAsync(route + "/1/10");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        using var created = await client.PostAsJsonAsync(route, new
        {
            name = "  Клиент HTTP  ",
            partyTypeCode = "person",
            phone = " +79001234567 ",
            tagNamesText = "VIP, vip, Новый"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var card = createdJson.RootElement.GetProperty("client");
        var id = card.GetProperty("id").GetGuid();
        Assert.Equal("Клиент HTTP", card.GetProperty("name").GetString());
        Assert.Equal(2, card.GetProperty("tags").GetArrayLength());
        Assert.False(card.TryGetProperty("Name", out _));

        using var updated = await client.PutAsJsonAsync(route + "/" + id, new
        {
            name = "Компания HTTP", partyTypeCode = "company", inn = "123", statusCode = "lead", tagNamesText = "VIP"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedJson = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal("123", updatedJson.RootElement.GetProperty("client").GetProperty("inn").GetString());

        using var list = await client.GetAsync(route + "/1/10?searchQuery=Компания&segmentCode=leads");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, listJson.RootElement.GetProperty("items").GetArrayLength());
        using var details = await client.GetAsync(route + "/" + id);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        using var options = await client.GetAsync(route + "/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var missing = await client.GetAsync(route + "/" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await client.PostAsJsonAsync(route, new { name = " ", partyTypeCode = "person" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        context.ChangeTracker.Clear();
        Assert.Equal("Компания HTTP", (await context.OrganizationClients.SingleAsync(value => value.Id == id)).Name);
        Assert.Equal(2, await context.OrganizationClientHistoryEvents.CountAsync(value => value.OrganizationClientId == id));
        Assert.Equal(2, await context.OrganizationAuditEvents.CountAsync(value => value.TargetId == id.ToString("D")));
    }

    [Fact]
    public async Task ClientHttpRoutesBindAndReturnIdentityAndCompanyTypeFields()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/clients";

        using var createdPerson = await client.PostAsJsonAsync(route, new
        {
            name = (string?)null,
            partyTypeCode = "person",
            lastName = "Иванов",
            firstName = "Иван",
            middleName = "Иванович"
        });
        Assert.Equal(HttpStatusCode.OK, createdPerson.StatusCode);
        using var createdPersonJson = JsonDocument.Parse(await createdPerson.Content.ReadAsStringAsync());
        var personId = createdPersonJson.RootElement.GetProperty("client").GetProperty("id").GetGuid();
        AssertIdentityFields(createdPersonJson.RootElement.GetProperty("client"), "Иванов", "Иван", "Иванович", null);

        using var updatedPerson = await client.PutAsJsonAsync(route + $"/{personId}", new
        {
            name = (string?)null,
            partyTypeCode = "person",
            lastName = "Петров",
            firstName = "Пётр",
            middleName = "Сергеевич"
        });
        Assert.Equal(HttpStatusCode.OK, updatedPerson.StatusCode);

        using var personDetails = await client.GetAsync(route + $"/{personId}");
        Assert.Equal(HttpStatusCode.OK, personDetails.StatusCode);
        using var personDetailsJson = JsonDocument.Parse(await personDetails.Content.ReadAsStringAsync());
        AssertIdentityFields(personDetailsJson.RootElement.GetProperty("client"), "Петров", "Пётр", "Сергеевич", null);

        using var companyCreated = await client.PostAsJsonAsync(route, new
        {
            name = "ООО Ромашка",
            partyTypeCode = "company",
            companyTypeCode = "legal_entity",
            inn = "0012345678",
            kpp = "1234AB567"
        });
        Assert.Equal(HttpStatusCode.OK, companyCreated.StatusCode);
        using var companyCreatedJson = JsonDocument.Parse(await companyCreated.Content.ReadAsStringAsync());
        var companyId = companyCreatedJson.RootElement.GetProperty("client").GetProperty("id").GetGuid();
        AssertIdentityFields(companyCreatedJson.RootElement.GetProperty("client"), null, null, null, "legal_entity");

        using var companyUpdated = await client.PutAsJsonAsync(route + $"/{companyId}", new
        {
            name = "ИП Ромашка",
            partyTypeCode = "company",
            companyTypeCode = "entrepreneur",
            inn = "001234567890",
            kpp = ""
        });
        Assert.Equal(HttpStatusCode.OK, companyUpdated.StatusCode);

        using var companyDetails = await client.GetAsync(route + $"/{companyId}");
        Assert.Equal(HttpStatusCode.OK, companyDetails.StatusCode);
        using var companyDetailsJson = JsonDocument.Parse(await companyDetails.Content.ReadAsStringAsync());
        AssertIdentityFields(companyDetailsJson.RootElement.GetProperty("client"), null, null, null, "entrepreneur");

        using var page = await client.GetAsync(route + "/1/10");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var pageJson = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var personInPage = Assert.Single(pageJson.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == personId);
        AssertIdentityFields(personInPage, "Петров", "Пётр", "Сергеевич", null);
        var companyInPage = Assert.Single(pageJson.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == companyId);
        AssertIdentityFields(companyInPage, null, null, null, "entrepreneur");
    }

    [Fact]
    public async Task CommentLimitReturnsClearHttpErrorsWithoutChangingSavedText()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/clients";
        var boundaryComment = new string('я', 4000);

        using var created = await client.PostAsJsonAsync(route, new
        {
            name = "Комментарий", partyTypeCode = "person", comment = boundaryComment
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var createdClient = createdJson.RootElement.GetProperty("client");
        var id = createdClient.GetProperty("id").GetGuid();
        Assert.Equal(boundaryComment, createdClient.GetProperty("comment").GetString());

        using var invalidCreate = await client.PostAsJsonAsync(route, new
        {
            name = "Лишний символ", partyTypeCode = "person", comment = new string('я', 4001)
        });
        using var invalidUpdate = await client.PutAsJsonAsync(route + "/" + id, new
        {
            name = "Комментарий", partyTypeCode = "person", comment = new string('я', 4001)
        });
        using var invalidLegacyUpdate = await client.PutAsJsonAsync(route + "/" + id, new
        {
            name = "Комментарий", partyTypeCode = "person", comment = boundaryComment, notes = "Дополнение"
        });

        foreach (var response in new[] { invalidCreate, invalidUpdate, invalidLegacyUpdate })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Комментарий не должен превышать 4000 символов.", error.RootElement.GetProperty("message").GetString());
        }

        Assert.Equal(boundaryComment, await context.OrganizationClients.AsNoTracking()
            .Where(item => item.Id == id).Select(item => item.Comment).SingleAsync());
        Assert.Equal(1, await context.OrganizationClients.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Equal(1, await context.OrganizationClientHistoryEvents.CountAsync(item => item.OrganizationClientId == id));
    }

    [Fact]
    public async Task CommentEndpointChangesOnlyCommentAndEnforcesAccessAndLimit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context, "Другая организация");
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/clients";

        using var created = await client.PostAsJsonAsync(route, new
        {
            name = "Клиент для комментария", partyTypeCode = "person", phone = "+7 900 123-45-67", tagNamesText = "VIP, Постоянный"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var clientId = createdJson.RootElement.GetProperty("client").GetProperty("id").GetGuid();

        var comment = new string('я', 4000);
        using var updated = await client.PutAsJsonAsync(route + $"/{clientId}/comment", new { comment });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var updatedJson = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        var updatedClient = updatedJson.RootElement.GetProperty("client");
        Assert.Equal(comment, updatedClient.GetProperty("comment").GetString());
        Assert.Equal("Клиент для комментария", updatedClient.GetProperty("name").GetString());
        Assert.Equal("+7 900 123-45-67", updatedClient.GetProperty("phone").GetString());
        Assert.Equal(2, updatedClient.GetProperty("tags").GetArrayLength());

        using var invalid = await client.PutAsJsonAsync(route + $"/{clientId}/comment", new { comment = new string('я', 4001) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal("Комментарий не должен превышать 4000 символов.", invalidJson.RootElement.GetProperty("message").GetString());

        using var cleared = await client.PutAsJsonAsync(route + $"/{clientId}/comment", new { comment = "  " });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        using var clearedJson = JsonDocument.Parse(await cleared.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, clearedJson.RootElement.GetProperty("client").GetProperty("comment").ValueKind);

        using var deniedHost = this.CreateHost(deniedPermissionCode: OrganizationPermissionCodes.ClientsEdit);
        using var deniedClient = deniedHost.GetTestClient();
        deniedClient.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var denied = await deniedClient.PutAsJsonAsync(route + $"/{clientId}/comment", new { comment = "Недоступный комментарий" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var foreignClient = host.GetTestClient();
        foreignClient.DefaultRequestHeaders.Add("X-Test-User", foreignOrganization.OwnerUserId);
        using var foreignUpdate = await foreignClient.PutAsJsonAsync(route + $"/{clientId}/comment", new { comment = "Чужой комментарий" });
        Assert.Equal(HttpStatusCode.Forbidden, foreignUpdate.StatusCode);

        Assert.Equal(3, await context.OrganizationClientHistoryEvents.AsNoTracking().CountAsync(item => item.OrganizationClientId == clientId));
        Assert.Equal(3, await context.OrganizationAuditEvents.AsNoTracking().CountAsync(item => item.TargetId == clientId.ToString("D")));
    }

    [Fact]
    public async Task ForeignUserCannotReadOrChangeAnotherOrganizationsClients()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreign = await fixture.CreateOrganizationAsync(context, "Другая организация");
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", foreign.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/clients";

        using var read = await client.GetAsync(route + "/1/10");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        using var write = await client.PostAsJsonAsync(route, new { name = "Чужой", partyTypeCode = "person" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.False(await context.OrganizationClients.AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientRoutesRespectFeature(bool enabled)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var host = this.CreateHost(enabled);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);

        using var response = await client.GetAsync($"/api/v1/organizations/{organization.Id}/clients/1/10");
        Assert.Equal(enabled ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DirectModuleCallAlsoRejectsDisabledFeature()
    {
        await using var context = fixture.CreateContext();
        var subscription = new Mock<IOrganizationSubscriptionService>();
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(subscription.Object);
        services.AddOpenCRMClients();
        using var provider = services.BuildServiceProvider();
        var access = provider.GetRequiredService<ClientsAccess>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => access.EnsureEnabledAsync("owner", Guid.NewGuid()));
        Assert.Equal("Раздел клиентов недоступен в текущем тарифе рабочего пространства.", error.Message);
        subscription.Verify(service => service.EnsureWorkspaceCanReadAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MaintenanceHistoryRoutePagesAndRejectsUnauthorizedRequests()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var customer = ClientsDatabaseFixture.CreateClient(organization, "История HTTP");
        context.OrganizationClients.Add(customer);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, customer);
        for (var index = 0; index < 12; index++)
        {
            context.Maintenance.Add(ClientsDatabaseFixture.CreateMaintenance(organization, vehicle,
                OpenCRM.Domain.Enums.MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-index)));
        }

        await context.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        var route = $"/api/v1/organizations/{organization.Id}/clients/{customer.Id}/maintenances";
        using var anonymous = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var response = await client.GetAsync(route + "?page=2&pageSize=10&status=completed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<OrganizationClientMaintenancePageDto>();
        Assert.NotNull(result);
        Assert.Equal(12, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.Items.Count);
        using var invalid = await client.GetAsync(route + "?status=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var missing = await client.GetAsync($"/api/v1/organizations/{organization.Id}/clients/{Guid.NewGuid()}/maintenances");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var foreign = await client.GetAsync($"/api/v1/organizations/{foreignOrganization.Id}/clients/{customer.Id}/maintenances");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var disabledHost = this.CreateHost(false);
        using var disabledClient = disabledHost.GetTestClient();
        disabledClient.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var disabled = await disabledClient.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);
    }

    [Fact]
    public async Task WorkspaceAccessDenialReturnsForbiddenBeforeReadingOrChangingClients()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var outsider = await fixture.CreateOrganizationAsync(context, "Другая организация");
        using var host = this.CreateHost(featureAccessDenied: true);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", outsider.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/clients";
        var clientId = Guid.NewGuid();
        (HttpMethod Method, string Suffix)[] requests =
        [
            (HttpMethod.Get, "/1/10"),
            (HttpMethod.Get, $"/{clientId}"),
            (HttpMethod.Get, $"/{clientId}/maintenances"),
            (HttpMethod.Get, "/options"),
            (HttpMethod.Get, "/lookup?searchQuery=example"),
            (HttpMethod.Post, string.Empty),
            (HttpMethod.Put, $"/{clientId}"),
            (HttpMethod.Put, $"/{clientId}/comment")
        ];
        foreach (var (method, suffix) in requests)
        {
            using var request = new HttpRequestMessage(method, route + suffix);
            if (method != HttpMethod.Get)
            {
                request.Content = JsonContent.Create(new { name = "Недоступный клиент", comment = "Недоступный комментарий" });
            }
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.False(await context.OrganizationClients.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationClientHistoryEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    private IHost CreateHost(bool enabled = true, string? deniedPermissionCode = null, bool featureAccessDenied = false)
    {
        var builder = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
            services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<OpenCRMContext>();
            services.AddSingleton(Mock.Of<IFileStorageService>());
            services.AddScoped<IOrganizationSubscriptionService>(provider =>
            {
                var context = provider.GetRequiredService<OpenCRMContext>();
                var subscription = new Mock<IOrganizationSubscriptionService>();
                var feature = subscription.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()));
                if (featureAccessDenied)
                {
                    feature.ThrowsAsync(new InvalidOperationException("Организация не найдена или доступ закрыт."));
                }
                else
                {
                    feature.ReturnsAsync(enabled);
                }
                subscription.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                    .ReturnsAsync(new FeatureAccessSnapshotDto());
                subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                        It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                    .Returns<string, Guid, string>(async (userId, organizationId, _) =>
                    {
                        if (!await context.Organizations.AnyAsync(value => value.Id == organizationId && value.OwnerUserId == userId))
                        {
                            throw new UnauthorizedAccessException();
                        }
                    });
                subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                        It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                    .Returns<string, Guid, string>((_, _, permissionCode) => permissionCode == deniedPermissionCode
                        ? Task.FromException(new UnauthorizedAccessException())
                        : Task.CompletedTask);

                return subscription.Object;
            });
            services.AddScoped<IOrganizationAuditService>(provider =>
            {
                var context = provider.GetRequiredService<OpenCRMContext>();
                var audit = new Mock<IOrganizationAuditService>();
                audit.Setup(service => service.AddAsync(It.IsAny<OrganizationAuditEventCreateDto>(), false))
                    .Callback<OrganizationAuditEventCreateDto, bool>((dto, _) => context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
                    {
                        OrganizationId = dto.OrganizationId,
                        ActorUserId = dto.ActorUserId,
                        EventType = dto.EventType,
                        Title = dto.Title,
                        Description = dto.Description,
                        TargetType = dto.TargetType,
                        TargetId = dto.TargetId
                    }))
                    .Returns(Task.CompletedTask);

                return audit.Object;
            });
            var permissions = new Mock<IOrganizationPermissionService>();
            permissions.Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            services.AddSingleton(permissions.Object);
            services.AddOpenCRMClients();
            services.AddOpenCRMVehicles();
            services.AddOpenCRMMaintenance();
            services.AddAuthentication("ClientsTest")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("ClientsTest", _ => { });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(OrganizationClientsApiController).Assembly);
        }).Configure(application =>
        {
            application.UseRouting();
            application.UseAuthentication();
            application.UseAuthorization();
            application.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
        var host = builder.Build();
        host.Start();

        return host;
    }

    private static void AssertIdentityFields(
        JsonElement client,
        string? lastName,
        string? firstName,
        string? middleName,
        string? companyTypeCode)
    {
        Assert.Equal(lastName, client.GetProperty("lastName").ValueKind == JsonValueKind.Null ? null : client.GetProperty("lastName").GetString());
        Assert.Equal(firstName, client.GetProperty("firstName").ValueKind == JsonValueKind.Null ? null : client.GetProperty("firstName").GetString());
        Assert.Equal(middleName, client.GetProperty("middleName").ValueKind == JsonValueKind.Null ? null : client.GetProperty("middleName").GetString());
        Assert.Equal(companyTypeCode, client.GetProperty("companyTypeCode").ValueKind == JsonValueKind.Null ? null : client.GetProperty("companyTypeCode").GetString());
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
