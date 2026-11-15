using OpenCRM.Modules.Planning.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Clients;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;
using OpenCRMSystemClock = OpenCRM.Domain.Interfaces.ISystemClock;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class FreeCustomerDayTests(OrganizationsDatabaseFixture fixture)
{
    private static readonly DateTime s_bookingDate = DateTime.UtcNow.Date.AddDays(1);

    [Fact]
    public async Task PlateOnlyBookingCreatesVehicleAndMaintenanceWithoutFabricatedClient()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: номер без контакта");
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        await this.AssertFreePlanAsync(organization.Id);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request("А123ВС54"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement;
        Assert.Equal(JsonValueKind.Null, result.GetProperty("organizationClientId").ValueKind);
        Assert.False(result.GetProperty("clientCreated").GetBoolean());
        Assert.True(result.GetProperty("vehicleCreated").GetBoolean());
        await using var verify = fixture.CreateContext();
        var vehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Null(vehicle.OrganizationClientId);
        Assert.True(await verify.Maintenance.AsNoTracking().AnyAsync(value => value.Id == result.GetProperty("maintenanceId").GetGuid()));
        Assert.Empty(await verify.OrganizationClients.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task PhoneOnlyBookingKeepsNewCallersPhone()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: новый звонок");
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        await this.AssertFreePlanAsync(organization.Id);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "В456ОР54",
            clientPhone: "+7 900 222-33-44"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var clientId = json.RootElement.GetProperty("organizationClientId").GetGuid();
        await using var verify = fixture.CreateContext();
        var savedClient = await verify.OrganizationClients.AsNoTracking().SingleAsync(value => value.Id == clientId);
        Assert.Equal("+7 900 222-33-44", savedClient.Phone);
    }

    [Fact]
    public async Task ReturningCustomerAndVehicleReuseExistingRecords()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: повторный визит");
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        await this.AssertFreePlanAsync(organization.Id);
        using var firstResponse = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А234ВС54",
            clientName: "Иван Петров",
            clientPhone: "+7 900 111-22-33"));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var first = firstJson.RootElement;
        var clientId = first.GetProperty("organizationClientId").GetGuid();
        var vehicleId = first.GetProperty("vehicleId").GetGuid();

        using var repeatResponse = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А 234 ВС 54",
            organizationClientId: clientId));

        Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);
        using var repeatJson = JsonDocument.Parse(await repeatResponse.Content.ReadAsStringAsync());
        Assert.Equal(vehicleId, repeatJson.RootElement.GetProperty("vehicleId").GetGuid());
        Assert.False(repeatJson.RootElement.GetProperty("vehicleCreated").GetBoolean());
        Assert.False(repeatJson.RootElement.GetProperty("clientCreated").GetBoolean());
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.Vehicles.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(1, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task SameNameWithDifferentPhoneDoesNotReuseAnotherPerson()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: одинаковые имена");
        var knownClient = new OrganizationClient
        {
            OrganizationId = organization.Id,
            Name = "Иван Иванов",
            FirstName = "Иван",
            LastName = "Иванов",
            Phone = "+7 900 111-22-33"
        };
        seed.OrganizationClients.Add(knownClient);
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        await this.AssertFreePlanAsync(organization.Id);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "Е345КХ54",
            clientName: "Иван Иванов",
            clientPhone: "+7 900 999-88-77"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resultClientId = json.RootElement.GetProperty("organizationClientId").GetGuid();
        Assert.NotEqual(knownClient.Id, resultClientId);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal("+7 900 999-88-77", await verify.OrganizationClients.AsNoTracking()
            .Where(value => value.Id == resultClientId).Select(value => value.Phone).SingleAsync());
    }

    [Fact]
    public async Task SelectedCustomerAndExistingUnlinkedVehiclePersistClientLink()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: привязка существующего авто");
        var knownClient = new OrganizationClient { OrganizationId = organization.Id, Name = "Анна Смирнова" };
        seed.OrganizationClients.Add(knownClient);
        var vehicle = CreateUnlinkedVehicle(organization);
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        await this.AssertFreePlanAsync(organization.Id);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А345ВС54",
            organizationClientId: knownClient.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(vehicle.Id, json.RootElement.GetProperty("vehicleId").GetGuid());
        Assert.Equal(knownClient.Id, json.RootElement.GetProperty("organizationClientId").GetGuid());
        Assert.False(json.RootElement.GetProperty("vehicleCreated").GetBoolean());
        Assert.False(json.RootElement.GetProperty("clientCreated").GetBoolean());
        await using var verify = fixture.CreateContext();
        var savedVehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Equal(vehicle.Id, savedVehicle.Id);
        Assert.Equal(knownClient.Id, savedVehicle.OrganizationClientId);
        Assert.Equal(vehicle.Id, (await verify.Maintenance.AsNoTracking()
            .SingleAsync(value => value.Vehicle.OrganizationId == organization.Id)).VehicleId);
        Assert.Equal(1, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task ContactForExistingUnlinkedVehiclePersistsPhoneAndClientLink()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: контакт для известного авто");
        var vehicle = CreateUnlinkedVehicle(organization);
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        await this.AssertFreePlanAsync(organization.Id);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А345ВС54",
            clientName: "Новый владелец",
            clientPhone: "+7 900 555-66-77"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var clientId = json.RootElement.GetProperty("organizationClientId").GetGuid();
        Assert.True(json.RootElement.GetProperty("clientCreated").GetBoolean());
        Assert.False(json.RootElement.GetProperty("vehicleCreated").GetBoolean());
        await using var verify = fixture.CreateContext();
        var savedClient = await verify.OrganizationClients.AsNoTracking().SingleAsync(value => value.Id == clientId);
        Assert.Equal("+7 900 555-66-77", savedClient.Phone);
        var savedVehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Equal(vehicle.Id, savedVehicle.Id);
        Assert.Equal(clientId, savedVehicle.OrganizationClientId);
        Assert.Equal(vehicle.Id, (await verify.Maintenance.AsNoTracking()
            .SingleAsync(value => value.Vehicle.OrganizationId == organization.Id)).VehicleId);
        Assert.Equal(1, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task ExplicitVehicleSelectionResolvesDuplicatePlates()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: выбор дубликата номера");
        var firstVehicle = CreateUnlinkedVehicle(organization);
        var selectedVehicle = CreateUnlinkedVehicle(organization);
        seed.Vehicles.AddRange(firstVehicle, selectedVehicle);
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А345ВС54", vehicleId: selectedVehicle.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(selectedVehicle.Id, json.RootElement.GetProperty("vehicleId").GetGuid());
        Assert.False(json.RootElement.GetProperty("vehicleCreated").GetBoolean());
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.Vehicles.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(selectedVehicle.Id, (await verify.Maintenance.AsNoTracking()
            .SingleAsync(value => value.Vehicle.OrganizationId == organization.Id)).VehicleId);
        Assert.Empty(await verify.OrganizationClients.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task AmbiguousPlateWithoutVehicleSelectionDoesNotCreateContactOrBooking()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: неоднозначный номер");
        seed.Vehicles.AddRange(CreateUnlinkedVehicle(organization), CreateUnlinkedVehicle(organization));
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А345ВС54", clientName: "Не должен сохраниться", clientPhone: "+7 900 222-33-44"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("несколько машин", json.RootElement.GetProperty("message").GetString());
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.Vehicles.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Empty(await verify.OrganizationClients.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await verify.Maintenance.AsNoTracking().Where(value => value.Vehicle.OrganizationId == organization.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundVehicleRejectsAnotherCustomerWithoutOrphanContact(bool newContact)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: конфликт клиента автомобиля");
        var owner = new OrganizationClient { OrganizationId = organization.Id, Name = "Владелец автомобиля" };
        var anotherClient = new OrganizationClient { OrganizationId = organization.Id, Name = "Другой клиент" };
        seed.OrganizationClients.AddRange(owner, anotherClient);
        var vehicle = CreateUnlinkedVehicle(organization);
        vehicle.OrganizationClientId = owner.Id;
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А345ВС54",
            organizationClientId: newContact ? null : anotherClient.Id,
            clientName: newContact ? "Новый контакт" : null,
            clientPhone: newContact ? "+7 900 222-33-44" : null,
            vehicleId: vehicle.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("другой клиент", json.RootElement.GetProperty("message").GetString());
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.OrganizationClients.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        var savedVehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Equal(owner.Id, savedVehicle.OrganizationClientId);
        Assert.Empty(await verify.Maintenance.AsNoTracking().Where(value => value.Vehicle.OrganizationId == organization.Id).ToListAsync());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("deleted")]
    [InlineData("missing")]
    [InlineData("plate")]
    [InlineData("kind")]
    public async Task InvalidSelectedVehicleDoesNotFallBackToCreatingAnotherVehicle(string invalidSelection)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: проверка выбранного автомобиля");
        var vehicleOrganization = invalidSelection == "foreign"
            ? await fixture.CreateOrganizationAsync(seed, "Чужая организация")
            : organization;
        var vehicle = CreateUnlinkedVehicle(vehicleOrganization);
        vehicle.IsDeleted = invalidSelection == "deleted";
        vehicle.Kind = invalidSelection == "kind" ? VehicleKind.Motorcycle : VehicleKind.PassengerCar;
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        var selectedVehicleId = invalidSelection == "missing" ? Guid.NewGuid() : vehicle.Id;
        var requestedPlate = invalidSelection == "plate" ? "А999ВС54" : "А345ВС54";
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            requestedPlate, clientPhone: "+7 900 222-33-44", vehicleId: selectedVehicleId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Empty(await verify.OrganizationClients.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await verify.Maintenance.AsNoTracking().Where(value => value.Vehicle.OrganizationId == organization.Id).ToListAsync());
        Assert.Equal(invalidSelection == "foreign" ? 0 : 1,
            await verify.Vehicles.AsNoTracking().CountAsync(value => value.OrganizationId == organization.Id));
        var savedVehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.Id == vehicle.Id);
        Assert.Null(savedVehicle.OrganizationClientId);
        Assert.Equal(vehicleOrganization.Id, savedVehicle.OrganizationId);
        Assert.Equal(vehicle.IsDeleted, savedVehicle.IsDeleted);
        Assert.Equal(vehicle.Kind, savedVehicle.Kind);
        Assert.Equal("А345ВС54", savedVehicle.RegistrationPlate);
    }

    [Fact]
    public async Task DeniedVehicleEditRollsBackNewContactAndBooking()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: отказ привязки клиента");
        var vehicle = CreateUnlinkedVehicle(organization);
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        using var host = this.CreateHost(OrganizationPermissionCodes.VehiclesEdit);
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);

        using var response = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А345ВС54", clientName: "Контакт для отката", clientPhone: "+7 900 222-33-44", vehicleId: vehicle.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Empty(await verify.OrganizationClients.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await verify.Maintenance.AsNoTracking().Where(value => value.Vehicle.OrganizationId == organization.Id).ToListAsync());
        var savedVehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Equal(vehicle.Id, savedVehicle.Id);
        Assert.Null(savedVehicle.OrganizationClientId);
    }

    [Fact]
    public async Task ConcurrentPlateOnlyBookingsCreateOneVehicleAndTwoOrders()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: одновременные звонки");
        using var host = this.CreateHost();
        using var firstClient = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        using var secondClient = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequest = SendAsync(firstClient);
        var secondRequest = SendAsync(secondClient);

        start.SetResult();
        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var vehicleId = firstJson.RootElement.GetProperty("vehicleId").GetGuid();
        Assert.Equal(vehicleId, secondJson.RootElement.GetProperty("vehicleId").GetGuid());
        Assert.NotEqual(firstJson.RootElement.GetProperty("vehicleCreated").GetBoolean(),
            secondJson.RootElement.GetProperty("vehicleCreated").GetBoolean());
        Assert.False(firstJson.RootElement.GetProperty("clientCreated").GetBoolean());
        Assert.False(secondJson.RootElement.GetProperty("clientCreated").GetBoolean());
        Assert.NotEqual(firstJson.RootElement.GetProperty("maintenanceId").GetGuid(),
            secondJson.RootElement.GetProperty("maintenanceId").GetGuid());
        await using var verify = fixture.CreateContext();
        var vehicle = await verify.Vehicles.AsNoTracking().SingleAsync(value => value.OrganizationId == organization.Id);
        Assert.Equal(vehicleId, vehicle.Id);
        Assert.Null(vehicle.OrganizationClientId);
        Assert.Equal(2, await verify.Maintenance.AsNoTracking().CountAsync(value => value.VehicleId == vehicleId));
        Assert.Empty(await verify.OrganizationClients.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());

        async Task<HttpResponseMessage> SendAsync(HttpClient httpClient)
        {
            await start.Task;

            return await httpClient.PostAsJsonAsync(DetailingRoute(organization.Id), Request("К765ОР54"));
        }
    }

    [Fact]
    public async Task SameNameWithoutPhoneCreatesSeparateClientsAndBothRemainSearchableForRepeatBooking()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed, "Free: два Ивана");
        using var host = this.CreateHost();
        using var client = FreeCustomerDayTests.CreateClient(host, organization.OwnerUserId);
        using var first = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request("А123ВС54", clientName: "Иван"));
        using var second = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request("А456ВС54", clientName: "Иван"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var firstClientId = firstJson.RootElement.GetProperty("organizationClientId").GetGuid();
        var secondClientId = secondJson.RootElement.GetProperty("organizationClientId").GetGuid();
        Assert.NotEqual(firstClientId, secondClientId);

        using var lookup = await client.GetAsync($"/api/v1/organizations/{organization.Id}/clients/lookup?searchQuery={Uri.EscapeDataString("Иван")}");

        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        using var lookupJson = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
        var ids = lookupJson.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(2, ids.Length);
        Assert.Contains(firstClientId, ids);
        Assert.Contains(secondClientId, ids);
        using var repeat = await client.PostAsJsonAsync(DetailingRoute(organization.Id), Request(
            "А123ВС54", organizationClientId: firstClientId, vehicleId: firstJson.RootElement.GetProperty("vehicleId").GetGuid()));
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.OrganizationClients.CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(2, await verify.Vehicles.CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(3, await verify.Maintenance.CountAsync(value => value.Vehicle.OrganizationId == organization.Id));
    }

    private IHost CreateHost(string? deniedPermission = null)
    {
        var builder = new HostBuilder()
            .ConfigureAppConfiguration(config => config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "organization-appsettings.json")))
            .ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
            {
                services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
                services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<OpenCRMContext>();
                services.AddOpenCRMRepositories();
                services.AddScoped<IApplicationUnitOfWork, ApplicationUnitOfWork>();
                services.AddScoped<IOrganizationSubscriptionService>(provider => CreateFreeSubscription(
                    provider.GetRequiredService<OpenCRMContext>(), deniedPermission));
                services.AddOpenCRMCatalog();
                services.AddOpenCRMClients();
                services.AddOpenCRMVehicles();
                services.AddOpenCRMMaintenance();
                services.AddOpenCRMOrganizations();
                services.AddScoped<IOrganizationQueryService>(provider =>
                {
                    var context = provider.GetRequiredService<OpenCRMContext>();
                    var query = new Mock<IOrganizationQueryService>();
                    query.Setup(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                        .Returns<string, Guid>(async (_, organizationId) => await context.Organizations.AsNoTracking()
                            .Where(value => value.Id == organizationId)
                            .Select(value => new OrganizationDto
                            {
                                Id = value.Id,
                                PrimaryScenarioCode = value.PrimaryScenarioCode,
                                MaintenanceWorkflowMode = value.MaintenanceWorkflowMode
                            })
                            .SingleAsync());
                    return query.Object;
                });
                var clock = new Mock<OpenCRMSystemClock>();
                clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
                clock.SetupGet(value => value.UtcToday).Returns(DateTime.UtcNow.Date);
                services.AddSingleton(clock.Object);
                var featureAccess = new Mock<IFeatureAccessService>();
                featureAccess.Setup(value => value.GetIntValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                    .ReturnsAsync(300);
                services.AddSingleton(featureAccess.Object);
                services.AddSingleton(Mock.Of<IPersonalSubscriptionQueryService>());
                services.AddSingleton(Mock.Of<IMaintenanceSchedulerSyncService>());
                var consumption = new Mock<IMaintenanceConsumptionService>();
                consumption.Setup(value => value.SynchronizeAsync(
                        It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
                    .ReturnsAsync(new MaintenanceConsumptionResult(0, []));
                services.AddSingleton(consumption.Object);
                services.AddSingleton(Mock.Of<OpenCRM.Services.Interfaces.FileStorages.IFileStorageService>());
                services.AddAuthentication("FreeCustomerDayTest")
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("FreeCustomerDayTest", _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(OrganizationDetailingQuickBookingsApiController).Assembly);
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

    private static IOrganizationSubscriptionService CreateFreeSubscription(OpenCRMContext context, string? deniedPermission = null)
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(value => value.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string code) => code == Helpers.FeatureCodes.ClientsModule);
        subscription.Setup(value => value.GetVehicleLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(300);
        subscription.Setup(value => value.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto
            {
                Features =
                [
                    new FeatureValueDto { Code = Helpers.FeatureCodes.ClientsModule, BoolValue = true },
                    new FeatureValueDto { Code = Helpers.FeatureCodes.WorkspaceClientsLimit, IntValue = 300 },
                    new FeatureValueDto { Code = Helpers.FeatureCodes.WorkspaceVehiclesLimit, IntValue = 300 }
                ]
            });
        subscription.Setup(value => value.GetWorkspaceAccessAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto
            {
                IsFreePlan = true,
                HasActiveSubscription = false,
                IsReadOnly = false
            });
        subscription.Setup(value => value.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMemberAsync(userId, organizationId));
        subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns<string, Guid>(EnsureMemberAsync);
        subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<string, Guid, string>(async (userId, organizationId, permission) =>
            {
                await EnsureMemberAsync(userId, organizationId);
                if (permission == deniedPermission)
                {
                    throw new UnauthorizedAccessException("Нет права изменять автомобиль.");
                }
            });
        return subscription.Object;

        async Task EnsureMemberAsync(string userId, Guid organizationId)
        {
            if (!await context.OrganizationMembers.AsNoTracking()
                .AnyAsync(value => value.OrganizationId == organizationId && value.UserId == userId))
            {
                throw new UnauthorizedAccessException("Нет доступа к организации.");
            }
        }
    }

    private async Task AssertFreePlanAsync(Guid organizationId)
    {
        await using var context = fixture.CreateContext();
        var subscription = CreateFreeSubscription(context);
        var access = await subscription.GetWorkspaceAccessAsync("test", organizationId);
        Assert.True(access.IsFreePlan);
        Assert.False(access.HasActiveSubscription);
    }

    private static HttpClient CreateClient(IHost host, string userId)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId);
        return client;
    }

    private static object Request(
        string plate,
        Guid? organizationClientId = null,
        string? clientName = null,
        string? clientPhone = null,
        Guid? vehicleId = null) => new
    {
        registrationPlate = plate,
        vehicleId,
        organizationClientId,
        clientName,
        clientPhone,
        plannedDate = s_bookingDate,
        services = new[] { new { name = "QA Free: базовая услуга", quantity = 1 } }
    };

    private static Vehicle CreateUnlinkedVehicle(Organization organization) => new()
    {
        UserId = organization.OwnerUserId,
        OrganizationId = organization.Id,
        RegistrationPlate = "А345ВС54",
        ManualBrandName = "Лада",
        ManualModelName = "Гранта",
        DateTimeCreation = DateTime.UtcNow
    };

    private static string DetailingRoute(Guid organizationId) =>
        $"/api/v1/organizations/{organizationId}/detailing/bookings/quick";

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
