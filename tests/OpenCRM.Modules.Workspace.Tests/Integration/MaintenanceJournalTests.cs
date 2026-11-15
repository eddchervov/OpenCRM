using OpenCRM.Testing;
using OpenCRM.Modules.Workspace.Workday;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Modules.Workspace.Access;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Workspace.Shell;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class MaintenanceJournalTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData(0, "no_payment_required")]
    [InlineData(200, "overpaid")]
    public async Task CancelledOrderHasNoPayableAmountAndPreservesPayment(decimal paid, string paymentStatus)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        var work = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Cancelled, 0);
        work.Details = [new() { Name = "Работа", Price = 500m }];
        work.PaidAmount = paid;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = new OrganizationMaintenanceJournalQueryService(context);

        var result = await service.GetMaintenanceJournalAsync(
            vehicle.Organization!.Id, null, null, null, null, 1, 25, true, canViewPayments: true);
        var item = Assert.Single(result.Items);

        Assert.Equal(0m, item.Amount);
        Assert.Equal(paid, item.PaidAmount);
        Assert.Equal(paymentStatus, item.PaymentStatus);
    }

    [Fact]
    public async Task EmployeeFilterIncludesGeneralAndServiceAssignmentsAndReturnsOrganizationHistoryOptions()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        var primaryPerformer = new OrganizationEmployee
        {
            Organization = vehicle.Organization!,
            Name = "Исполнитель записи"
        };
        var servicePerformer = new OrganizationEmployee
        {
            Organization = vehicle.Organization!,
            Name = "Исполнитель услуги",
            IsActive = false
        };
        var otherOrganizationVehicle = MaintenanceJournalTests.CreateVehicle();
        var otherOrganizationPerformer = new OrganizationEmployee
        {
            Organization = otherOrganizationVehicle.Organization!,
            Name = "Чужой исполнитель"
        };
        var work = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Completed, 0);
        work.EmployeeId = primaryPerformer.Id;
        work.Performer = primaryPerformer.Name;
        work.PlannedDate = work.Date.AddDays(-4);
        work.CompletedDate = work.Date.AddDays(2);
        work.WorkActOrganizationId = vehicle.Organization!.Id;
        work.WorkActNumber = 142;
        work.PaidAmount = 450;
        work.DiscountAmount = 10;
        work.Details =
        [
            new() { Name = "Полировка", EmployeeId = servicePerformer.Id, EmployeeName = servicePerformer.Name, Price = 500 },
            new() { Name = "Мойка", Price = 300 }
        ];
        var details = work.Details;
        details[0].Position = 2;
        details[1].Position = 1;
        context.OrganizationEmployees.AddRange(primaryPerformer, servicePerformer, otherOrganizationPerformer);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = new OrganizationMaintenanceJournalQueryService(context);

        var byGeneralPerformer = await service.GetMaintenanceJournalAsync(
            vehicle.Organization.Id, null, null, null, null, 1, 25, true, employeeId: primaryPerformer.Id);
        var byServicePerformer = await service.GetMaintenanceJournalAsync(
            vehicle.Organization.Id, null, null, null, null, 1, 25, true, employeeId: servicePerformer.Id);
        var byOtherOrganization = await service.GetMaintenanceJournalAsync(
            vehicle.Organization.Id, null, null, null, null, 1, 25, true, employeeId: otherOrganizationPerformer.Id);

        var item = Assert.Single(byServicePerformer.Items);
        Assert.Single(byGeneralPerformer.Items);
        Assert.Empty(byOtherOrganization.Items);
        Assert.Equal(new[] { "Мойка", "Полировка" }, item.Services);
        Assert.Equal(new decimal?[] { 300m, 500m }, item.ServiceLines.Select(line => line.Amount));
        Assert.Equal(servicePerformer.Name, item.ServiceLines[1].EmployeeName);
        Assert.Equal(new[] { servicePerformer.Name, primaryPerformer.Name }, item.PerformerNames);
        Assert.Equal(new HashSet<Guid> { primaryPerformer.Id, servicePerformer.Id }, byServicePerformer.Performers.Select(performer => performer.Id).ToHashSet());
        Assert.DoesNotContain(byServicePerformer.Performers, performer => performer.Id == otherOrganizationPerformer.Id);
        Assert.Null(item.PaidAmount);
        Assert.Equal(790m, item.Amount);
        Assert.Equal(work.CompletedDate!.Value, item.Date);
        Assert.Equal(142, item.WorkActNumber);

        var withPayments = await service.GetMaintenanceJournalAsync(
            vehicle.Organization.Id, null, null, null, null, 1, 25, true, employeeId: servicePerformer.Id, canViewPayments: true);
        Assert.Equal(450m, Assert.Single(withPayments.Items).PaidAmount);
    }

    [Fact]
    public async Task StatusAndPaginationIncludeOldWorkButExcludeDeletedAndOtherOrganizations()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        var works = Enumerable.Range(0, 31).Select(index => MaintenanceJournalTests.CreateWork(vehicle,
            index == 0 ? MaintenanceStatus.Planned : MaintenanceStatus.Completed, index)).ToList();
        var deleted = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Completed, 40);
        deleted.IsDeleted = true;
        var deletedVehicle = MaintenanceJournalTests.CreateVehicle();
        deletedVehicle.Organization = vehicle.Organization;
        deletedVehicle.IsDeleted = true;
        context.Maintenance.AddRange(works);
        context.Maintenance.AddRange(deleted,
            MaintenanceJournalTests.CreateWork(deletedVehicle, MaintenanceStatus.Completed, 50),
            MaintenanceJournalTests.CreateWork(MaintenanceJournalTests.CreateVehicle(), MaintenanceStatus.Completed, 50));
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var first = await service.GetMaintenanceJournalAsync(vehicle.Organization!.Id, MaintenanceStatus.Completed, null, null, null, 1, 25, true);
        var second = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, MaintenanceStatus.Completed, null, null, null, 2, 25, true);
        Assert.Equal(30, first.TotalCount);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(works.Skip(1).Reverse().Select(item => item.Id), first.Items.Concat(second.Items).Select(item => item.Id));
        var all = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, null, null, null, null, 1, 1000, true);
        Assert.Equal(31, all.TotalCount);
        Assert.Equal(100, all.PageSize);
        var outOfRange = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, null, null, null, null, int.MaxValue, 25, true);
        Assert.Equal(2, outOfRange.Page);
    }

    [Theory]
    [InlineData("химчистка")]
    [InlineData("проверочный клиент")]
    [InlineData("BMW X3")]
    [InlineData("А123АА154")]
    [InlineData("50%_скидка")]
    public async Task SearchFindsRealFieldsBeforePaginationAndKeepsOneRowPerRecord(string search)
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        var work = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Completed, 0);
        work.Details = [new() { Name = "Химчистка 50%_скидка", Price = 2000 }, new() { Name = "Полировка", Price = 1000 },
            new() { Name = "Не услуга", MaterialName = "Керамика", Price = 500 },
            new() { Name = "Складская позиция", MaterialCatalogItemId = Guid.NewGuid() },
            new() { Name = "Расход", MaterialQuantity = 1 }, new() { Name = " " }];
        work.DiscountAmount = 250;
        var otherVehicle = MaintenanceJournalTests.CreateVehicle();
        otherVehicle.Organization = vehicle.Organization;
        otherVehicle.ManualBrandName = "Lada";
        otherVehicle.ManualModelName = "Vesta";
        otherVehicle.RegistrationPlate = "В456ВВ154";
        otherVehicle.OrganizationClient = null;
        context.Maintenance.Add(work);
        context.Maintenance.AddRange(Enumerable.Range(1, 35).Select(index => MaintenanceJournalTests.CreateWork(otherVehicle, MaintenanceStatus.Planned, index)));
        context.Maintenance.Add(MaintenanceJournalTests.CreateWork(MaintenanceJournalTests.CreateVehicle(), MaintenanceStatus.Completed, 40));
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var result = await service.GetMaintenanceJournalAsync(vehicle.Organization!.Id, null, null, null, search, 1, 10, true);
        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(work.Id, item.Id);
        Assert.Equal(new[] { "Химчистка 50%_скидка", "Полировка" }, item.Services);
        Assert.Equal(3250m, item.Amount);
        Assert.Equal("BMW X3", item.VehicleDisplayName);
        foreach (var notFound in new[] { "Керамика", "Не услуга", "50XXскидка", "' OR 1=1 --" })
        {
            var empty = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, null, null, null, notFound, 1, 10, true);
            Assert.Empty(empty.Items);
        }
    }

    [Fact]
    public async Task SameDayIsSortedByPlannedTimeAndCatalogNamesAreSearchable()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        vehicle.Brand = new Brand { Name = "Каталожная марка" };
        vehicle.Model = new Model { Name = "Каталожная модель", Brand = vehicle.Brand };
        vehicle.ManualBrandName = null;
        vehicle.ManualModelName = null;
        vehicle.OrganizationClient = null;
        var early = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Planned, 0);
        early.PlannedStartTime = TimeSpan.FromHours(9);
        var late = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Planned, 0);
        late.PlannedStartTime = TimeSpan.FromHours(18);
        late.Details = [];
        context.Maintenance.AddRange(early, late);
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var result = await service.GetMaintenanceJournalAsync(vehicle.Organization!.Id, null, null, null, "Каталожная модель", 1, 25, true);
        Assert.Equal(new[] { late.Id, early.Id }, result.Items.Select(item => item.Id));
        Assert.Empty(result.Items[0].Services);
        Assert.Equal("Каталожная марка Каталожная модель", result.Items[0].VehicleDisplayName);
    }

    [Fact]
    public async Task ClientPermissionRestrictsBothDisplayAndSearch()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        context.Maintenance.Add(MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Planned, 0));
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var result = await service.GetMaintenanceJournalAsync(vehicle.Organization!.Id, null, null, null, "BMW", 1, 25, false);
        Assert.Null(Assert.Single(result.Items).ClientName);
        var hidden = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, null, null, null, "Проверочный клиент", 1, 25, false);
        Assert.Empty(hidden.Items);
    }

    [Fact]
    public async Task BuyerSearchAndDisplayRequireAnAssociatedBuyerAndClientPermission()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        var buyer = new OrganizationClient { Organization = vehicle.Organization!, Name = "Покупатель по заказу" };
        var selectedBuyerOrder = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Completed, 0);
        selectedBuyerOrder.BuyerClientId = buyer.Id;
        selectedBuyerOrder.BuyerName = buyer.Name;
        var buyerlessOrder = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Completed, 1);
        buyerlessOrder.BuyerName = "Снимок без покупателя";
        context.OrganizationClients.Add(buyer);
        context.Maintenance.AddRange(selectedBuyerOrder, buyerlessOrder);
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var visibleBuyer = await service.GetMaintenanceJournalAsync(
            vehicle.Organization!.Id, null, null, null, "Покупатель по заказу", 1, 25, true);
        var hiddenBuyer = await service.GetMaintenanceJournalAsync(
            vehicle.Organization!.Id, null, null, null, "Покупатель по заказу", 1, 25, false);
        var snapshotWithoutBuyer = await service.GetMaintenanceJournalAsync(
            vehicle.Organization!.Id, null, null, null, "Снимок без покупателя", 1, 25, true);
        var hiddenItems = await service.GetMaintenanceJournalAsync(
            vehicle.Organization!.Id, null, null, null, null, 1, 25, false);
        var visibleOwnerSearch = await service.GetMaintenanceJournalAsync(
            vehicle.Organization!.Id, null, null, null, "Проверочный клиент", 1, 25, true);

        var visibleOrder = Assert.Single(visibleBuyer.Items);
        Assert.Equal("Покупатель по заказу", visibleOrder.ClientName);
        Assert.Empty(hiddenBuyer.Items);
        Assert.Empty(snapshotWithoutBuyer.Items);
        Assert.All(hiddenItems.Items, item => Assert.Null(item.ClientName));
        Assert.Equal(2, visibleOwnerSearch.Items.Count);
        Assert.Equal("Покупатель по заказу", visibleOwnerSearch.Items.Single(item => item.Id == selectedBuyerOrder.Id).ClientName);
        Assert.Null(visibleOwnerSearch.Items.Single(item => item.Id == buyerlessOrder.Id).ClientName);
    }

    [Fact]
    public async Task PeriodUsesActualCompletionForCompletedAndPlanForOtherStatuses()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenanceJournalTests.CreateVehicle();
        var completed = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Completed, 0);
        completed.CompletedDate = completed.Date.AddDays(10).AddHours(23).AddMinutes(59);
        var planned = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Planned, 10);
        var active = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.InProgress, 10);
        var cancelled = MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Cancelled, 10);
        context.Maintenance.AddRange(completed, planned, active, cancelled,
            MaintenanceJournalTests.CreateWork(vehicle, MaintenanceStatus.Planned, 11));
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));
        var day = DateOnly.FromDateTime(planned.Date);

        var result = await service.GetMaintenanceJournalAsync(vehicle.Organization!.Id, null, day, day, null, 1, 25, true);
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(completed.Id, result.Items[0].Id);
        Assert.All(result.Items.Where(item => item.Status != MaintenanceStatus.Completed), item => Assert.Null(item.CompletedDate));
        foreach (var status in Enum.GetValues<MaintenanceStatus>())
        {
            var filtered = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, status, day, day, null, 1, 25, true);
            Assert.Equal(status, Assert.Single(filtered.Items).Status);
        }

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMaintenanceJournalAsync(vehicle.Organization.Id, null, day.AddDays(1), day, null, 1, 25, true));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMaintenanceJournalAsync(
            vehicle.Organization.Id, null, null, DateOnly.MaxValue, null, 1, 25, true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetMaintenanceJournalAsync(vehicle.Organization.Id, (MaintenanceStatus)99, null, null, null, 1, 25, true));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMaintenanceJournalAsync(
            vehicle.Organization.Id, null, null, null, null, 1, 25, true, handover: "unknown"));
    }

    private static Vehicle CreateVehicle()
    {
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Проверка журнала услуг",
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };

        return new Vehicle
        {
            User = user,
            Organization = organization,
            ManualBrandName = "BMW",
            ManualModelName = "X3",
            RegistrationPlate = "А123АА154",
            OrganizationClient = new OrganizationClient { Organization = organization, Name = "Проверочный клиент" }
        };
    }

    private static MaintenanceEntity CreateWork(Vehicle vehicle, MaintenanceStatus status, int day)
    {
        var date = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day);

        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = vehicle.User,
            Type = MaintenanceType.Other,
            Status = status,
            Date = date,
            PlannedDate = date,
            CompletedDate = status == MaintenanceStatus.Completed ? date : null,
            DateTimeCreation = date,
            Details = [new() { Name = "Обслуживание" }]
        };
    }
}
