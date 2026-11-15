using OpenCRM.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceReadCoverageTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task GetByIdMapsFinanceDetailsPhotosAndOnlyActiveLinkedDefects()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        var maintenance = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 11, 16, 30, 0, DateTimeKind.Utc));
        maintenance.PlannedDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        maintenance.CompletedDate = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        maintenance.IntakeNotes = "Вмятина на двери";
        maintenance.IntakeMileage = 120_000;
        maintenance.IntakeCompleteness = "Ключи";
        maintenance.IntakeVisibleDamage = "Царапина";
        maintenance.IntakeRecordedAt = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
        maintenance.DiscountAmount = 10;
        maintenance.PaidAmount = 50;
        maintenance.MaterialCostAmount = 30;
        maintenance.LaborCostAmount = 5;
        maintenance.BuyerName = "ООО Тест";
        maintenance.BuyerPartyTypeCode = "company";
        maintenance.BuyerInn = "1234567890";
        maintenance.BuyerKpp = "123456789";
        maintenance.BuyerLegalAddress = "Тестовый адрес";
        maintenance.Details =
        [
            new MaintenanceDetail
            {
                Name = "Полировка",
                Description = "Кузов",
                Price = 100.005m,
                Quantity = 2,
                EmployeeName = "Мастер",
                MaterialName = "Паста",
                MaterialQuantity = 2,
                MaterialUnit = "мл",
                MaterialUnitCost = 10,
                MaterialTotalCost = 20
            },
            new MaintenanceDetail { Name = "Мойка", Price = 25.555m }
        ];
        var activeDefect = MaintenanceReadCoverageTests.CreateDefect(vehicle, "Активная неисправность");
        var deletedDefect = MaintenanceReadCoverageTests.CreateDefect(vehicle, "Удалённая неисправность");
        deletedDefect.IsDeleted = true;
        context.Maintenance.Add(maintenance);
        context.VehicleDefects.AddRange(activeDefect, deletedDefect);
        context.MaintenanceDefects.AddRange(
            MaintenanceReadCoverageTests.CreateDefectLink(maintenance, activeDefect, vehicle),
            MaintenanceReadCoverageTests.CreateDefectLink(maintenance, deletedDefect, vehicle));
        var activePhoto = MaintenanceReadCoverageTests.CreatePhoto(vehicle, maintenance, isDeleted: false);
        var deletedPhoto = MaintenanceReadCoverageTests.CreatePhoto(vehicle, maintenance, isDeleted: true);
        context.MaintenancePhotos.AddRange(activePhoto, deletedPhoto);
        await context.SaveChangesAsync();
        var access = MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id);
        var service = MaintenanceReadCoverageTests.CreateQueryService(context, access.Object);

        var result = await service.GetById(vehicle.User.Id, vehicle.Organization.Id, maintenance.Id);

        Assert.Equal(maintenance.Id, result.Id);
        Assert.Equal(vehicle.Id, result.VehicleId);
        Assert.Equal("ООО Тест", result.BuyerName);
        Assert.Equal("company", result.BuyerPartyTypeCode);
        Assert.Equal("1234567890", result.BuyerInn);
        Assert.Equal("123456789", result.BuyerKpp);
        Assert.Equal("Тестовый адрес", result.BuyerLegalAddress);
        Assert.Equal(125.56m, result.GrossAmount);
        Assert.Equal(65.56m, result.DebtAmount);
        Assert.Equal(80.56m, result.ProfitAmount);
        Assert.Equal("partial", result.PaymentStatus);
        Assert.Equal(new DateTime(2026, 10, 11), result.Date);
        Assert.Equal(DateTimeKind.Unspecified, result.Date.Kind);
        Assert.Equal<DateTime?>(new DateTime(2026, 10, 12), result.PlannedDate);
        Assert.Equal<DateTime?>(new DateTime(2026, 10, 13), result.CompletedDate);
        Assert.Equal("Вмятина на двери", result.IntakeNotes);
        Assert.Equal(120_000, result.IntakeMileage);
        Assert.Equal("Ключи", result.IntakeCompleteness);
        Assert.Equal("Царапина", result.IntakeVisibleDamage);
        Assert.Equal(new[] { activePhoto.Id }, result.PhotoIds);
        var detail = Assert.Single(result.MaintenanceDetails, item => item.Name == "Полировка");
        Assert.Equal("Кузов", detail.Description);
        Assert.Equal(2, detail.Quantity);
        Assert.Equal("Мастер", detail.EmployeeName);
        Assert.Equal("Паста", detail.MaterialName);
        Assert.Equal(2m, detail.MaterialQuantity);
        Assert.Equal("мл", detail.MaterialUnit);
        Assert.Equal(10m, detail.MaterialUnitCost);
        Assert.Equal(20m, detail.MaterialTotalCost);
        var linkedDefect = Assert.Single(result.LinkedDefects);
        Assert.Equal(activeDefect.Id, linkedDefect.Id);
        Assert.Equal("Активная неисправность", linkedDefect.Title);
        access.VerifyAll();
    }

    [Fact]
    public async Task PersonalReadIsOwnerScopedAndRejectsDeletedOrForeignMaintenance()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: false);
        var visible = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        var deleted = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));
        deleted.IsDeleted = true;
        var foreignVehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: false);
        var foreign = MaintenanceReadCoverageTests.CreateMaintenance(foreignVehicle, new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc));
        context.Maintenance.AddRange(visible, deleted, foreign);
        await context.SaveChangesAsync();
        var service = MaintenanceReadCoverageTests.CreateQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        var result = await service.GetById(vehicle.User.Id, visible.Id);
        var page = await service.Get(vehicle.User.Id, vehicle.Id, 1, 10, "date_desc");
        var all = await service.Get(vehicle.User.Id, vehicle.Id);
        var allRows = all.ToList();
        var deletedPage = await service.GetDeleted(vehicle.User.Id, vehicle.Id);

        Assert.Equal(visible.Id, result.Id);
        Assert.Empty(result.LinkedDefects);
        Assert.Equal(visible.Id, Assert.Single(page.Items).Id);
        var allRow = Assert.Single(allRows);
        Assert.Equal(visible.Id, allRow.Id);
        Assert.Equal("Работа", Assert.Single(allRow.MaintenanceDetails).Name);
        Assert.Equal(deleted.Id, Assert.Single(deletedPage.Items).Id);
        Assert.Empty(await service.GetHistory(vehicle.User.Id, visible.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetById(vehicle.User.Id, deleted.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetById(vehicle.User.Id, foreign.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetHistory(vehicle.User.Id, deleted.Id));
    }

    [Fact]
    public async Task AnalyticsReadRejectsMissingWorkspaceVehicle()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var access = MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id);
        var service = MaintenanceReadCoverageTests.CreateQueryService(context, access.Object);

        await Assert.ThrowsAsync<Exception>(() => service.Get(
            vehicle.User.Id, vehicle.Organization.Id, Guid.NewGuid()));

        access.VerifyAll();
    }

    [Fact]
    public async Task PagedReadAppliesWorkspaceFiltersSortsAndCalculatesPagination()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        var first = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        first.Type = MaintenanceType.Repair;
        first.Status = MaintenanceStatus.Planned;
        first.Mileage = 20;
        first.Details =
        [
            new MaintenanceDetail
            {
                Name = "Полировка",
                Description = "Капот",
                Price = 1000,
                VatRateCode = "without_vat",
                Quantity = 2,
                EmployeeName = "Мастер",
                MaterialName = "Паста",
                MaterialQuantity = 3,
                MaterialUnit = "мл",
                MaterialUnitCost = 10,
                MaterialTotalCost = 30
            }
        ];
        var second = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc));
        second.Type = MaintenanceType.Repair;
        second.Status = MaintenanceStatus.Planned;
        second.Mileage = null;
        var third = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        third.Type = MaintenanceType.Repair;
        third.Status = MaintenanceStatus.Planned;
        third.Mileage = 50;
        var excluded = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
        excluded.Type = MaintenanceType.Maintenance;
        excluded.Status = MaintenanceStatus.Completed;
        var deleted = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc));
        deleted.Type = MaintenanceType.Repair;
        deleted.Status = MaintenanceStatus.Planned;
        deleted.IsDeleted = true;
        var linkedDefect = MaintenanceReadCoverageTests.CreateDefect(vehicle, "Неисправность записи");
        context.Maintenance.AddRange(first, second, third, excluded, deleted);
        context.VehicleDefects.Add(linkedDefect);
        context.MaintenanceDefects.Add(MaintenanceReadCoverageTests.CreateDefectLink(first, linkedDefect, vehicle));
        await context.SaveChangesAsync();
        var service = MaintenanceReadCoverageTests.CreateQueryService(context,
            MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id).Object);

        var page = await service.Get(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, 1, 2, "date_asc",
            (int)MaintenanceType.Repair, (int)MaintenanceStatus.Planned);
        var lastPage = await service.Get(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, 2, 2, "date_asc",
            (int)MaintenanceType.Repair, (int)MaintenanceStatus.Planned);
        var defaultSort = await service.Get(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, 1, 10, "unknown");

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { first.Id, third.Id }, page.Items.Select(item => item.Id));
        Assert.True(page.HasMore);
        Assert.Equal(2, lastPage.Page);
        Assert.False(lastPage.HasMore);
        Assert.Equal(second.Id, Assert.Single(lastPage.Items).Id);
        Assert.Equal(new[] { excluded.Id, second.Id, third.Id, first.Id }, defaultSort.Items.Select(item => item.Id));
        var listedWork = Assert.Single(page.Items, item => item.Id == first.Id);
        Assert.All(page.Items.Where(item => item.Id != first.Id), item => Assert.Empty(item.LinkedDefects));
        var listedDetail = Assert.Single(listedWork.MaintenanceDetails);
        Assert.Equal("Полировка", listedDetail.Name);
        Assert.Equal("Капот", listedDetail.Description);
        Assert.Equal("without_vat", listedDetail.VatRateCode);
        Assert.Equal(2, listedDetail.Quantity);
        Assert.Equal("Мастер", listedDetail.EmployeeName);
        Assert.Equal("Паста", listedDetail.MaterialName);
        Assert.Equal(3m, listedDetail.MaterialQuantity);
        Assert.Equal("мл", listedDetail.MaterialUnit);
        Assert.Equal(10m, listedDetail.MaterialUnitCost);
        Assert.Equal(30m, listedDetail.MaterialTotalCost);
        Assert.Equal(linkedDefect.Id, Assert.Single(listedWork.LinkedDefects).Id);
    }

    [Fact]
    public async Task UnpagedAndDeletedReadsKeepTheirOwnVisibilityAndPhotoRules()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        var active = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        var deleted = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));
        deleted.IsDeleted = true;
        context.Maintenance.AddRange(active, deleted);
        var activePhoto = MaintenanceReadCoverageTests.CreatePhoto(vehicle, active, isDeleted: false);
        context.MaintenancePhotos.AddRange(
            activePhoto,
            MaintenanceReadCoverageTests.CreatePhoto(vehicle, deleted, isDeleted: false),
            MaintenanceReadCoverageTests.CreatePhoto(vehicle, deleted, isDeleted: true));
        await context.SaveChangesAsync();
        var service = MaintenanceReadCoverageTests.CreateQueryService(context,
            MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id).Object);

        var activeRows = await service.Get(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id);
        var deletedRows = await service.GetDeleted(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, 1, 10);
        var missingVehicle = await Assert.ThrowsAsync<Exception>(() => service.Get(vehicle.User.Id, vehicle.Organization.Id, Guid.NewGuid(), 1, 10));

        var activeRow = Assert.Single(activeRows);
        Assert.Equal(active.Id, activeRow.Id);
        Assert.Equal(activePhoto.Id, Assert.Single(activeRow.PhotoIds));
        var deletedRow = Assert.Single(deletedRows.Items);
        Assert.Equal(deleted.Id, deletedRow.Id);
        Assert.Equal(2, deletedRow.PhotoIds.Count());
        Assert.Equal(1, deletedRows.TotalCount);
        Assert.False(deletedRows.HasMore);
        Assert.Contains("не найдена техника", missingVehicle.Message);
        await Assert.ThrowsAsync<Exception>(() => service.GetDeleted(
            vehicle.User.Id, vehicle.Organization.Id, Guid.NewGuid(), 1, 10));
    }

    [Fact]
    public async Task EmptyMaintenanceQueriesReturnEmptyResultsWithoutLoadingLinkedDefects()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var service = MaintenanceReadCoverageTests.CreateQueryService(context,
            MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id).Object);

        var page = await service.Get(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id, 1, 10);
        var deletedPage = await service.GetDeleted(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id);
        var all = await service.Get(vehicle.User.Id, vehicle.Organization.Id, vehicle.Id);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.False(page.HasMore);
        Assert.Empty(deletedPage.Items);
        Assert.Empty(all);
        await Assert.ThrowsAsync<Exception>(() => service.GetDeleted(
            vehicle.User.Id, vehicle.Organization.Id, Guid.NewGuid(), 1, 10));
        await Assert.ThrowsAsync<Exception>(() => service.Get(
            vehicle.User.Id, vehicle.Organization.Id, Guid.NewGuid(), 1, 10));
    }

    [Fact]
    public async Task HistoryIsChronologicalAndScopedToPersonalOwnerOrOrganization()
    {
        await using var context = fixture.CreateContext();
        var personalVehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: false);
        var personal = MaintenanceReadCoverageTests.CreateMaintenance(personalVehicle, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        var organizationVehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        var organizational = MaintenanceReadCoverageTests.CreateMaintenance(organizationVehicle, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        context.Maintenance.AddRange(personal, organizational);
        var earlier = MaintenanceReadCoverageTests.CreateHistory(personal, personalVehicle, personalVehicle.User, "created", new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        earlier.Description = "Создано";
        var later = MaintenanceReadCoverageTests.CreateHistory(personal, personalVehicle, personalVehicle.User, "updated", new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc));
        var organizationHistory = MaintenanceReadCoverageTests.CreateHistory(organizational, organizationVehicle, organizationVehicle.User, "status_changed", new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc));
        context.MaintenanceHistoryEvents.AddRange(earlier, later, organizationHistory);
        await context.SaveChangesAsync();
        var access = MaintenanceReadCoverageTests.CreateAccess(organizationVehicle.User.Id, organizationVehicle.Organization!.Id);
        var otherOrganizationId = Guid.NewGuid();
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(
                organizationVehicle.User.Id,
                otherOrganizationId,
                OrganizationPermissionCodes.MaintenanceView))
            .Returns(Task.CompletedTask);
        var service = MaintenanceReadCoverageTests.CreateQueryService(context, access.Object);

        var personalHistory = await service.GetHistory(personalVehicle.User.Id, personal.Id);
        var organizationHistoryRows = await service.GetHistory(
            organizationVehicle.User.Id, organizationVehicle.Organization.Id, organizational.Id);

        Assert.Equal(new[] { later.Id, earlier.Id }, personalHistory.Select(item => item.Id));
        Assert.Equal("updated", personalHistory[0].EventType);
        Assert.Equal("Создано", personalHistory[1].Description);
        Assert.Equal(organizationHistory.Id, Assert.Single(organizationHistoryRows).Id);
        await Assert.ThrowsAsync<Exception>(() => service.GetHistory("foreign-user", personal.Id));
        await Assert.ThrowsAsync<Exception>(() => service.GetHistory(
            organizationVehicle.User.Id, otherOrganizationId, organizational.Id));
        access.VerifyAll();
    }

    [Fact]
    public async Task HistoryMarksCreatedEventWithAccessibleInquiryOrigin()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        vehicle.Organization!.ShiftTimeZoneId = "Asia/Novosibirsk";
        var maintenance = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        context.Maintenance.Add(maintenance);
        var created = MaintenanceReadCoverageTests.CreateHistory(
            maintenance, vehicle, vehicle.User, "created", new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        var updated = MaintenanceReadCoverageTests.CreateHistory(
            maintenance, vehicle, vehicle.User, "updated", new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc));
        context.MaintenanceHistoryEvents.AddRange(created, updated);
        await context.SaveChangesAsync();
        var inquiryId = Guid.NewGuid();
        var originReader = new Mock<IOrganizationInquiryOriginReader>();
        originReader.Setup(reader => reader.FindSourceAsync(vehicle.User.Id, vehicle.Organization!.Id, maintenance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationInquiryOriginDto { InquiryId = inquiryId });
        var access = MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id);
        var service = MaintenanceReadCoverageTests.CreateQueryService(context, access.Object, originReader.Object);

        var history = await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, maintenance.Id);

        Assert.Equal(2, history.Count);
        var createdEvent = Assert.Single(history, item => item.EventType == "created");
        Assert.Equal("Запись создана из обращения", createdEvent.Title);
        Assert.Equal(inquiryId, createdEvent.SourceInquiryId);
        Assert.Equal("Asia/Novosibirsk", createdEvent.TimeZoneId);
        Assert.Null(Assert.Single(history, item => item.EventType == "updated").SourceInquiryId);
        originReader.VerifyAll();
    }

    [Fact]
    public async Task HistoryKeepsOrdinaryCreatedEventWithoutInquiryOrigin()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        var maintenance = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        context.Maintenance.Add(maintenance);
        var created = MaintenanceReadCoverageTests.CreateHistory(
            maintenance, vehicle, vehicle.User, "created", new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        created.Title = "Обслуживание создано";
        context.MaintenanceHistoryEvents.Add(created);
        await context.SaveChangesAsync();
        var access = MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id);
        var service = MaintenanceReadCoverageTests.CreateQueryService(context, access.Object);

        var history = await service.GetHistory(vehicle.User.Id, vehicle.Organization.Id, maintenance.Id);

        var createdEvent = Assert.Single(history);
        Assert.Equal("Обслуживание создано", createdEvent.Title);
        Assert.Null(createdEvent.SourceInquiryId);
    }

    [Fact]
    public async Task SlotReadClipsAnIntervalAcrossUtcMidnightAndChecksWorkspacePermissionFirst()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceReadCoverageTests.CreateVehicle(organization: true);
        var day = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var maintenance = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, day.AddDays(-1));
        maintenance.Status = MaintenanceStatus.Planned;
        maintenance.PlannedDate = day.AddDays(-1);
        maintenance.PlannedStartTime = TimeSpan.FromHours(23.5);
        maintenance.PlannedEndDate = day;
        maintenance.PlannedEndTime = TimeSpan.FromMinutes(30);
        maintenance.Location = "Пост 1";
        var namedVehicle = new Vehicle { User = vehicle.User, Organization = vehicle.Organization, Name = "Техника без номера" };
        var namedVehicleMaintenance = MaintenanceReadCoverageTests.CreateMaintenance(namedVehicle, day);
        namedVehicleMaintenance.Status = MaintenanceStatus.Planned;
        namedVehicleMaintenance.Location = "Пост 1";
        namedVehicleMaintenance.PlannedStartTime = TimeSpan.FromHours(8);
        namedVehicleMaintenance.PlannedEndTime = TimeSpan.FromHours(9);
        namedVehicleMaintenance.Details = [new MaintenanceDetail { Name = "Расход материала", MaterialName = "Масло" }];
        var unnamedVehicle = new Vehicle { User = vehicle.User, Organization = vehicle.Organization };
        var unnamedVehicleMaintenance = MaintenanceReadCoverageTests.CreateMaintenance(unnamedVehicle, day);
        unnamedVehicleMaintenance.Status = MaintenanceStatus.Planned;
        unnamedVehicleMaintenance.Location = "Пост 1";
        unnamedVehicleMaintenance.PlannedStartTime = TimeSpan.FromHours(9);
        unnamedVehicleMaintenance.PlannedEndTime = TimeSpan.FromHours(10);
        unnamedVehicleMaintenance.Details = [];
        var endingAtMidnight = MaintenanceReadCoverageTests.CreateMaintenance(vehicle, day.AddDays(-1));
        endingAtMidnight.Status = MaintenanceStatus.Planned;
        endingAtMidnight.Location = "Пост 1";
        endingAtMidnight.PlannedDate = day.AddDays(-1);
        endingAtMidnight.PlannedStartTime = TimeSpan.FromHours(23);
        endingAtMidnight.PlannedEndDate = day;
        endingAtMidnight.PlannedEndTime = TimeSpan.Zero;
        context.Maintenance.AddRange(maintenance, namedVehicleMaintenance, unnamedVehicleMaintenance, endingAtMidnight);
        await context.SaveChangesAsync();
        var access = MaintenanceReadCoverageTests.CreateAccess(vehicle.User.Id, vehicle.Organization!.Id);
        var service = new MaintenanceSlotQueryService(context, access.Object);

        var slots = await service.GetOccupiedSlots(vehicle.User.Id, vehicle.Organization.Id,
            DateOnly.FromDateTime(day), " пост 1 ");

        Assert.Equal(3, slots.Count);
        var slot = Assert.Single(slots, item => item.Id == maintenance.Id);
        Assert.Equal(0, slot.StartMinute);
        Assert.Equal(30, slot.EndMinute);
        Assert.Equal(day.AddDays(-1).AddHours(23.5), slot.StartAt);
        Assert.Equal(day.AddMinutes(30), slot.EndAt);
        var namedSlot = Assert.Single(slots, item => item.Id == namedVehicleMaintenance.Id);
        Assert.Equal("Техника без номера", namedSlot.Vehicle);
        Assert.Null(namedSlot.Service);
        var unnamedSlot = Assert.Single(slots, item => item.Id == unnamedVehicleMaintenance.Id);
        Assert.Equal("Автомобиль", unnamedSlot.Vehicle);
        Assert.Null(unnamedSlot.Service);
        Assert.Empty(await service.GetOccupiedSlots(vehicle.User.Id, vehicle.Organization.Id,
            DateOnly.FromDateTime(day), "   ", dateTo: DateOnly.FromDateTime(day.AddDays(-1))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOccupiedSlots(
            vehicle.User.Id, vehicle.Organization.Id, DateOnly.FromDateTime(day), "Пост 1", dateTo: DateOnly.FromDateTime(day.AddDays(-1))));
        access.VerifyAll();
    }

    private static MaintenanceQueryService CreateQueryService(
        OpenCRMContext context,
        IOrganizationSubscriptionService access,
        IOrganizationInquiryOriginReader? inquiryOriginReader = null)
    {
        return new MaintenanceQueryService(
            new MaintenanceRepository(context),
            new MaintenancePhotoRepository(context, NullLogger<MaintenancePhotoRepository>.Instance),
            new VehicleMaintenanceLookupService(new VehicleRepository(context)),
            context,
            access,
            inquiryOriginReader ?? Mock.Of<IOrganizationInquiryOriginReader>());
    }

    private static Mock<IOrganizationSubscriptionService> CreateAccess(string userId, Guid organizationId)
    {
        var access = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(
                userId,
                organizationId,
                OrganizationPermissionCodes.MaintenanceView))
            .Returns(Task.CompletedTask);

        return access;
    }

    private static Vehicle CreateVehicle(bool organization)
    {
        var user = new ApplicationUser { Email = $"maintenance-read-{Guid.NewGuid():N}@example.test", UserName = $"maintenance-read-{Guid.NewGuid():N}" };
        var vehicle = new Vehicle { User = user, RegistrationPlate = "А123АА154", Name = "Тестовый автомобиль" };
        if (organization)
        {
            vehicle.Organization = new Organization
            {
                Name = "Организация чтения",
                OwnerUser = user,
                Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
            };
        }

        return vehicle;
    }

    private static MaintenanceEntity CreateMaintenance(Vehicle vehicle, DateTime date)
    {
        return new MaintenanceEntity
        {
            User = vehicle.User,
            Vehicle = vehicle,
            Type = MaintenanceType.Maintenance,
            Status = MaintenanceStatus.Completed,
            Date = date,
            DateTimeCreation = date,
            CompletedDate = date,
            Details = [new MaintenanceDetail { Name = "Работа", Price = 100 }]
        };
    }

    private static VehicleDefect CreateDefect(Vehicle vehicle, string title)
    {
        return new VehicleDefect
        {
            User = vehicle.User,
            Vehicle = vehicle,
            Organization = vehicle.Organization!,
            Title = title
        };
    }

    private static MaintenanceDefect CreateDefectLink(MaintenanceEntity maintenance, VehicleDefect defect, Vehicle vehicle)
    {
        return new MaintenanceDefect
        {
            Maintenance = maintenance,
            VehicleDefect = defect,
            Organization = vehicle.Organization!,
            User = vehicle.User
        };
    }

    private static MaintenancePhoto CreatePhoto(Vehicle vehicle, MaintenanceEntity maintenance, bool isDeleted)
    {
        return new MaintenancePhoto
        {
            Id = Guid.NewGuid(),
            Path = $"{Guid.NewGuid():N}.jpg",
            Type = MediaType.MaintenancePhoto,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = isDeleted,
            Size = 100,
            ContentType = "image/jpeg",
            OriginalFileName = "Фото.jpg",
            User = vehicle.User,
            Maintenance = maintenance,
            Vehicle = vehicle
        };
    }

    private static MaintenanceHistoryEvent CreateHistory(
        MaintenanceEntity maintenance,
        Vehicle vehicle,
        ApplicationUser user,
        string eventType,
        DateTime createdAt)
    {
        return new MaintenanceHistoryEvent
        {
            Maintenance = maintenance,
            VehicleId = vehicle.Id,
            OrganizationId = vehicle.Organization?.Id,
            User = user,
            EventType = eventType,
            Title = eventType,
            ActorDisplayName = "Тестовый сотрудник",
            CreatedAt = createdAt
        };
    }
}
