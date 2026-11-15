using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Bookings;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class RepeatVisitBookingTests(OrganizationsDatabaseFixture fixture)
{
    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task RepeatCreatesOnePlannedBookingAndKeepsClientAndVehicle(string scenario)
    {
        await using var context = fixture.CreateContext();
        var graph = await this.SeedAsync(context, scenario);
        var harness = this.CreateHarness(context, graph);
        var payload = RepeatVisitBookingTests.Request(scenario);

        var first = await harness.Service.CreateAsync(graph.Organization.OwnerUserId, graph.Organization.Id, graph.Recommendation.Id, payload);
        var repeated = await harness.Service.CreateAsync(graph.Organization.OwnerUserId, graph.Organization.Id, graph.Recommendation.Id, payload);

        Assert.Equal(first.MaintenanceId, repeated.MaintenanceId);
        Assert.Equal(1, harness.Calls());
        var saved = await context.Maintenance.AsNoTracking().SingleAsync(item => item.VehicleId == graph.Vehicle.Id);
        Assert.Equal(MaintenanceStatus.Planned, saved.Status);
        Assert.Equal(graph.Vehicle.Id, saved.VehicleId);
        Assert.Equal(graph.Client.Id, scenario == OrganizationTypeCodes.Detailing
            ? payload.Detailing!.OrganizationClientId : payload.AutoService!.OrganizationClientId);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("disabled")]
    [InlineData("foreign-client")]
    [InlineData("wrong-scenario")]
    public async Task InvalidRecommendationDoesNotCreateBooking(string change)
    {
        await using var context = fixture.CreateContext();
        var graph = await this.SeedAsync(context, OrganizationTypeCodes.Detailing);
        var recommendation = change == "disabled" ? graph.Recommendation with { IsActive = false } : graph.Recommendation;
        var harness = this.CreateHarness(context, graph with { Recommendation = recommendation });
        var payload = RepeatVisitBookingTests.Request(change == "wrong-scenario" ? OrganizationTypeCodes.ServiceStation : OrganizationTypeCodes.Detailing);
        if (change == "stale")
        {
            payload.Version = 4;
        }
        if (change == "foreign-client")
        {
            payload.Detailing!.OrganizationClientId = Guid.NewGuid();
        }

        var create = () => harness.Service.CreateAsync(graph.Organization.OwnerUserId, graph.Organization.Id, recommendation.Id, payload);
        if (change is "stale" or "disabled")
        {
            await Assert.ThrowsAsync<RepeatVisitConflictException>(create);
        }
        else
        {
            await Assert.ThrowsAsync<ArgumentException>(create);
        }

        Assert.Equal(0, harness.Calls());
        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == graph.Vehicle.Id));
    }

    [Fact]
    public async Task FailedLinkRollsBackMaintenanceInsert()
    {
        await using var context = fixture.CreateContext();
        var graph = await this.SeedAsync(context, OrganizationTypeCodes.Detailing);
        var harness = this.CreateHarness(context, graph, true);

        await Assert.ThrowsAsync<RepeatVisitConflictException>(() => harness.Service.CreateAsync(graph.Organization.OwnerUserId,
            graph.Organization.Id, graph.Recommendation.Id, RepeatVisitBookingTests.Request(OrganizationTypeCodes.Detailing)));

        Assert.Equal(1, harness.Calls());
        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == graph.Vehicle.Id));
    }

    private async Task<Graph> SeedAsync(OpenCRMContext context, string scenario)
    {
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.PrimaryScenarioCode = scenario;
        var client = new OrganizationClient { Organization = organization, Name = "Повторный клиент" };
        var vehicle = new Vehicle { UserId = organization.OwnerUserId, OrganizationId = organization.Id,
            OrganizationClient = client, Name = "Toyota", RegistrationPlate = "А123АА154" };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return new Graph(organization, client, vehicle, new OrganizationRepeatVisitBookingContext(
            Guid.NewGuid(), client.Id, vehicle.Id, "Повторная работа", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), 1, true, null));
    }

    private Harness CreateHarness(OpenCRMContext context, Graph graph, bool failLink = false)
    {
        var calls = 0;
        var recommendation = graph.Recommendation;
        var links = new Mock<IOrganizationRepeatVisitBookingLinkService>();
        links.Setup(service => service.LockAndGetAsync(It.IsAny<string>(), graph.Organization.Id, recommendation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => recommendation);
        links.Setup(service => service.MarkBookedAsync(It.IsAny<string>(), graph.Organization.Id, recommendation.Id,
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid _, Guid _, Guid id, long _, CancellationToken _) =>
            {
                if (failLink)
                {
                    throw new RepeatVisitConflictException("Конфликт после создания записи.");
                }

                recommendation = recommendation with { BookedMaintenanceId = id, Version = recommendation.Version + 1 };

                return Task.CompletedTask;
            });
        async Task<Guid> CreateAsync()
        {
            calls++;
            var maintenance = new OpenCRM.Domain.Maintenance { UserId = graph.Organization.OwnerUserId, VehicleId = graph.Vehicle.Id, Status = MaintenanceStatus.Planned,
                Date = DateTime.UtcNow.Date, Type = MaintenanceType.Maintenance };
            context.Maintenance.Add(maintenance);
            await context.SaveChangesAsync();

            return maintenance.Id;
        }
        var detailing = new Mock<IOrganizationDetailingQuickBookingService>();
        detailing.Setup(service => service.CreateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CreateOrganizationDetailingQuickBookingDto>()))
            .Returns(async (string _, Guid _, CreateOrganizationDetailingQuickBookingDto command) =>
            {
                Assert.True(command.IsPlannedBooking);

                return new OrganizationDetailingQuickBookingResultDto { MaintenanceId = await CreateAsync(), VehicleId = graph.Vehicle.Id };
            });
        var autoService = new Mock<IOrganizationAutoServiceQuickBookingService>();
        autoService.Setup(service => service.CreateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CreateOrganizationAutoServiceQuickBookingDto>()))
            .Returns(async (string _, Guid _, CreateOrganizationAutoServiceQuickBookingDto command) =>
            {
                Assert.True(command.IsPlannedBooking);
                Assert.Equal(graph.Vehicle.Id, command.VehicleId);

                return new OrganizationAutoServiceQuickBookingResultDto { MaintenanceId = await CreateAsync(), VehicleId = graph.Vehicle.Id };
            });
        var service = new OrganizationRepeatVisitBookingService(context, links.Object, detailing.Object, autoService.Object,
            Mock.Of<IOrganizationSubscriptionService>(), new ApplicationUnitOfWork(context));

        return new Harness(service, () => calls);
    }

    private static CreateOrganizationRepeatVisitBookingDto Request(string scenario) => new()
    {
        Version = 1,
        Detailing = scenario == OrganizationTypeCodes.Detailing ? new CreateOrganizationDetailingQuickBookingDto { RegistrationPlate = "А123АА154" } : null,
        AutoService = scenario == OrganizationTypeCodes.ServiceStation ? new CreateOrganizationAutoServiceQuickBookingDto() : null
    };

    private sealed record Graph(Organization Organization, OrganizationClient Client, Vehicle Vehicle, OrganizationRepeatVisitBookingContext Recommendation);
    private sealed record Harness(OrganizationRepeatVisitBookingService Service, Func<int> Calls);
}
