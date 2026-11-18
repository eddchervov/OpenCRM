using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class QuickBookingVehicleLookupServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task ExplicitCountryDoesNotMergeIdenticalPlatesFromDifferentCountries()
    {
        await using var context = fixture.CreateContext();
        var organization = await this.CreateOrganizationAsync(context);
        var client = this.CreateClient(organization, "Тестовый клиент");
        context.OrganizationClients.Add(client);
        var chinese = this.CreateVehicle(organization, client, "ABC123", VehicleKind.PassengerCar);
        chinese.RegistrationCountryCode = "CN";
        var american = this.CreateVehicle(organization, client, "ABC123", VehicleKind.PassengerCar);
        american.RegistrationCountryCode = "US";
        context.Vehicles.AddRange(chinese, american);
        await context.SaveChangesAsync();
        var lookup = new QuickBookingVehicleLookupService(context, this.CreateSubscriptions().Object);

        var found = await lookup.FindByPlateAsync(
            organization.OwnerUserId, organization.Id, "ABC123", client.Id, VehicleKind.PassengerCar, true, "CN");
        var missing = await lookup.FindByPlateAsync(
            organization.OwnerUserId, organization.Id, "ABC123", client.Id, VehicleKind.PassengerCar, true, "GB");

        Assert.Equal(chinese.Id, found?.Id);
        Assert.Null(missing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.FindByPlateAsync(
            organization.OwnerUserId, organization.Id, "ABC123", client.Id, VehicleKind.PassengerCar, true));
    }

    [Fact]
    public async Task FindsForeignPlateWithoutRussianCharacterMappingAndRejectsAmbiguousMatches()
    {
        await using var context = fixture.CreateContext();
        var organization = await this.CreateOrganizationAsync(context);
        var client = this.CreateClient(organization, "Тестовый клиент");
        context.OrganizationClients.Add(client);
        var vehicle = this.CreateVehicle(organization, client, "沪A·12345", VehicleKind.PassengerCar);
        vehicle.RegistrationCountryCode = "CN";
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var lookup = new QuickBookingVehicleLookupService(context, this.CreateSubscriptions().Object);

        var found = await lookup.FindByPlateAsync(
            organization.OwnerUserId, organization.Id, "沪A·12345", client.Id, VehicleKind.PassengerCar);

        Assert.Equal(vehicle.Id, found?.Id);
        Assert.Equal("CN", found?.RegistrationCountryCode);
        var duplicate = this.CreateVehicle(organization, client, "沪A·12345", VehicleKind.PassengerCar);
        duplicate.RegistrationCountryCode = "CN";
        context.Vehicles.Add(duplicate);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.FindByPlateAsync(
            organization.OwnerUserId, organization.Id, "沪A·12345", client.Id, VehicleKind.PassengerCar));
    }

    [Fact]
    public async Task FindsOnlyVehiclesMatchingOrganizationKindAndOptionalClientWithoutHidingArchivedVehicles()
    {
        await using var context = fixture.CreateContext();
        var organization = await this.CreateOrganizationAsync(context);
        var otherOrganization = await this.CreateOrganizationAsync(context);
        var firstClient = this.CreateClient(organization, "Первый клиент");
        var secondClient = this.CreateClient(organization, "Второй клиент");
        var foreignClient = this.CreateClient(otherOrganization, "Чужой клиент");
        context.OrganizationClients.AddRange(firstClient, secondClient, foreignClient);
        await context.SaveChangesAsync();
        var archivedVehicle = this.CreateVehicle(
            organization,
            firstClient,
            "А123АА154",
            VehicleKind.PassengerCar,
            isArchived: true);
        var otherKindVehicle = this.CreateVehicle(
            organization,
            firstClient,
            "А123АА154",
            VehicleKind.Truck);
        var otherClientVehicle = this.CreateVehicle(
            organization,
            secondClient,
            "А123АА154",
            VehicleKind.PassengerCar);
        var deletedVehicle = this.CreateVehicle(
            organization,
            firstClient,
            "А123АА154",
            VehicleKind.PassengerCar,
            isDeleted: true);
        var foreignVehicle = this.CreateVehicle(
            otherOrganization,
            foreignClient,
            "А123АА154",
            VehicleKind.PassengerCar);
        context.Vehicles.AddRange(archivedVehicle, otherKindVehicle, otherClientVehicle, deletedVehicle, foreignVehicle);
        await context.SaveChangesAsync();
        var subscriptions = this.CreateSubscriptions();
        var lookup = new QuickBookingVehicleLookupService(context, subscriptions.Object);

        var archivedMatch = await lookup.FindByPlateAsync(
            organization.OwnerUserId,
            organization.Id,
            "A123AA154",
            firstClient.Id,
            VehicleKind.PassengerCar);
        var truckMatch = await lookup.FindByPlateAsync(
            organization.OwnerUserId,
            organization.Id,
            "A123AA154",
            firstClient.Id,
            VehicleKind.Truck);
        var secondClientMatch = await lookup.FindByPlateAsync(
            organization.OwnerUserId,
            organization.Id,
            "A123AA154",
            secondClient.Id,
            VehicleKind.PassengerCar);
        var selectedForeignVehicle = await lookup.GetSelectedAsync(
            organization.OwnerUserId,
            organization.Id,
            foreignVehicle.Id);
        var selectedDeletedVehicle = await lookup.GetSelectedAsync(
            organization.OwnerUserId,
            organization.Id,
            deletedVehicle.Id);
        var selectedArchivedVehicle = await lookup.GetSelectedAsync(
            organization.OwnerUserId,
            organization.Id,
            archivedVehicle.Id);

        Assert.Equal(archivedVehicle.Id, archivedMatch?.Id);
        Assert.Equal(otherKindVehicle.Id, truckMatch?.Id);
        Assert.Equal(otherClientVehicle.Id, secondClientMatch?.Id);
        Assert.Null(selectedForeignVehicle);
        Assert.Null(selectedDeletedVehicle);
        Assert.Equal(archivedVehicle.Id, selectedArchivedVehicle?.Id);
        subscriptions.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            organization.OwnerUserId,
            organization.Id,
            OrganizationPermissionCodes.MaintenanceCreate), Times.Exactly(6));
        subscriptions.Verify(service => service.EnsureWorkspaceCanReadAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        subscriptions.Verify(service => service.HasFeatureAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LookupPropagatesWorkspaceWriteDenial()
    {
        await using var context = fixture.CreateContext();
        var organization = await this.CreateOrganizationAsync(context);
        var subscriptions = this.CreateSubscriptions();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.MaintenanceCreate))
            .ThrowsAsync(new InvalidOperationException("Недостаточно прав."));
        var lookup = new QuickBookingVehicleLookupService(context, subscriptions.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.GetSelectedAsync(
            organization.OwnerUserId,
            organization.Id,
            Guid.NewGuid()));
    }

    [Fact]
    public async Task FreeFormatNumbersRemainDistinctAndAmbiguousStandardNumbersRequireSelection()
    {
        await using var context = fixture.CreateContext();
        var organization = await this.CreateOrganizationAsync(context);
        var client = this.CreateClient(organization, "Клиент для проверки формата");
        context.OrganizationClients.Add(client);
        var latinBoat = this.CreateVehicle(organization, client, "AB-123", VehicleKind.Boat);
        var cyrillicBoat = this.CreateVehicle(organization, client, "АВ123", VehicleKind.Boat);
        context.Vehicles.AddRange(latinBoat, cyrillicBoat,
            this.CreateVehicle(organization, client, "A123BC000", VehicleKind.PassengerCar),
            this.CreateVehicle(organization, client, "А123ВС000", VehicleKind.PassengerCar));
        await context.SaveChangesAsync();
        var lookup = new QuickBookingVehicleLookupService(context, this.CreateSubscriptions().Object);

        var exactBoat = await lookup.FindByPlateAsync(organization.OwnerUserId, organization.Id, "AB-123", client.Id, VehicleKind.Boat, allowOtherFormat: true);

        Assert.Equal(latinBoat.Id, exactBoat?.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lookup.FindByPlateAsync(
            organization.OwnerUserId, organization.Id, "А123ВС000", client.Id, VehicleKind.PassengerCar));
    }

    private async Task<Organization> CreateOrganizationAsync(OpenCRM.Infrastructure.Data.OpenCRMContext context)
    {
        var user = new ApplicationUser
        {
            UserName = $"vehicle-lookup-{Guid.NewGuid():N}",
            Email = $"vehicle-lookup-{Guid.NewGuid():N}@example.test"
        };
        var organization = new Organization
        {
            Name = "Рабочее пространство",
            PrimaryScenarioCode = "detailing",
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };

        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    private OrganizationClient CreateClient(Organization organization, string name) => new()
    {
        Organization = organization,
        Name = name
    };

    private Vehicle CreateVehicle(
        Organization organization,
        OrganizationClient client,
        string plate,
        VehicleKind kind,
        bool isArchived = false,
        bool isDeleted = false) => new()
    {
        Organization = organization,
        OrganizationClient = client,
        UserId = organization.OwnerUserId,
        Kind = kind,
        RegistrationPlate = plate,
        DateTimeCreation = DateTime.UtcNow,
        IsArchived = isArchived,
        IsDeleted = isDeleted
    };

    private Mock<IOrganizationSubscriptionService> CreateSubscriptions()
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), OrganizationPermissionCodes.MaintenanceCreate))
            .Returns(Task.CompletedTask);

        return subscriptions;
    }
}
