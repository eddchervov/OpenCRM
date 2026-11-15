using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Testing;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class OrganizationWorkJournalQueryTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task WorkJournalCombinesWorkHistoryAndDefectsAndAppliesKindDateAndVehicleFilters()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "owner@example.test", UserName ="owner" };
        var organization = new Organization
        {
            Name = "Журнал работ",
            OwnerUser = owner,
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing
        };
        var client = new OrganizationClient { Organization = organization, Name = "Клиент журнала" };
        var vehicle = CreateVehicle(owner, organization, client, "А123АА154");
        vehicle.OrganizationClient = null;
        var secondVehicle = CreateVehicle(owner, organization, client, "В456ВВ154");
        var brandOnlyVehicle = CreateVehicle(owner, organization, client, "Е987ЕЕ154");
        brandOnlyVehicle.Brand = new Brand { Name = "Марка без модели" };
        var archivedVehicle = CreateVehicle(owner, organization, client, "С789СС154");
        archivedVehicle.IsArchived = true;
        var deletedVehicle = CreateVehicle(owner, organization, client, "Н000НН154");
        deletedVehicle.IsDeleted = true;
        var catalogBrand = new Brand { Name = "Каталожная марка" };
        var catalogModel = new Model { Brand = catalogBrand, Name = "Каталожная модель" };
        secondVehicle.Brand = catalogBrand;
        secondVehicle.Model = catalogModel;
        secondVehicle.ManualBrandName = null;
        secondVehicle.ManualModelName = null;
        var from = UtcDate(2026, 10, 10);
        var to = from.AddDays(2);
        var completedWithDetails = CreateCompletedMaintenance(owner, vehicle, from.AddDays(1));
        var separateBuyer = new OrganizationClient { Organization = organization, Name = "Покупатель заказа" };
        completedWithDetails.BuyerClientId = separateBuyer.Id;
        completedWithDetails.BuyerName = separateBuyer.Name;
        completedWithDetails.PlannedStartTime = TimeSpan.FromHours(9);
        completedWithDetails.Mileage = 1200;
        vehicle.MileageUnit = VehicleMileageUnit.EngineHours;
        completedWithDetails.Details = [new() { Name = "Полировка", Price = 100m }, new() { Name = " " }, new() { Name = "Химчистка", Price = 50m }];
        var completedWithoutDetails = CreateCompletedMaintenance(owner, secondVehicle, to);
        completedWithoutDetails.BuyerName = "Снимок без покупателя";
        completedWithoutDetails.Details = [];
        completedWithoutDetails.Comment = "Работа без строк";
        var anotherCompleted = CreateCompletedMaintenance(owner, secondVehicle, to.AddDays(1));
        var historyUsers = new[]
        {
            new ApplicationUser { UserName ="actor", Email = "actor@example.test" },
            new ApplicationUser { UserName ="original", Email = "email@example.test", OriginalEmail = "original@example.test" },
            new ApplicationUser { UserName ="email", Email = "email-only@example.test" },
            new ApplicationUser { UserName ="username" },
            new ApplicationUser { IsDeleted = true, Email = "deleted@example.test" },
            new ApplicationUser { IsDeleted = true },
            new ApplicationUser()
        };
        var eventTypes = new[]
        {
            "created", "updated", "status_changed", "deleted", "defect_linked", "defect_unlinked",
            "fields_changed", "details_changed", "document_generated", "unknown"
        };
        var maintenanceEvents = eventTypes.Select((eventType, index) => new MaintenanceHistoryEvent
        {
            Maintenance = completedWithDetails,
            VehicleId = vehicle.Id,
            OrganizationId = index == 0 ? organization.Id : null,
            User = index < historyUsers.Length ? historyUsers[index] : owner,
            ActorDisplayName = index == 0 ? "Имя на момент события" : null,
            EventType = eventType,
            Title = $"Событие {eventType}",
            CreatedAt = from.AddHours(index + 1)
        }).ToList();
        var createdDefect = CreateDefect(owner, organization, vehicle, "Неисправность создана", from.AddHours(2));
        var closedDefect = CreateDefect(owner, organization, secondVehicle, "Неисправность закрыта", from.AddDays(-1));
        closedDefect.ClosedAt = to.AddHours(3);
        closedDefect.StatusCode = VehicleDefectStatusCodes.Resolved;
        closedDefect.SeverityCode = VehicleDefectSeverityCodes.High;
        var bothDatesDefect = CreateDefect(owner, organization, secondVehicle, "Создана и закрыта", from.AddHours(4));
        bothDatesDefect.ClosedAt = from.AddHours(5);
        var manualClosedDefect = CreateDefect(owner, organization, vehicle, "Закрыта вручную", from.AddDays(-1));
        manualClosedDefect.ClosedAt = from.AddHours(8);
        var brandOnlyClosedDefect = CreateDefect(owner, organization, brandOnlyVehicle, "Закрыта с маркой", from.AddDays(-1));
        brandOnlyClosedDefect.ClosedAt = from.AddHours(10);
        var outsideDefect = CreateDefect(owner, organization, vehicle, "Вне периода", from.AddDays(-2));
        outsideDefect.ClosedAt = from.AddDays(-1);
        var defectHistory = new VehicleHistoryEvent
        {
            Vehicle = secondVehicle,
            OrganizationId = organization.Id,
            User = historyUsers[2],
            EventType = "updated",
            RelatedEntityType = "defect",
            RelatedEntityId = closedDefect.Id,
            Title = "История неисправности",
            Description = "Обновлено описание",
            CreatedAt = from.AddHours(6)
        };
        var unrelatedHistory = new VehicleHistoryEvent
        {
            Vehicle = secondVehicle,
            OrganizationId = organization.Id,
            User = owner,
            EventType = "updated",
            RelatedEntityType = "vehicle",
            RelatedEntityId = Guid.NewGuid(),
            Title = "Не неисправность",
            CreatedAt = from.AddHours(7)
        };
        var manualDefectHistory = new VehicleHistoryEvent
        {
            Vehicle = vehicle,
            OrganizationId = organization.Id,
            User = owner,
            EventType = "updated",
            RelatedEntityType = "defect",
            RelatedEntityId = createdDefect.Id,
            Title = "История неисправности вручную",
            CreatedAt = from.AddHours(9)
        };

        context.Users.AddRange(historyUsers.Prepend(owner));
        context.Organizations.Add(organization);
        context.OrganizationClients.Add(client);
        context.OrganizationClients.Add(separateBuyer);
        context.Brands.AddRange(catalogBrand, brandOnlyVehicle.Brand);
        context.Models.Add(catalogModel);
        context.Vehicles.AddRange(archivedVehicle, deletedVehicle);
        context.Maintenance.AddRange(completedWithDetails, completedWithoutDetails, anotherCompleted);
        context.MaintenanceHistoryEvents.AddRange(maintenanceEvents);
        context.VehicleDefects.AddRange(createdDefect, closedDefect, bothDatesDefect, manualClosedDefect, brandOnlyClosedDefect, outsideDefect);
        context.VehicleHistoryEvents.AddRange(defectHistory, unrelatedHistory, manualDefectHistory);
        await context.SaveChangesAsync();

        var service = new OrganizationWorkJournalQueryService(context);
        var all = await service.GetWorkJournalAsync(organization.Id, to, from, null, null, "history", hasDefectsModule: true);
        var maintenanceOnly = await service.GetWorkJournalAsync(organization.Id, from, to, vehicle.Id, " maintenance ", "invalid", hasDefectsModule: false);
        var defectsForSecondVehicle = await service.GetWorkJournalAsync(organization.Id, from, to, secondVehicle.Id, "DEFECT", null, hasDefectsModule: true);
        var unknownKind = await service.GetWorkJournalAsync(organization.Id, from, to, null, "unknown", " HISTORY ", hasDefectsModule: false);

        Assert.Equal("", all.SelectedKind);
        Assert.Equal("history", all.SelectedTab);
        Assert.Equal(2, all.CompletedMaintenancesCount);
        Assert.Equal(4, all.WorkItems.Count(item => item.Kind == "defect"));
        Assert.Equal(10, all.MaintenanceEventsCount);
        Assert.Equal(2, all.DefectsCreatedCount);
        Assert.Equal(4, all.DefectsClosedCount);
        Assert.Equal(18, all.HistoryItemsCount);
        Assert.Equal("Имя на момент события", Assert.Single(all.HistoryItems, item => item.Title == "Событие created").ActorDisplayName);
        Assert.Equal("original@example.test", Assert.Single(all.HistoryItems, item => item.Title == "Событие updated").ActorDisplayName);
        Assert.Equal("email-only@example.test", Assert.Single(all.HistoryItems, item => item.Title == "Событие status_changed").ActorDisplayName);
        Assert.Equal("username", Assert.Single(all.HistoryItems, item => item.Title == "Событие deleted").ActorDisplayName);
        Assert.Equal("deleted@example.test (удален)", Assert.Single(all.HistoryItems, item => item.Title == "Событие defect_linked").ActorDisplayName);
        Assert.Equal("Удаленный пользователь", Assert.Single(all.HistoryItems, item => item.Title == "Событие defect_unlinked").ActorDisplayName);
        Assert.Equal("Пользователь не указан", Assert.Single(all.HistoryItems, item => item.Title == "Событие fields_changed").ActorDisplayName);
        Assert.Equal(2, all.HistoryItems.Count(item => item.Title == "Создана и закрыта"));
        Assert.Contains(all.WorkItems, item => item.ResultText == "Полировка, Химчистка" && item.MetaText?.Contains("1", StringComparison.Ordinal) == true);
        Assert.Contains(all.WorkItems, item => item.ResultText == "Работа без строк" && item.MetaText == null);
        var selectedBuyerWork = Assert.Single(all.WorkItems, item => item.DetailUrl == $"#maintenance/{completedWithDetails.Id}");
        Assert.Equal(separateBuyer.Id, selectedBuyerWork.ClientId);
        Assert.Equal(separateBuyer.Name, selectedBuyerWork.ClientName);
        var buyerlessWork = Assert.Single(all.WorkItems, item => item.DetailUrl == $"#maintenance/{completedWithoutDetails.Id}");
        Assert.Null(buyerlessWork.ClientId);
        Assert.Null(buyerlessWork.ClientName);
        Assert.Contains(all.VehicleOptions, option => option.DisplayName == "Каталожная марка Каталожная модель" && option.Subtitle?.Contains("В456ВВ154", StringComparison.Ordinal) == true);
        Assert.Equal(3, all.VehicleOptions.Count);
        Assert.DoesNotContain(all.VehicleOptions, option => option.Id == archivedVehicle.Id || option.Id == deletedVehicle.Id);
        Assert.Equal(2, all.ServedClientsCount);
        Assert.Equal("maintenance", maintenanceOnly.SelectedKind);
        Assert.Equal("works", maintenanceOnly.SelectedTab);
        Assert.DoesNotContain(maintenanceOnly.WorkItems, item => item.Kind == "defect");
        Assert.DoesNotContain(maintenanceOnly.HistoryItems, item => item.Kind == "defect");
        Assert.Equal("defect", defectsForSecondVehicle.SelectedKind);
        Assert.All(defectsForSecondVehicle.WorkItems, item => Assert.Equal(secondVehicle.Id, Guid.Parse(item.VehicleUrl![("#vehicles/".Length)..])));
        var defectOwner = Assert.Single(defectsForSecondVehicle.WorkItems, item => item.Title == "Неисправность закрыта");
        Assert.Equal(client.Id, defectOwner.ClientId);
        Assert.Equal(client.Name, defectOwner.ClientName);
        Assert.All(defectsForSecondVehicle.HistoryItems.Where(item => item.Kind == "defect"), item => Assert.Contains(secondVehicle.RegistrationPlate!, item.VehicleSubtitle));
        Assert.Equal(string.Empty, unknownKind.SelectedKind);
        Assert.Equal("history", unknownKind.SelectedTab);
        Assert.DoesNotContain(unknownKind.WorkItems, item => item.Kind == "defect");
    }

    [Fact]
    public async Task ArchiveAndSoftDeleteKeepExistingHistoryAndActiveSelectionRules()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization { Name = "Архив журнала", OwnerUser = owner };
        var client = new OrganizationClient { Organization = organization, Name = "Клиент архива" };
        var archived = CreateVehicle(owner, organization, client, "А111АА154");
        archived.IsArchived = true;
        var deleted = CreateVehicle(owner, organization, client, "У222УУ154");
        deleted.IsDeleted = true;
        var date = UtcDate(2026, 10, 10);
        var archivedCompleted = CreateCompletedMaintenance(owner, archived, date);
        var deletedCompleted = CreateCompletedMaintenance(owner, deleted, date);
        var archivedPlanned = CreateCompletedMaintenance(owner, archived, date);
        archivedPlanned.Status = MaintenanceStatus.Planned;
        archivedPlanned.CompletedDate = null;
        var deletedPlanned = CreateCompletedMaintenance(owner, deleted, date);
        deletedPlanned.Status = MaintenanceStatus.Planned;
        deletedPlanned.CompletedDate = null;
        context.Maintenance.AddRange(archivedCompleted, deletedCompleted, archivedPlanned, deletedPlanned);
        await context.SaveChangesAsync();
        var history = await new OrganizationWorkJournalQueryService(context)
            .GetWorkJournalAsync(organization.Id, date, date, null, "maintenance", "works", false);

        Assert.Empty(history.VehicleOptions);
        Assert.Equal(new[] { archivedCompleted.Id, deletedCompleted.Id }.Order(),
            history.WorkItems.Select(item => Guid.Parse(item.DetailUrl!["#maintenance/".Length..])).Order());
        var maintenanceJournal = await new OrganizationMaintenanceJournalQueryService(context)
            .GetMaintenanceJournalAsync(organization.Id, MaintenanceStatus.Completed, null, null, null, 1, 25, true);
        Assert.Equal(archivedCompleted.Id, Assert.Single(maintenanceJournal.Items).Id);
        var feedService = new OrganizationWorkspaceMaintenanceQueryService(context);
        var feed = await feedService.GetMaintenancesAsync(organization.Id, date, date, 1, 25);
        Assert.Equal(archivedPlanned.Id, Assert.Single(feed.Items).Id);
        var calendar = await feedService.GetMaintenanceCalendarAsync(organization.Id, date, date, includeAllStatuses: true);
        Assert.Equal(new[] { archivedCompleted.Id, archivedPlanned.Id }.Order(), calendar.Select(item => item.Id).Order());
        var empty = await new OrganizationWorkJournalQueryService(context)
            .GetWorkJournalAsync(Guid.NewGuid(), date, date, null, null, null, true);
        Assert.Empty(empty.WorkItems);
        Assert.Empty(empty.HistoryItems);
        Assert.Empty(empty.VehicleOptions);
    }

    private static Vehicle CreateVehicle(
        ApplicationUser user,
        Organization organization,
        OrganizationClient client,
        string registrationPlate) =>
        new()
        {
            User = user,
            Organization = organization,
            OrganizationClient = client,
            Name = "Автомобиль клиента",
            ManualBrandName = "BMW",
            ManualModelName = "X5",
            RegistrationPlate = registrationPlate,
            DateTimeCreation = UtcDate(2026, 10, 1)
        };

    private static MaintenanceEntity CreateCompletedMaintenance(ApplicationUser user, Vehicle vehicle, DateTime date) =>
        new()
        {
            User = user,
            Vehicle = vehicle,
            Type = MaintenanceType.Other,
            Status = MaintenanceStatus.Completed,
            Date = date,
            PlannedDate = date,
            CompletedDate = date,
            DateTimeCreation = date,
            Comment = "Комментарий к работе"
        };

    private static VehicleDefect CreateDefect(
        ApplicationUser user,
        Organization organization,
        Vehicle vehicle,
        string title,
        DateTime createdAt) =>
        new()
        {
            User = user,
            Organization = organization,
            Vehicle = vehicle,
            Title = title,
            CreatedAt = createdAt
        };

    private static DateTime UtcDate(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
