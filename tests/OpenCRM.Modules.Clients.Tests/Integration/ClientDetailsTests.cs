using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientDetailsTests(ClientsDatabaseFixture fixture)
{
    private const string UserId = "client-details-test";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DetailsExposeClientEditingPermission(bool canEdit)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Права связи с ботом");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(
            context,
            deniedPermissionCodes: canEdit
                ? new HashSet<string>()
                : new HashSet<string> { OrganizationPermissionCodes.ClientsEdit });

        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        Assert.Equal(canEdit, result.CanEdit);
    }

    [Fact]
    public async Task DetailsCombineDefectsWithBuyerMaintenanceHistory()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент с историей");
        var otherClient = ClientsDatabaseFixture.CreateClient(organization, "Другой покупатель");
        context.OrganizationClients.AddRange(client, otherClient);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client, "Основной автомобиль");
        var otherVehicle = await fixture.AddVehicleAsync(context, organization, client, "Архивная техника", isDeleted: true);
        var openDefect = new VehicleDefect
        {
            Organization = organization,
            Vehicle = vehicle,
            UserId = organization.OwnerUserId,
            Title = "Открытая неисправность",
            StatusCode = VehicleDefectStatusCodes.New
        };
        var resolvedDefect = new VehicleDefect
        {
            Organization = organization,
            Vehicle = vehicle,
            UserId = organization.OwnerUserId,
            Title = "Устранённая неисправность",
            StatusCode = VehicleDefectStatusCodes.Resolved,
            ClosedAt = DateTime.UtcNow.AddDays(-2)
        };
        context.VehicleDefects.AddRange(openDefect, resolvedDefect,
            new VehicleDefect
            {
                Organization = organization,
                Vehicle = otherVehicle,
                UserId = organization.OwnerUserId,
                Title = "Не показывать удалённую технику"
            });

        var completedDate = DateTime.UtcNow.AddDays(-4);
        var completed = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Completed, completedDate);
        completed.BuyerClientId = client.Id;
        completed.Type = MaintenanceType.Repair;
        completed.Comment = "Выполнен ремонт";
        var plannedDate = DateTime.UtcNow.AddDays(2);
        var planned = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Planned, plannedDate);
        planned.BuyerClientId = client.Id;
        var completedWithoutScheduledDate = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-8));
        completedWithoutScheduledDate.CompletedDate = null;
        completedWithoutScheduledDate.BuyerClientId = client.Id;
        var completedWithoutDate = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-6));
        completedWithoutDate.CompletedDate = null;
        completedWithoutDate.BuyerClientId = client.Id;
        var plannedWithoutDate = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Planned, DateTime.UtcNow.AddDays(3));
        plannedWithoutDate.PlannedDate = null;
        plannedWithoutDate.BuyerClientId = client.Id;
        var reassignedBuyer = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-10));
        reassignedBuyer.BuyerClientId = otherClient.Id;
        var deleted = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-12));
        deleted.IsDeleted = true;
        context.Maintenance.AddRange(
            completed,
            planned,
            completedWithoutScheduledDate,
            completedWithoutDate,
            plannedWithoutDate,
            reassignedBuyer,
            deleted);
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(context);
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        Assert.Equal(1, result.OpenDefectsCount);
        Assert.Equal(1, result.ClosedDefectsCount);
        Assert.Contains(result.Defects, defect => defect.Id == openDefect.Id);
        Assert.Contains(result.Defects, defect => defect.Id == resolvedDefect.Id);
        Assert.DoesNotContain(result.Defects, defect => defect.Title == "Не показывать удалённую технику");
        Assert.Contains(result.RecentMaintenances, maintenance => maintenance.Id == completed.Id);
        Assert.Contains(result.RecentMaintenances, maintenance => maintenance.Id == planned.Id);
        Assert.Contains(result.RecentMaintenances, maintenance => maintenance.Id == completedWithoutScheduledDate.Id);
        Assert.Contains(result.RecentMaintenances, maintenance => maintenance.Id == completedWithoutDate.Id);
        Assert.Contains(result.RecentMaintenances, maintenance => maintenance.Id == plannedWithoutDate.Id);
        var plannedItem = Assert.Single(result.RecentMaintenances, maintenance => maintenance.Id == planned.Id);
        Assert.Equal(plannedDate.Date, plannedItem.PlannedDate?.Date);
        var completedItem = Assert.Single(result.RecentMaintenances, maintenance => maintenance.Id == completed.Id);
        Assert.Equal(MaintenanceType.Repair, completedItem.Type);
        Assert.Equal(MaintenanceStatus.Completed, completedItem.Status);
        Assert.Equal(completedDate.Date, completedItem.CompletedDate?.Date);
        Assert.DoesNotContain(result.RecentMaintenances, maintenance => maintenance.Id == reassignedBuyer.Id);
        Assert.DoesNotContain(result.RecentMaintenances, maintenance => maintenance.Id == deleted.Id);
        var maintenanceHistory = Assert.Single(result.HistoryItems, item => item.RelatedEntityId == completed.Id && item.EventType == "maintenance");
        Assert.Equal(completedDate.Date, maintenanceHistory.CreatedAt.Date);
        Assert.Contains("Ремонт", maintenanceHistory.Title);
        Assert.Contains("Выполнено", maintenanceHistory.Title);
        Assert.Contains(result.HistoryItems, item =>
            item.RelatedEntityId == completedWithoutDate.Id
            && item.CreatedAt.Date == completedWithoutDate.Date.Date);
        Assert.Contains(result.HistoryItems, item =>
            item.RelatedEntityId == plannedWithoutDate.Id
            && item.CreatedAt.Date == plannedWithoutDate.Date.Date);
        Assert.Contains(result.HistoryItems, item => item.RelatedEntityId == resolvedDefect.Id && item.EventType == "defect_resolved");
    }

    [Fact]
    public async Task DetailsReturnVehicleLogosMaintenanceFinancialsAndUpcomingOutsideRecentLimit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context, "Чужая организация");
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент с оплатами");
        context.OrganizationClients.Add(client);
        var brand = new Brand { Name = "Марка клиента", LogoKey = "brands/client.svg" };
        context.Brands.Add(brand);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client, "Автомобиль клиента");
        vehicle.Brand = brand;
        vehicle.BrandId = brand.Id;
        await context.SaveChangesAsync();

        var oldCompleted = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-20));
        oldCompleted.BuyerClientId = client.Id;
        var recentCompleted = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1));
        recentCompleted.BuyerClientId = client.Id;
        recentCompleted.ReadyForHandoverAt = DateTime.UtcNow.AddHours(-2);
        recentCompleted.HandedOverAt = DateTime.UtcNow.AddHours(-1);
        recentCompleted.Details =
        [
            new MaintenanceDetail { Name = "Полировка кузова", Price = 1200.50m },
            new MaintenanceDetail { Name = "Материал", Price = 300, MaterialName = "Полировальная паста" }
        ];

        var upcoming = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Planned, DateTime.UtcNow.Date.AddDays(30));
        upcoming.BuyerClientId = client.Id;
        var organizationTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk");
        organization.ShiftTimeZoneId = organizationTimeZone.Id;
        var organizationToday = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, organizationTimeZone).Date,
            DateTimeKind.Utc);

        var earlierToday = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Planned, organizationToday);
        earlierToday.BuyerClientId = client.Id;
        earlierToday.PlannedDate = organizationToday;
        earlierToday.PlannedStartTime = TimeSpan.Zero;
        var todayWithoutStartTime = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Planned, organizationToday);
        todayWithoutStartTime.BuyerClientId = client.Id;
        todayWithoutStartTime.PlannedDate = organizationToday;
        var laterUpcoming = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Planned, DateTime.UtcNow.Date.AddDays(31));
        laterUpcoming.BuyerClientId = client.Id;
        var otherMaintenances = Enumerable.Range(32, 8)
            .Select(days => ClientsDatabaseFixture.CreateMaintenance(
                organization, vehicle, MaintenanceStatus.Planned, DateTime.UtcNow.Date.AddDays(days)))
            .ToList();
        foreach (var maintenance in otherMaintenances)
        {
            maintenance.BuyerClientId = client.Id;
        }

        var foreignVehicle = await fixture.AddVehicleAsync(context, foreignOrganization, name: "Чужой автомобиль");
        var foreignMaintenance = ClientsDatabaseFixture.CreateMaintenance(
            foreignOrganization, foreignVehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-2));
        foreignMaintenance.BuyerClientId = client.Id;
        context.Maintenance.AddRange(
            oldCompleted,
            recentCompleted,
            upcoming,
            earlierToday,
            todayWithoutStartTime,
            laterUpcoming,
            foreignMaintenance);
        context.Maintenance.AddRange(otherMaintenances);
        context.MaintenancePayments.AddRange(
            new MaintenancePayment { Id = Guid.NewGuid(), Maintenance = oldCompleted, Amount = 45m },
            new MaintenancePayment { Id = Guid.NewGuid(), Maintenance = recentCompleted, Amount = 20m },
            new MaintenancePayment
            {
                Id = Guid.NewGuid(),
                Maintenance = recentCompleted,
                Amount = 80m,
                CancelledAt = DateTime.UtcNow
            },
            new MaintenancePayment { Id = Guid.NewGuid(), Maintenance = foreignMaintenance, Amount = 999m });
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(context);
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        Assert.True(result.CanViewMaintenance);
        Assert.Equal(65m, result.TotalPaidAmount);
        Assert.Equal("brands/client.svg", result.Client.Vehicles.Single().BrandLogoKey);
        Assert.Equal(8, result.RecentMaintenances.Count);
        Assert.DoesNotContain(result.RecentMaintenances, maintenance => maintenance.Id == oldCompleted.Id);
        Assert.DoesNotContain(result.RecentMaintenances, maintenance => maintenance.Id == upcoming.Id);
        Assert.NotNull(result.UpcomingMaintenance);
        Assert.Equal(todayWithoutStartTime.Id, result.UpcomingMaintenance.Id);
        Assert.DoesNotContain(result.RecentMaintenances, maintenance => maintenance.Id == foreignMaintenance.Id);

        foreach (var maintenance in otherMaintenances
            .Append(upcoming)
            .Append(earlierToday)
            .Append(todayWithoutStartTime)
            .Append(laterUpcoming))
        {
            maintenance.IsDeleted = true;
        }

        await context.SaveChangesAsync();
        result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        var recentItem = Assert.Single(result.RecentMaintenances, maintenance => maintenance.Id == recentCompleted.Id);
        Assert.Equal(["Полировка кузова"], recentItem.ServiceNames);
        Assert.Equal(1500.50m, recentItem.GrossAmount);
        Assert.Equal(20m, recentItem.PaidAmount);
        Assert.Equal(recentCompleted.ReadyForHandoverAt!.Value, recentItem.ReadyForHandoverAt!.Value, TimeSpan.FromMicroseconds(1));
        Assert.Equal(recentCompleted.HandedOverAt!.Value, recentItem.HandedOverAt!.Value, TimeSpan.FromMicroseconds(1));
    }

    [Fact]
    public async Task DetailsHideFinancialAmountsWithoutFinancePermissions()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Без финансового доступа");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var maintenance = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1));
        maintenance.BuyerClientId = client.Id;
        maintenance.Details = [new MaintenanceDetail { Name = "Полировка", Price = 2500 }];
        context.Maintenance.Add(maintenance);
        context.MaintenancePayments.Add(new MaintenancePayment
        {
            Id = Guid.NewGuid(),
            Maintenance = maintenance,
            Amount = 1500m
        });
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(
            context,
            deniedPermissionCodes: new HashSet<string>
            {
                OpenCRM.Services.Interfaces.Organizations.OrganizationPermissionCodes.FinanceView,
                OpenCRM.Services.Interfaces.Organizations.OrganizationPermissionCodes.FinanceManage
            });
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        Assert.Null(result.TotalPaidAmount);
        var maintenanceItem = Assert.Single(result.RecentMaintenances);
        Assert.Null(maintenanceItem.GrossAmount);
        Assert.Null(maintenanceItem.PaidAmount);
    }

    [Fact]
    public async Task DetailsKeepMaintenanceHistoryButHideUpcomingAndServiceNamesWithoutMaintenancePermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Без доступа к обслуживанию");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var completed = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1));
        completed.BuyerClientId = client.Id;
        completed.Details = [new MaintenanceDetail { Name = "Полировка", Price = 2500 }];
        var upcoming = ClientsDatabaseFixture.CreateMaintenance(
            organization, vehicle, MaintenanceStatus.Planned, DateTime.UtcNow.AddDays(1));
        upcoming.BuyerClientId = client.Id;
        context.Maintenance.AddRange(completed, upcoming);
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(
            context,
            deniedPermissionCodes: new HashSet<string>
            {
                OpenCRM.Services.Interfaces.Organizations.OrganizationPermissionCodes.MaintenanceView
            });
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        Assert.False(result.CanViewMaintenance);
        Assert.Null(result.UpcomingMaintenance);
        Assert.Equal(2, result.RecentMaintenances.Count);
        Assert.Contains(result.RecentMaintenances, maintenance => maintenance.Id == completed.Id);
        Assert.Contains(result.RecentMaintenances, maintenance => maintenance.Id == upcoming.Id);
        Assert.All(result.RecentMaintenances, maintenance => Assert.Empty(maintenance.ServiceNames));
        Assert.Contains(result.HistoryItems, item => item.RelatedEntityId == completed.Id);
    }

    [Fact]
    public async Task DetailsReturnsNullForMissingClient()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task DetailsForClientWithoutVehiclesReturnsEmptyVehicleData()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Без техники");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        Assert.Empty(result.Client.Vehicles);
        Assert.Empty(result.Defects);
        Assert.Equal(0, result.OpenDefectsCount);
        Assert.Equal(0, result.ClosedDefectsCount);
    }

    [Fact]
    public async Task UsesLatestNonblankPhotoAndContinuesWhenStorageThrows()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент с фотографиями");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var withPhoto = await fixture.AddVehicleAsync(context, organization, client, "Фото есть");
        var blankPhoto = await fixture.AddVehicleAsync(context, organization, client, "Последнее фото пустое");
        var failingPhoto = await fixture.AddVehicleAsync(context, organization, client, "Хранилище недоступно");
        context.VehiclePhotos.AddRange(
            ClientDetailsTests.CreatePhoto(withPhoto, "vehicle/old.jpg", DateTime.UtcNow.AddDays(-1)),
            ClientDetailsTests.CreatePhoto(withPhoto, "vehicle/latest.jpg", DateTime.UtcNow),
            ClientDetailsTests.CreatePhoto(blankPhoto, "vehicle/previous.jpg", DateTime.UtcNow.AddDays(-1)),
            ClientDetailsTests.CreatePhoto(blankPhoto, "  ", DateTime.UtcNow),
            ClientDetailsTests.CreatePhoto(failingPhoto, "vehicle/fails.jpg", DateTime.UtcNow));
        await context.SaveChangesAsync();

        var storage = new Mock<IFileStorageService>();
        storage.Setup(service => service.GeneratePresignedUrlAsync("vehicle/latest.jpg"))
            .ReturnsAsync("https://storage.test/latest.jpg");
        storage.Setup(service => service.GeneratePresignedUrlAsync("vehicle/fails.jpg"))
            .ThrowsAsync(new IOException("storage unavailable"));
        var loggerProvider = new ClientsDatabaseFixture.RecordingLoggerProvider();
        using var services = fixture.CreateServices(context, storage.Object, loggerProvider);
        var details = services.GetRequiredService<IOrganizationClientDetailsQueryService>();

        var result = await details.GetDetailsAsync(UserId, organization.Id, client.Id);

        Assert.NotNull(result);
        Assert.Equal("https://storage.test/latest.jpg", result.Client.Vehicles.Single(vehicle => vehicle.Id == withPhoto.Id).PhotoUrl);
        Assert.Null(result.Client.Vehicles.Single(vehicle => vehicle.Id == blankPhoto.Id).PhotoUrl);
        Assert.Null(result.Client.Vehicles.Single(vehicle => vehicle.Id == failingPhoto.Id).PhotoUrl);
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && entry.Exception is IOException);
        storage.Verify(service => service.GeneratePresignedUrlAsync("vehicle/old.jpg"), Times.Never);
        storage.Verify(service => service.GeneratePresignedUrlAsync("vehicle/previous.jpg"), Times.Never);
    }

    private static VehiclePhoto CreatePhoto(Vehicle vehicle, string path, DateTime createdAt)
    {
        return new VehiclePhoto
        {
            Id = Guid.NewGuid(),
            Vehicle = vehicle,
            Path = path,
            Type = MediaType.VehiclePhoto,
            CreatedAt = createdAt,
            IsPrimary = false,
            ContentType = "image/jpeg",
            OriginalFileName = Path.GetFileName(path)
        };
    }
}
