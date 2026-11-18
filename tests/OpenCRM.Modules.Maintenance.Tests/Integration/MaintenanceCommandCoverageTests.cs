using Helpers;
using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceCommandCoverageTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task VatRatesRequireCurrentVersionAndOneValidRatePerPricedService()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        work.Details = [
            new MaintenanceDetail { Name = "Полировка", Price = 1000, VatRateCode = "without_vat" },
            new MaintenanceDetail { Name = "Вторая услуга", Price = 500, VatRateCode = "without_vat" },
            new MaintenanceDetail { Name = "Без цены", Price = null },
            new MaintenanceDetail { Name = "Материал", MaterialName = "Состав", MaterialQuantity = 1 }
        ];
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var access = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        access.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                work.User.Id,
                organizationId,
                OrganizationPermissionCodes.FinanceManage))
            .Returns(Task.CompletedTask);
        using var service = MaintenanceCommandCoverageTests.CreateService(context, access: access.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateVatRates(
            work.User.Id,
            organizationId,
            work.Id,
            new UpdateMaintenanceVatRatesDto { Version = work.Version - 1, Lines = [] }));

        var invalidRequests = new[]
        {
            new UpdateMaintenanceVatRatesDto { Version = work.Version, Lines = null! },
            new UpdateMaintenanceVatRatesDto { Version = work.Version, Lines = [] },
            new UpdateMaintenanceVatRatesDto
            {
                Version = work.Version,
                Lines = [new MaintenanceVatRateDto { LineIndex = 0, VatRateCode = "20" }, new MaintenanceVatRateDto { LineIndex = 0, VatRateCode = "10" }]
            },
            new UpdateMaintenanceVatRatesDto
            {
                Version = work.Version,
                Lines = [new MaintenanceVatRateDto { LineIndex = 0, VatRateCode = "invalid" }]
            },
            new UpdateMaintenanceVatRatesDto
            {
                Version = work.Version,
                Lines = [new MaintenanceVatRateDto { LineIndex = -1, VatRateCode = "20" }]
            },
            new UpdateMaintenanceVatRatesDto
            {
                Version = work.Version,
                Lines = [new MaintenanceVatRateDto
                {
                    LineIndex = work.Details.Count(detail => detail.Price.HasValue),
                    VatRateCode = "20"
                }]
            },
            new UpdateMaintenanceVatRatesDto
            {
                Version = work.Version,
                Lines = [
                    new MaintenanceVatRateDto { LineIndex = -1, VatRateCode = "20" },
                    new MaintenanceVatRateDto { LineIndex = 1, VatRateCode = "10" }
                ]
            },
            new UpdateMaintenanceVatRatesDto
            {
                Version = work.Version,
                Lines = [
                    new MaintenanceVatRateDto
                    {
                        LineIndex = work.Details.Count(detail => detail.Price.HasValue),
                        VatRateCode = "20"
                    },
                    new MaintenanceVatRateDto { LineIndex = 0, VatRateCode = "10" }
                ]
            },
            new UpdateMaintenanceVatRatesDto
            {
                Version = work.Version,
                Lines = [null!]
            }
        };
        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateVatRates(
                work.User.Id, organizationId, work.Id, request));
        }

        var noOp = new UpdateMaintenanceVatRatesDto
        {
            Version = work.Version,
            Lines = [
                new MaintenanceVatRateDto { LineIndex = 0, VatRateCode = "without_vat" },
                new MaintenanceVatRateDto { LineIndex = 1, VatRateCode = "without_vat" }
            ]
        };
        await service.UpdateVatRates(work.User.Id, organizationId, work.Id, noOp);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());

        work.Details.Single(detail => detail.Name == "Полировка").VatRateCode = null;
        await context.SaveChangesAsync();
        noOp.Lines[0].VatRateCode = "20";
        noOp.Lines[1].VatRateCode = "7";
        await service.UpdateVatRates(work.User.Id, organizationId, work.Id, noOp);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        Assert.Equal("20", savedWork.Details.Single(value => value.Name == "Полировка").VatRateCode);
        Assert.Equal("7", savedWork.Details.Single(value => value.Name == "Вторая услуга").VatRateCode);
        var history = await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync();
        Assert.Single(history);
        Assert.Contains("не указана", history[0].Description);
        access.VerifyAll();
    }

    [Fact]
    public async Task OrganizationCreateRejectsAnInitialPaidAmountBeforeWritingTheOrder()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var request = MaintenanceCommandCoverageTests.CreateRequest();
        request.PaidAmount = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            work.User.Id, work.Vehicle.OrganizationId, work.Vehicle.Id, request));

        Assert.False(await context.Maintenance.AnyAsync(value => value.VehicleId == work.Vehicle.Id));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task CreateEmployeeAssignmentsRequireTeamFeature(bool isFreePlan, bool hasTeam, bool assignLine)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        var employee = new OrganizationEmployee { Organization = work.Vehicle.Organization!, Name = "QA мастер", IsActive = true };
        context.Vehicles.Add(work.Vehicle);
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        using var service = MaintenanceCommandCoverageTests.CreateService(
            context, access: MaintenanceCommandCoverageTests.CreateTeamAccess(organizationId, isFreePlan, hasTeam));
        var request = MaintenanceCommandCoverageTests.CreateRequest();
        if (assignLine)
        {
            request.Details.First().EmployeeId = employee.Id;
        }
        else
        {
            request.EmployeeId = employee.Id;
        }

        if (!hasTeam)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
                work.User.Id, organizationId, work.Vehicle.Id, request));
            Assert.Contains("текущем тарифе", error.Message);
            Assert.False(await context.Maintenance.AnyAsync(value => value.VehicleId == work.Vehicle.Id));

            return;
        }

        var maintenanceId = await service.Create(work.User.Id, organizationId, work.Vehicle.Id, request);
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(assignLine ? (Guid?)null : employee.Id, saved.EmployeeId);
        Assert.Equal(assignLine ? employee.Id : (Guid?)null, saved.Details.First().EmployeeId);
    }

    [Theory]
    [InlineData(true, false, "order")]
    [InlineData(true, false, "order-line")]
    [InlineData(true, false, "services")]
    [InlineData(true, false, "assignment")]
    [InlineData(false, false, "order")]
    [InlineData(false, false, "order-line")]
    [InlineData(false, false, "services")]
    [InlineData(false, false, "assignment")]
    [InlineData(false, true, "order")]
    [InlineData(false, true, "order-line")]
    [InlineData(false, true, "services")]
    [InlineData(false, true, "assignment")]
    public async Task EveryAssignmentUpdatePathRequiresTeamFeature(bool isFreePlan, bool hasTeam, string route)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        var employee = new OrganizationEmployee { Organization = work.Vehicle.Organization!, Name = "QA мастер", IsActive = true };
        context.Maintenance.Add(work);
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        using var service = MaintenanceCommandCoverageTests.CreateService(
            context, access: MaintenanceCommandCoverageTests.CreateTeamAccess(organizationId, isFreePlan, hasTeam));
        var update = MaintenanceCommandCoverageTests.CreateUpdateRequest(work);
        if (route == "order")
        {
            update.EmployeeId = employee.Id;
        }
        else
        {
            update.Details.First().EmployeeId = employee.Id;
        }

        var updateServices = new UpdateMaintenanceServicesDto { Version = work.Version, Details = update.Details.ToList() };
        Func<Task> action = route switch
        {
            "order" or "order-line" => () => service.Update(work.User.Id, organizationId, work.Id, update),
            "services" => () => service.UpdateServices(work.User.Id, organizationId, work.Id, updateServices),
            _ => () => service.AssignEmployee(work.User.Id, organizationId, work.Id,
                new AssignMaintenanceEmployeeDto { Version = work.Version, EmployeeId = employee.Id })
        };

        if (!hasTeam)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(action);
            Assert.Contains("текущем тарифе", error.Message);
        }
        else
        {
            await action();
        }

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        Assert.Equal(hasTeam && (route is "order" or "assignment") ? employee.Id : (Guid?)null, saved.EmployeeId);
        Assert.Equal(hasTeam && (route is "order-line" or "services") ? employee.Id : (Guid?)null, saved.Details[0].EmployeeId);
        Assert.Equal(1000, saved.Details[0].Price);
    }

    [Fact]
    public async Task FreePlanCreateRejectsEmployeeAssignmentsButAllowsMaterials()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        using var service = MaintenanceCommandCoverageTests.CreateService(context, access: CreateFreeAccess(organizationId));
        var employeeRequest = MaintenanceCommandCoverageTests.CreateRequest();
        employeeRequest.Details.First().EmployeeId = Guid.NewGuid();
        var materialRequest = MaintenanceCommandCoverageTests.CreateRequest();
        materialRequest.Details = materialRequest.Details.Append(new CreateMaintenanceDetailDto
        {
            Name = "Полироль",
            MaterialName = "Полироль",
            MaterialQuantity = 1,
            MaterialUnit = "л"
        }).ToArray();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            work.User.Id, organizationId, work.Vehicle.Id, employeeRequest));
        var maintenanceId = await service.Create(work.User.Id, organizationId, work.Vehicle.Id, materialRequest);

        Assert.Equal(1, await context.Maintenance.CountAsync(value => value.VehicleId == work.Vehicle.Id));
        Assert.True(await context.MaintenanceDetails.AnyAsync(value => value.MaintenanceId == maintenanceId
            && value.MaterialName == "Полироль"));
    }

    [Fact]
    public async Task FreePlanUpdatesRejectNewEmployeeAssignmentsThroughBothUpdatePaths()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        var employee = new OrganizationEmployee { Organization = work.Vehicle.Organization!, Name = "Мастер тарифа Free" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        using var service = MaintenanceCommandCoverageTests.CreateService(context, access: CreateFreeAccess(organizationId));
        var update = MaintenanceCommandCoverageTests.CreateUpdateRequest(work);
        update.Details.First().EmployeeId = employee.Id;
        var updateServices = new UpdateMaintenanceServicesDto
        {
            Version = work.Version,
            Details = [new CreateMaintenanceDetailDto
            {
                DetailId = work.Details[0].DetailId,
                Name = work.Details[0].Name,
                Price = 1200,
                EmployeeId = employee.Id
            }]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(
            work.User.Id, organizationId, work.Id, update));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateServices(
            work.User.Id, organizationId, work.Id, updateServices));

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        var savedService = Assert.Single(savedWork.Details, detail => detail.Name == "Полировка");
        Assert.Null(savedService.EmployeeId);
        Assert.Equal(1000, savedService.Price);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestrictedTeamCanEditBaseFieldsAndRetainsHistoricalAssignmentsAndMaterials(bool isFreePlan)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        var employee = new OrganizationEmployee { Organization = work.Vehicle.Organization!, Name = "Мастер после тарифа PRO" };
        work.EmployeeId = employee.Id;
        work.Performer = employee.Name;
        work.Details[0].EmployeeId = employee.Id;
        work.Details = work.Details.Append(new MaintenanceDetail
        {
            Name = "Полироль",
            MaterialName = "Полироль",
            MaterialQuantity = 1.5m,
            MaterialUnit = "л",
            MaterialUnitCost = 20,
            MaterialTotalCost = 30
        }).ToList();
        context.Maintenance.Add(work);
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var consumption = new Mock<IMaintenanceConsumptionService>();
        consumption.Setup(value => value.SynchronizeAsync(
                It.IsAny<string>(), organizationId, work.Id, It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
            .Returns((string userId, Guid workspaceId, Guid maintenanceId, IReadOnlyCollection<MaintenanceMaterialConsumptionLine> details) =>
                Task.FromResult(new MaintenanceConsumptionResult(40, details
                    .Where(detail => detail.Quantity.HasValue)
                    .Select(detail => new MaintenanceMaterialConsumptionCost(detail.DetailId, 20, 40))
                    .ToArray())));
        using var service = MaintenanceCommandCoverageTests.CreateService(
            context, access: MaintenanceCommandCoverageTests.CreateTeamAccess(organizationId, isFreePlan, false), maintenanceConsumption: consumption.Object);
        var update = MaintenanceCommandCoverageTests.CreateUpdateRequest(work);
        var historicalMaterial = work.Details.Single(value => value.MaterialName == "Полироль");
        update.Details = update.Details.Where(value => value.DetailId != historicalMaterial.DetailId)
            .Append(new CreateMaintenanceDetailDto
            {
                DetailId = historicalMaterial.DetailId,
                Name = historicalMaterial.Name,
                MaterialName = historicalMaterial.MaterialName,
                MaterialQuantity = 2,
                MaterialUnit = historicalMaterial.MaterialUnit
            }).ToArray();
        update.Comment = "Обновлённый комментарий";
        update.EmployeeId = null;
        update.Details.First().EmployeeId = null;
        update.Details.First().Price = 1300;

        await service.Update(work.User.Id, organizationId, work.Id, update);

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        var serviceDetail = savedWork.Details.Single(value => value.Name == "Полировка");
        var materialDetail = savedWork.Details.Single(value => value.MaterialName == "Полироль");
        var updateServices = new UpdateMaintenanceServicesDto
        {
            Version = savedWork.Version,
            Details = [new CreateMaintenanceDetailDto
            {
                DetailId = serviceDetail.DetailId,
                Name = serviceDetail.Name,
                Price = 1400
            }]
        };

        await service.UpdateServices(work.User.Id, organizationId, work.Id, updateServices);

        context.ChangeTracker.Clear();
        savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        Assert.Equal(employee.Id, savedWork.EmployeeId);
        Assert.Equal(employee.Id, savedWork.Details.Single(value => value.Name == "Полировка").EmployeeId);
        var savedMaterial = Assert.Single(savedWork.Details, value => value.MaterialName == "Полироль");
        Assert.Equal(2, savedMaterial.MaterialQuantity);
        Assert.Equal(20, savedMaterial.MaterialUnitCost);
        Assert.Equal(40, savedMaterial.MaterialTotalCost);
        Assert.Equal(materialDetail.DetailId, savedMaterial.DetailId);
        Assert.Equal(1400, savedWork.Details.Single(value => value.Name == "Полировка").Price);
        Assert.Equal(0, savedWork.Details.Single(value => value.Name == "Полировка").Position);
        Assert.Equal(1, savedMaterial.Position);
        Assert.Equal(savedWork.Details.Count, savedWork.Details.Select(value => value.DetailId).Distinct().Count());
        Assert.Equal("Обновлённый комментарий", savedWork.Comment);
        consumption.Verify(value => value.SynchronizeAsync(
            work.User.Id,
            organizationId,
            work.Id,
            It.Is<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>(details => details.Any(detail => detail.DetailId == materialDetail.DetailId && detail.Quantity == 2))),
            Times.Once);
    }

    [Fact]
    public async Task FreePlanCanConvertHistoricalMaterialIntoAService()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        var material = new MaintenanceDetail
        {
            Name = "Полироль",
            MaterialName = "Полироль",
            MaterialQuantity = 1,
            MaterialUnit = "л"
        };
        work.Details = work.Details.Append(material).ToList();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        using var service = MaintenanceCommandCoverageTests.CreateService(context, access: CreateFreeAccess(organizationId));
        var update = MaintenanceCommandCoverageTests.CreateUpdateRequest(work);
        var serviceDetail = update.Details.Single(value => value.DetailId == material.DetailId);
        serviceDetail.Price = 25;

        await service.Update(work.User.Id, organizationId, work.Id, update);

        context.ChangeTracker.Clear();
        var savedService = await context.MaintenanceDetails.SingleAsync(value => value.DetailId == material.DetailId);
        Assert.Equal(25, savedService.Price);
        Assert.Null(savedService.MaterialName);
        Assert.Null(savedService.MaterialQuantity);
    }

    [Fact]
    public async Task FreePlanRetainsInactiveHistoricalEmployeeWhenReplacingTheCatalogService()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        var employee = new OrganizationEmployee
        {
            Organization = work.Vehicle.Organization!,
            Name = "Мастер из прежнего PRO",
            IsActive = false
        };
        var previousService = new OrganizationScenarioCatalogItem
        {
            Organization = work.Vehicle.Organization!,
            Name = "Старая полировка",
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "old-polishing",
            ScenarioCode = "detailing",
            UnitPrice = 1000
        };
        var newService = new OrganizationScenarioCatalogItem
        {
            Organization = work.Vehicle.Organization!,
            Name = "Новая полировка",
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "new-polishing",
            ScenarioCode = "detailing",
            UnitPrice = 1200
        };
        var detail = work.Details[0];
        detail.CatalogItemId = previousService.Id;
        detail.EmployeeId = employee.Id;
        detail.EmployeeName = employee.Name;
        work.Details = work.Details;
        context.Maintenance.Add(work);
        context.OrganizationEmployees.Add(employee);
        context.OrganizationScenarioCatalogItems.AddRange(previousService, newService);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        using var service = MaintenanceCommandCoverageTests.CreateService(context, access: CreateFreeAccess(organizationId));
        var update = MaintenanceCommandCoverageTests.CreateUpdateRequest(work);
        var serviceUpdate = update.Details.First(value => value.DetailId == detail.DetailId);
        serviceUpdate.EmployeeId = null;
        serviceUpdate.CatalogItemId = newService.Id;
        serviceUpdate.Name = newService.Name;
        serviceUpdate.Price = newService.UnitPrice;

        await service.Update(work.User.Id, organizationId, work.Id, update);

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        var savedService = savedWork.Details.Single(value => value.DetailId == detail.DetailId);
        Assert.Equal(newService.Id, savedService.CatalogItemId);
        Assert.Equal(newService.Name, savedService.Name);
        Assert.Equal(employee.Id, savedService.EmployeeId);
        Assert.Equal(employee.Name, savedService.EmployeeName);
    }

    [Fact]
    public async Task OrganizationCreateRejectsVehicleOutsideTheRequestedWorkspace()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        var existingMaintenanceCount = await context.Maintenance.CountAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);

        await Assert.ThrowsAsync<Exception>(() => service.Create(
            work.User.Id,
            work.Vehicle.OrganizationId,
            Guid.NewGuid(),
            MaintenanceCommandCoverageTests.CreateRequest()));

        Assert.Equal(existingMaintenanceCount, await context.Maintenance.CountAsync());
    }

    [Fact]
    public async Task OrganizationCreateUsesVehicleClientAsBuyerAndLinksSchedulerTask()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var buyer = MaintenanceCommandCoverageTests.CreateClient(organizationId, "Клиент техники", "4444444444");
        context.OrganizationClients.Add(buyer);
        await context.SaveChangesAsync();
        work.Vehicle.OrganizationClientId = buyer.Id;
        await context.SaveChangesAsync();

        var scheduler = new Mock<IMaintenanceSchedulerSyncService>(MockBehavior.Strict);
        scheduler.Setup(value => value.LinkMaintenanceTaskAsync(
                work.User.Id,
                organizationId,
                work.Vehicle.Id,
                It.IsAny<Guid>(),
                "booking",
                null,
                "trigger-1",
                (int)MaintenanceStatus.Planned))
            .Returns(Task.CompletedTask);
        using var service = MaintenanceCommandCoverageTests.CreateService(context, scheduler: scheduler.Object);

        var maintenanceId = await service.Get<IMaintenanceCreateService>().Create(work.User.Id, organizationId, work.Vehicle.Id,
            MaintenanceCommandCoverageTests.CreateRequest(),
            new MaintenanceSchedulerLink("booking", null, "trigger-1"));

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(buyer.Id, savedWork.BuyerClientId);
        Assert.Equal(buyer.Name, savedWork.BuyerName);
        scheduler.VerifyAll();

        var alternateBuyer = MaintenanceCommandCoverageTests.CreateClient(organizationId, "Покупатель из запроса", "5555555555");
        context.OrganizationClients.Add(alternateBuyer);
        await context.SaveChangesAsync();
        var requestWithBuyer = MaintenanceCommandCoverageTests.CreateRequest();
        requestWithBuyer.PlannedDate = requestWithBuyer.PlannedDate!.Value.AddDays(1);
        requestWithBuyer.BuyerClientId = alternateBuyer.Id;

        var maintenanceWithBuyerId = await service.Get<IMaintenanceCreateService>().Create(
            work.User.Id, organizationId, work.Vehicle.Id, requestWithBuyer);

        var savedWorkWithBuyer = await context.Maintenance.SingleAsync(value => value.Id == maintenanceWithBuyerId);
        Assert.Equal(alternateBuyer.Id, savedWorkWithBuyer.BuyerClientId);
        Assert.Equal(alternateBuyer.Name, savedWorkWithBuyer.BuyerName);
    }

    [Fact]
    public async Task PersonalCreateAndUpdateUseTheirOwnerScopedOverloads()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork(isOrganization: false);
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);

        var maintenanceId = await service.Create(work.User.Id, work.Vehicle.Id, MaintenanceCommandCoverageTests.CreateRequest());
        var created = await context.Maintenance
            .Include(value => value.Details)
            .SingleAsync(value => value.Id == maintenanceId);
        Assert.Null(created.Vehicle.OrganizationId);
        Assert.Equal(work.User.Id, created.UserId);
        Assert.Single(created.Details);

        var update = MaintenanceCommandCoverageTests.CreateUpdateRequest(created);
        update.Comment = "Личная запись обновлена";
        await service.Update(work.User.Id, maintenanceId, update);

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal("Личная запись обновлена", saved.Comment);
        var history = await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == maintenanceId).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Single(history, value => value.EventType == MaintenanceHistoryEventCodes.EventCreated);
        Assert.Contains("Личная запись обновлена", Assert.Single(history, value => value.EventType == MaintenanceHistoryEventCodes.EventFieldsChanged).Description);
    }

    [Fact]
    public async Task PersonalWorkflowReschedulePreservesTheExistingMultiDayDuration()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork(isOrganization: false);
        work.PlannedEndDate = work.PlannedDate!.Value.AddDays(2);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var targetDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

        await service.ChangeWorkflow(work.User.Id, work.Id, new MaintenanceWorkflowActionDto
        {
            Action = "reschedule",
            PlannedDate = targetDate
        });
        await service.ChangeWorkflow(work.User.Id, work.Id, new MaintenanceWorkflowActionDto
        {
            Action = "reschedule",
            PlannedDate = targetDate
        });

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(targetDate, savedWork.PlannedDate);
        Assert.Equal(targetDate.AddDays(2), savedWork.PlannedEndDate);
        Assert.Equal(MaintenanceStatus.Planned, savedWork.Status);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task UpdatingAPlannedDateToTheSameSlotDoesNotWriteHistory()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        work.Date = work.PlannedDate!.Value;
        work.PlannedEndDate = work.PlannedDate;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var originalVersion = work.Version;
        using var service = MaintenanceCommandCoverageTests.CreateService(context);

        var status = await service.UpdatePlannedDate(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new UpdateMaintenancePlannedDateDto
            {
                PlannedDate = work.PlannedDate.Value,
                PlannedEndDate = work.PlannedEndDate,
                PlannedStartTime = work.PlannedStartTime,
                PlannedEndTime = work.PlannedEndTime
            });

        Assert.Equal(MaintenanceStatus.Planned, status);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task PlannedDateUpdateRejectsMissingMaintenance()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork(isOrganization: false);
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var missingMaintenanceId = Guid.NewGuid();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdatePlannedDate(
            work.User.Id,
            null,
            missingMaintenanceId,
            new UpdateMaintenancePlannedDateDto
            {
                PlannedDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
            }));

        Assert.False(await context.Maintenance.AnyAsync(maintenance => maintenance.Id == missingMaintenanceId));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(history => history.MaintenanceId == missingMaintenanceId));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Completed)]
    [InlineData(MaintenanceStatus.Cancelled)]
    public async Task PlannedDateUpdateRejectsCompletedAndCancelledRecords(MaintenanceStatus status)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        work.Status = status;
        if (status == MaintenanceStatus.Completed)
        {
            work.CompletedDate = work.PlannedDate;
        }

        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var originalPlannedDate = work.PlannedDate;
        var originalVersion = work.Version;
        using var service = MaintenanceCommandCoverageTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePlannedDate(
            work.User.Id,
            work.Vehicle.OrganizationId,
            work.Id,
            new UpdateMaintenancePlannedDateDto { PlannedDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc) }));

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(status, savedWork.Status);
        Assert.Equal(originalPlannedDate, savedWork.PlannedDate);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task ServiceEditingRejectsProtectedOrdersAndInvalidServiceLines()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;

        var stale = new UpdateMaintenanceServicesDto
        {
            Version = work.Version - 1,
            Details = [new CreateMaintenanceDetailDto { Name = "Полировка", Price = 1000 }]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateServices(
            work.User.Id, organizationId, work.Id, stale));

        var requests = new[]
        {
            new UpdateMaintenanceServicesDto { Version = work.Version, Details = null! },
            new UpdateMaintenanceServicesDto
            {
                Version = work.Version,
                Details = Enumerable.Range(0, 101)
                    .Select(index => new CreateMaintenanceDetailDto { Name = $"Услуга {index}", Price = 100 })
                    .ToList()
            },
            new UpdateMaintenanceServicesDto
            {
                Version = work.Version,
                Details = [new CreateMaintenanceDetailDto { Name = "Без цены", Price = null }]
            },
            new UpdateMaintenanceServicesDto
            {
                Version = work.Version,
                Details = [new CreateMaintenanceDetailDto { Name = "Материал", Price = 0, MaterialName = "Состав", MaterialQuantity = 1 }]
            }
        };
        foreach (var request in requests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateServices(
                work.User.Id, organizationId, work.Id, request));
        }

        work.Status = MaintenanceStatus.Cancelled;
        await context.SaveChangesAsync();
        var cancelledRequest = new UpdateMaintenanceServicesDto
        {
            Version = work.Version,
            Details = [new CreateMaintenanceDetailDto { Name = "Полировка", Price = 1000 }]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateServices(
            work.User.Id, organizationId, work.Id, cancelledRequest));

        work.Status = MaintenanceStatus.Completed;
        work.CompletedDate = work.PlannedDate;
        work.ReadyForHandoverAt = new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);
        work.HandedOverAt = new DateTime(2026, 10, 11, 11, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();
        var handedOverRequest = new UpdateMaintenanceServicesDto
        {
            Version = work.Version,
            Details = [new CreateMaintenanceDetailDto { Name = "Полировка", Price = 1000 }]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateServices(
            work.User.Id, organizationId, work.Id, handedOverRequest));

        await Assert.ThrowsAsync<Exception>(() => service.Update(
            work.User.Id,
            work.Vehicle.OrganizationId,
            Guid.NewGuid(),
            MaintenanceCommandCoverageTests.CreateUpdateRequest(work)));
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task BuyerAndPayerSnapshotsAreCopiedFromClientsAndSurviveLaterChanges()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var buyer = MaintenanceCommandCoverageTests.CreateClient(organizationId, "Первый клиент", "1111111111");
        var payer = MaintenanceCommandCoverageTests.CreateClient(organizationId, "Плательщик", "2222222222");
        context.OrganizationClients.AddRange(buyer, payer);
        await context.SaveChangesAsync();
        var service = MaintenanceCommandCoverageTests.CreateService(context);
        var request = MaintenanceCommandCoverageTests.CreateRequest();
        request.BuyerClientId = buyer.Id;
        request.HasBuyerSelection = true;

        var maintenanceId = await service.Create(work.User.Id, work.Vehicle.OrganizationId, work.Vehicle.Id, request);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(buyer.Id, savedWork.BuyerClientId);
        Assert.Equal(buyer.Name, savedWork.BuyerName);
        Assert.Equal(buyer.Inn, savedWork.BuyerInn);

        var historyCount = await context.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == maintenanceId);
        await service.Update(work.User.Id, work.Vehicle.OrganizationId, maintenanceId,
            MaintenanceCommandCoverageTests.CreateUpdateRequest(savedWork));
        Assert.Equal(historyCount, await context.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == maintenanceId));

        var selectedBuyerAgain = MaintenanceCommandCoverageTests.CreateUpdateRequest(savedWork);
        selectedBuyerAgain.ChangeBuyer = true;
        selectedBuyerAgain.BuyerClientId = buyer.Id;
        await service.Update(work.User.Id, work.Vehicle.OrganizationId, maintenanceId, selectedBuyerAgain);
        Assert.Equal(historyCount, await context.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == maintenanceId));

        var buyerUpdate = MaintenanceCommandCoverageTests.CreateUpdateRequest(savedWork);
        buyerUpdate.ChangeBuyer = true;
        buyerUpdate.BuyerClientId = payer.Id;
        await service.Update(work.User.Id, work.Vehicle.OrganizationId, maintenanceId, buyerUpdate);
        context.ChangeTracker.Clear();
        savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == maintenanceId);
        Assert.Equal(payer.Id, savedWork.BuyerClientId);
        Assert.Equal(payer.Name, savedWork.BuyerName);

        await service.AddPayment(work.User.Id, work.Vehicle.OrganizationId!.Value, maintenanceId, new AddMaintenancePaymentDto
        {
            Id = Guid.NewGuid(),
            Amount = 200,
            PayerClientId = buyer.Id
        });
        var payment = await context.MaintenancePayments.SingleAsync(value => value.MaintenanceId == maintenanceId);
        Assert.Equal(buyer.Id, payment.PayerClientId);
        Assert.Equal(buyer.Name, payment.PayerName);
        Assert.Equal(buyer.Inn, payment.PayerInn);

        var savedBuyer = await context.OrganizationClients.SingleAsync(value => value.Id == buyer.Id);
        var savedPayer = await context.OrganizationClients.SingleAsync(value => value.Id == payer.Id);
        savedBuyer.Name = "Переименованный клиент";
        savedPayer.Name = "Переименованный плательщик";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == maintenanceId);
        payment = await context.MaintenancePayments.SingleAsync(value => value.MaintenanceId == maintenanceId);
        Assert.Equal("Плательщик", savedWork.BuyerName);
        Assert.Equal("Первый клиент", payment.PayerName);

        var clearBuyer = MaintenanceCommandCoverageTests.CreateUpdateRequest(savedWork);
        clearBuyer.ChangeBuyer = true;
        clearBuyer.BuyerClientId = null;
        await service.Update(work.User.Id, work.Vehicle.OrganizationId, maintenanceId, clearBuyer);
        context.ChangeTracker.Clear();
        savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == maintenanceId);
        Assert.Null(savedWork.BuyerClientId);
        var historyAfterClear = await context.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == maintenanceId);

        var buyerRemainsCleared = MaintenanceCommandCoverageTests.CreateUpdateRequest(savedWork);
        buyerRemainsCleared.ChangeBuyer = true;
        buyerRemainsCleared.BuyerClientId = null;
        await service.Update(work.User.Id, work.Vehicle.OrganizationId, maintenanceId, buyerRemainsCleared);
        Assert.Equal(historyAfterClear, await context.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == maintenanceId));

        var foreignOrganization = new Organization { Name = "Другая организация", OwnerUserId = work.User.Id };
        context.Organizations.Add(foreignOrganization);
        await context.SaveChangesAsync();
        var foreignClient = MaintenanceCommandCoverageTests.CreateClient(foreignOrganization.Id, "Чужой клиент", "3333333333");
        var deletedClient = MaintenanceCommandCoverageTests.CreateClient(organizationId, "Удалённый клиент", "4444444444");
        deletedClient.IsDeleted = true;
        context.OrganizationClients.AddRange(foreignClient, deletedClient);
        await context.SaveChangesAsync();
        foreach (var invalidClientId in new[] { foreignClient.Id, deletedClient.Id })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
                work.User.Id,
                organizationId,
                maintenanceId,
                new AddMaintenancePaymentDto
                {
                    Id = Guid.NewGuid(),
                    Amount = 100,
                    ExpectedPaidAmount = 200,
                    PayerClientId = invalidClientId
                }));
        }

        var invalidBuyerRequest = MaintenanceCommandCoverageTests.CreateRequest();
        invalidBuyerRequest.BuyerClientId = foreignClient.Id;
        invalidBuyerRequest.HasBuyerSelection = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            work.User.Id, work.Vehicle.OrganizationId, work.Vehicle.Id, invalidBuyerRequest));
        Assert.Equal(1, await context.MaintenancePayments.CountAsync(value => value.MaintenanceId == maintenanceId));
    }

    [Fact]
    public async Task LinkedDefectsCanBeResolvedOnCompletionAndUnlinkedWithHistory()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        var defect = new VehicleDefect
        {
            Vehicle = work.Vehicle,
            Organization = work.Vehicle.Organization!,
            User = work.User,
            Title = "Повреждённая деталь"
        };
        var resolvedDefect = new VehicleDefect
        {
            Vehicle = work.Vehicle,
            Organization = work.Vehicle.Organization!,
            User = work.User,
            Title = "Уже устранённая неисправность",
            StatusCode = VehicleDefectStatusCodes.Resolved,
            ClosedAt = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc)
        };
        context.Vehicles.Add(work.Vehicle);
        context.VehicleDefects.AddRange(defect, resolvedDefect);
        await context.SaveChangesAsync();
        var userId = work.User.Id;
        var organizationId = work.Vehicle.Organization!.Id;
        var vehicleId = work.Vehicle.Id;
        var service = MaintenanceCommandCoverageTests.CreateService(context);
        var request = MaintenanceCommandCoverageTests.CreateRequest();
        request.LinkedDefectIds = [defect.Id, defect.Id, resolvedDefect.Id];

        var maintenanceId = await service.Create(userId, organizationId, vehicleId, request);
        Assert.Equal(2, await context.MaintenanceDefects.CountAsync(value => value.MaintenanceId == maintenanceId));
        context.ChangeTracker.Clear();
        work = await context.Maintenance
            .Include(value => value.Details)
            .SingleAsync(value => value.Id == maintenanceId);
        await service.ChangeWorkflow(
            userId,
            organizationId,
            maintenanceId,
            new MaintenanceWorkflowActionDto { Action = "complete" });

        var savedDefect = await context.VehicleDefects.SingleAsync(value => value.Id == defect.Id);
        Assert.Equal(VehicleDefectStatusCodes.Resolved, savedDefect.StatusCode);
        Assert.NotNull(savedDefect.ClosedAt);
        var savedResolvedDefect = await context.VehicleDefects.SingleAsync(value => value.Id == resolvedDefect.Id);
        Assert.Equal(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), savedResolvedDefect.ClosedAt);
        context.ChangeTracker.Clear();
        work = await context.Maintenance
            .Include(value => value.Details)
            .SingleAsync(value => value.Id == maintenanceId);
        var update = MaintenanceCommandCoverageTests.CreateUpdateRequest(work);
        update.LinkedDefectIds = [];
        await service.Update(userId, organizationId, maintenanceId, update);
        Assert.False(await context.MaintenanceDefects.AnyAsync(value => value.MaintenanceId == maintenanceId));
        Assert.Contains(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == maintenanceId).ToListAsync(),
            value => value.EventType == "defect_unlinked");
        Assert.Contains(await context.VehicleHistoryEvents.Where(value => value.VehicleId == vehicleId).ToListAsync(),
            value => value.EventType == "defect_unlinked");

        var invalidRequest = MaintenanceCommandCoverageTests.CreateRequest();
        invalidRequest.LinkedDefectIds = [Guid.NewGuid()];
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            userId, organizationId, vehicleId, invalidRequest));
        Assert.Equal(1, await context.Maintenance.CountAsync(value => value.VehicleId == vehicleId));
    }

    [Fact]
    public async Task CreatingCompletedWorkClosesItsLinkedDefect()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        var defect = new VehicleDefect
        {
            VehicleId = work.Vehicle.Id,
            OrganizationId = work.Vehicle.OrganizationId!.Value,
            UserId = work.User.Id,
            Title = "Неисправность при завершённой работе"
        };
        context.VehicleDefects.Add(defect);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var request = MaintenanceCommandCoverageTests.CreateRequest();
        request.Status = MaintenanceStatus.Completed;
        request.CompletedDate = request.PlannedDate;
        request.LinkedDefectIds = [defect.Id];

        var maintenanceId = await service.Create(work.User.Id, work.Vehicle.OrganizationId, work.Vehicle.Id, request);

        var savedDefect = await context.VehicleDefects.SingleAsync(value => value.Id == defect.Id);
        Assert.Equal(VehicleDefectStatusCodes.Resolved, savedDefect.StatusCode);
        Assert.NotNull(savedDefect.ClosedAt);
        Assert.True(await context.MaintenanceDefects.AnyAsync(value => value.MaintenanceId == maintenanceId));
    }

    [Fact]
    public async Task UpdateCannotCompleteOrganizationWorkWithoutWorkflowAndKeepsLinkedDefectOpen()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        var defect = new VehicleDefect
        {
            Vehicle = work.Vehicle,
            Organization = work.Vehicle.Organization!,
            User = work.User,
            Title = "Неисправность, закрываемая при завершении"
        };
        work.LinkedDefects.Add(new MaintenanceDefect
        {
            Maintenance = work,
            VehicleDefect = defect,
            Organization = work.Vehicle.Organization!,
            User = work.User
        });
        context.Maintenance.Add(work);
        context.VehicleDefects.Add(defect);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var request = MaintenanceCommandCoverageTests.CreateUpdateRequest(work);
        request.Status = MaintenanceStatus.Completed;
        request.CompletedDate = work.PlannedDate;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(
            work.User.Id, work.Vehicle.OrganizationId, work.Id, request));

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        var savedDefect = await context.VehicleDefects.SingleAsync(value => value.Id == defect.Id);
        Assert.Equal(MaintenanceStatus.Planned, savedWork.Status);
        Assert.Null(savedWork.CompletedDate);
        Assert.Equal(VehicleDefectStatusCodes.New, savedDefect.StatusCode);
        Assert.Null(savedDefect.ClosedAt);
        Assert.True(await context.MaintenanceDefects.AnyAsync(value => value.MaintenanceId == work.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task DeletePersonalRecordSoftDeletesAndKeepsItsHistory()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork(isOrganization: false);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var counter = new Mock<IVehicleCounterCommandService>(MockBehavior.Strict);
        counter.Setup(value => value.RemoveCounterSyncFromMaintenanceAsync(
                work.User.Id, null, work.Vehicle.Id, work.Id))
            .Returns(Task.CompletedTask);
        using var service = MaintenanceCommandCoverageTests.CreateService(context, counter: counter.Object);

        await service.Delete(work.User.Id, work.Id);

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.True(savedWork.IsDeleted);
        Assert.Equal("deleted", (await context.MaintenanceHistoryEvents.SingleAsync(value => value.MaintenanceId == work.Id)).EventType);
        counter.VerifyAll();
    }

    [Fact]
    public async Task DeleteRejectsMissingRecordWithoutCallingSideEffects()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork(isOrganization: false);
        context.Vehicles.Add(work.Vehicle);
        await context.SaveChangesAsync();
        var counter = new Mock<IVehicleCounterCommandService>(MockBehavior.Strict);
        using var service = MaintenanceCommandCoverageTests.CreateService(context, counter: counter.Object);
        var missingMaintenanceId = Guid.NewGuid();

        await Assert.ThrowsAsync<Exception>(() => service.Delete(work.User.Id, missingMaintenanceId));

        Assert.False(await context.Maintenance.AnyAsync(maintenance => maintenance.Id == missingMaintenanceId));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(history => history.MaintenanceId == missingMaintenanceId));
        counter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteOrganizationRecordSoftDeletesAndResetsItsWorkflow()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var counter = new Mock<IVehicleCounterCommandService>(MockBehavior.Strict);
        counter.Setup(value => value.RemoveCounterSyncFromMaintenanceAsync(
                work.User.Id, work.Vehicle.OrganizationId, work.Vehicle.Id, work.Id))
            .Returns(Task.CompletedTask);
        var scheduler = new Mock<IMaintenanceSchedulerSyncService>(MockBehavior.Strict);
        scheduler.Setup(value => value.ResetMaintenanceWorkflowAsync(
                work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id))
            .Returns(Task.CompletedTask);
        using var service = MaintenanceCommandCoverageTests.CreateService(context, counter: counter.Object, scheduler: scheduler.Object);

        await service.Delete(work.User.Id, work.Vehicle.OrganizationId, work.Id);

        context.ChangeTracker.Clear();
        Assert.True((await context.Maintenance.SingleAsync(value => value.Id == work.Id)).IsDeleted);
        Assert.Equal("deleted", (await context.MaintenanceHistoryEvents.SingleAsync(value => value.MaintenanceId == work.Id)).EventType);
        counter.VerifyAll();
        scheduler.VerifyAll();
    }

    [Fact]
    public async Task DeleteRejectsOrganizationRecordWithStartedWorkWithoutDeletingIt()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = work.Details[0].DetailId,
            MaintenanceId = work.Id,
            OrganizationId = work.Vehicle.OrganizationId!.Value,
            EmployeeId = Guid.NewGuid(),
            Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Delete(
            work.User.Id, work.Vehicle.OrganizationId, work.Id));

        Assert.False((await context.Maintenance.SingleAsync(value => value.Id == work.Id)).IsDeleted);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task PaymentsRejectInvalidMethodLongCommentAndConflictingRetries()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;

        var invalidMethod = await Assert.ThrowsAsync<MaintenancePaymentValidationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id,
            new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 100, MethodCode = "crypto" }));
        Assert.Equal("maintenance.payment.methodInvalid", invalidMethod.MessageKey);
        Assert.Equal("Выберите корректный способ оплаты.", invalidMethod.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id,
            new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 100, Comment = new string('x', 1001) }));

        var paymentId = Guid.NewGuid();
        await service.AddPayment(work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
        {
            Id = paymentId,
            Amount = 100,
            MethodCode = " CASH ",
            Comment = " Первый платёж "
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id,
            new AddMaintenancePaymentDto { Id = paymentId, Amount = 200, MethodCode = "cash", Comment = "Первый платёж" }));
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(100, savedWork.PaidAmount);
        Assert.Single(await context.MaintenancePayments.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task PaymentRetryIsIdempotentAndAcceptsAnEmptyComment()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var firstPayment = new AddMaintenancePaymentDto
        {
            Id = Guid.NewGuid(),
            Amount = 100,
            MethodCode = " CASH ",
            Comment = "Первый платёж"
        };

        await service.AddPayment(work.User.Id, organizationId, work.Id, firstPayment);
        await service.AddPayment(work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
        {
            Id = firstPayment.Id,
            Amount = firstPayment.Amount,
            MethodCode = "cash",
            Comment = "  Первый платёж  "
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
            {
                Id = firstPayment.Id,
                Amount = firstPayment.Amount,
                MethodCode = "cash",
                Comment = "Первый платёж",
                PayerClientId = Guid.NewGuid()
            }));
        await service.AddPayment(work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
        {
            Id = Guid.NewGuid(),
            Amount = 100,
            ExpectedPaidAmount = 100,
            Comment = null
        });

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(200, savedWork.PaidAmount);
        var payments = await context.MaintenancePayments.Where(value => value.MaintenanceId == work.Id).ToListAsync();
        Assert.Equal(2, payments.Count);
        Assert.Contains(payments, value => value.Comment == null);
        Assert.Equal(2, await context.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task PaymentRetryRequiresTheSameMaintenanceCommentMethodAndExplicitPayer()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var payer = MaintenanceCommandCoverageTests.CreateClient(organizationId, "Плательщик", "6666666666");
        context.OrganizationClients.Add(payer);
        await context.SaveChangesAsync();
        var secondWork = MaintenanceCommandCoverageTests.CreateWork();
        secondWork.User = work.User;
        secondWork.Vehicle = work.Vehicle;
        var secondWorkDate = work.PlannedDate!.Value.AddDays(3);
        secondWork.PlannedDate = secondWorkDate;
        secondWork.Date = secondWorkDate;
        context.Maintenance.Add(secondWork);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var payment = new AddMaintenancePaymentDto
        {
            Id = Guid.NewGuid(),
            Amount = 100,
            MethodCode = "cash",
            Comment = "Исходный платёж",
            PayerClientId = payer.Id
        };

        await service.AddPayment(work.User.Id, organizationId, work.Id, payment);
        await service.AddPayment(work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
        {
            Id = payment.Id,
            Amount = payment.Amount,
            MethodCode = payment.MethodCode,
            Comment = payment.Comment,
            PayerClientId = payer.Id
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
            {
                Id = payment.Id,
                Amount = payment.Amount,
                MethodCode = payment.MethodCode,
                Comment = "Другое описание",
                PayerClientId = payer.Id
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
            {
                Id = payment.Id,
                Amount = payment.Amount,
                MethodCode = "card",
                Comment = payment.Comment,
                PayerClientId = payer.Id
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
            {
                Id = payment.Id,
                Amount = payment.Amount,
                MethodCode = payment.MethodCode,
                Comment = payment.Comment,
                PayerClientId = Guid.NewGuid()
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, secondWork.Id, new AddMaintenancePaymentDto
            {
                Id = payment.Id,
                Amount = payment.Amount,
                MethodCode = payment.MethodCode,
                Comment = payment.Comment,
                PayerClientId = payer.Id
            }));

        context.ChangeTracker.Clear();
        Assert.Equal(100, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).PaidAmount);
        Assert.Equal(0, (await context.Maintenance.SingleAsync(value => value.Id == secondWork.Id)).PaidAmount);
        Assert.Single(await context.MaintenancePayments.Where(value => value.Id == payment.Id).ToListAsync());
    }

    [Fact]
    public async Task CancelPaymentRejectsMissingAndInconsistentPaymentTotals()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var missingPaymentId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelPayment(
            work.User.Id, organizationId, work.Id, missingPaymentId, "Ошибка"));
        var paymentId = Guid.NewGuid();
        await service.AddPayment(work.User.Id, organizationId, work.Id, new AddMaintenancePaymentDto
        {
            Id = paymentId,
            Amount = 100
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelPayment(
            work.User.Id, organizationId, work.Id, paymentId, new string('x', 1001)));
        work.Status = MaintenanceStatus.Cancelled;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddPayment(
            work.User.Id, organizationId, work.Id,
            new AddMaintenancePaymentDto { Id = Guid.NewGuid(), Amount = 100, ExpectedPaidAmount = 100 }));
        work.PaidAmount = 0;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelPayment(
            work.User.Id, organizationId, work.Id, paymentId, "Восстановление согласованности"));

        context.ChangeTracker.Clear();
        Assert.Equal(0, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).PaidAmount);
        Assert.Null((await context.MaintenancePayments.SingleAsync(value => value.Id == paymentId)).CancelledAt);
    }

    [Fact]
    public async Task GetPaymentsRejectsMaintenanceOutsideTheRequestedWorkspace()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPayments(
            work.User.Id, Guid.NewGuid(), work.Id));
    }

    [Fact]
    public async Task PlannedDateUpdateUsesDateAndTimeDefaultsWhenLegacyFieldsAreMissing()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceCommandCoverageTests.CreateWork(isOrganization: false);
        work.PlannedDate = null;
        work.PlannedEndDate = null;
        work.PlannedStartTime = null;
        work.PlannedEndTime = null;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var service = MaintenanceCommandCoverageTests.CreateService(context);
        var targetDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

        await service.UpdatePlannedDate(work.User.Id, null, work.Id,
            new UpdateMaintenancePlannedDateDto { PlannedDate = targetDate });

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(targetDate, savedWork.PlannedDate);
        Assert.Equal(targetDate, savedWork.PlannedEndDate);
        Assert.Equal(TimeSpan.FromHours(9), savedWork.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(10), savedWork.PlannedEndTime);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    private static MaintenanceTestScope CreateService(
        OpenCRMContext context,
        IVehicleCounterCommandService? counter = null,
        IOrganizationSubscriptionService? access = null,
        IMaintenanceSchedulerSyncService? scheduler = null,
        IMaintenanceConsumptionService? maintenanceConsumption = null)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
        var counterMock = counter == null ? new Mock<IVehicleCounterCommandService>() : null;
        if (counterMock != null)
        {
            counterMock.Setup(value => value.SyncCounterFromMaintenanceAsync(
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<DateTime>()))
                .Returns(Task.CompletedTask);
            counterMock.Setup(value => value.RemoveCounterSyncFromMaintenanceAsync(
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);
        }

        var schedulerMock = scheduler == null ? new Mock<IMaintenanceSchedulerSyncService>() : null;
        schedulerMock?.Setup(value => value.SyncMaintenanceWorkflowAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        schedulerMock?.Setup(value => value.ResetMaintenanceWorkflowAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        var consumption = maintenanceConsumption;
        if (consumption == null)
        {
            var consumptionMock = new Mock<IMaintenanceConsumptionService>();
            consumptionMock.Setup(value => value.SynchronizeAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
                .ReturnsAsync(new MaintenanceConsumptionResult(0, []));
            consumption = consumptionMock.Object;
        }

        if (access == null)
        {
            var enabledAccess = new Mock<IOrganizationSubscriptionService>();
            enabledAccess.Setup(value => value.HasFeatureAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.MembersModule))
                .ReturnsAsync(true);
            access = enabledAccess.Object;
        }

        return new MaintenanceTestScope(
            new MaintenanceRepository(context),
            counter ?? counterMock!.Object,
            clock.Object,
            context,
            access,
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            consumption,
            scheduler: scheduler ?? schedulerMock!.Object);
    }

    private static IOrganizationSubscriptionService CreateFreeAccess(Guid organizationId) =>
        MaintenanceCommandCoverageTests.CreateTeamAccess(organizationId, true, false);

    private static IOrganizationSubscriptionService CreateTeamAccess(Guid organizationId, bool isFreePlan, bool hasTeam)
    {
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), organizationId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        access.Setup(value => value.GetWorkspaceAccessAsync(It.IsAny<string>(), organizationId))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { IsFreePlan = isFreePlan, HasActiveSubscription = !isFreePlan });
        access.Setup(value => value.HasFeatureAsync(It.IsAny<string>(), organizationId, FeatureCodes.MembersModule))
            .ReturnsAsync(hasTeam);

        return access.Object;
    }

    private static MaintenanceEntity CreateWork(bool isOrganization = true)
    {
        var user = new ApplicationUser
        {
            Email = $"commands-{Guid.NewGuid():N}@example.test",
            UserName = $"commands-{Guid.NewGuid():N}"
        };
        var organization = isOrganization
            ? new Organization
            {
                Name = "Проверка команд обслуживания",
                PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
                MaintenanceWorkflowMode = "simple",
                OwnerUser = user,
                Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
            }
            : null;
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            RegistrationPlate = "А123АА154"
        };
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = user,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Date = date,
            PlannedDate = date.AddDays(1),
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            DateTimeCreation = date.AddDays(-1),
            Details = [
                new() { Name = "Полировка", Price = 1000 },
                new() { Name = "Вторая услуга", Price = 500, VatRateCode = "without_vat" }
            ]
        };
    }

    private static OrganizationClient CreateClient(Guid organizationId, string name, string inn)
    {
        return new OrganizationClient
        {
            OrganizationId = organizationId,
            Name = name,
            PartyTypeCode = "company",
            Inn = inn,
            Kpp = "123456789",
            LegalAddress = "Тестовый юридический адрес"
        };
    }

    private static CreateMaintenanceDto CreateRequest()
    {
        var date = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

        return new CreateMaintenanceDto
        {
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            PlannedDate = date,
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            CompletedDate = null,
            Location = null,
            Performer = null,
            Mileage = null,
            Comment = "Создано интеграционным тестом",
            Details = [new CreateMaintenanceDetailDto { Name = "Полировка", Price = 1000 }]
        };
    }

    private static UpdateMaintenanceDto CreateUpdateRequest(MaintenanceEntity work)
    {
        return new UpdateMaintenanceDto
        {
            Version = work.Version,
            Status = work.Status,
            Type = work.Type,
            PlannedDate = work.PlannedDate,
            PlannedStartTime = work.PlannedStartTime,
            PlannedEndTime = work.PlannedEndTime,
            PlannedEndDate = work.PlannedEndDate,
            CompletedDate = work.CompletedDate,
            Location = work.Location,
            Performer = work.Performer,
            Mileage = work.Mileage,
            Comment = work.Comment,
            DiscountAmount = work.DiscountAmount,
            PaidAmount = work.PaidAmount,
            LaborCostAmount = work.LaborCostAmount,
            Details = work.Details.Select(detail => new CreateMaintenanceDetailDto
            {
                DetailId = detail.DetailId,
                Name = detail.Name,
                Description = detail.Description,
                Price = detail.Price,
                VatRateCode = detail.VatRateCode,
                EmployeeId = detail.EmployeeId,
                EmployeeName = detail.EmployeeName,
                CatalogItemId = detail.CatalogItemId,
                Quantity = detail.Quantity
            }).ToArray()
        };
    }
}
