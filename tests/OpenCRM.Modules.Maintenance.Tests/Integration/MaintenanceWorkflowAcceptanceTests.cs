using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Inventory;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Planning;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceWorkflowAcceptanceTests(MaintenanceDatabaseFixture fixture)
    : IClassFixture<MaintenanceDatabaseFixture>
{
    [Theory]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    [InlineData(OrganizationTypeCodes.Fleet)]
    public async Task OrganizationWorkflowKeepsOrderInventoryCounterAndPlanningConsistent(string scenarioCode)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"workflow-{scenarioCode}@example.test" };
        var organization = new Organization
        {
            Name = $"Приёмка {scenarioCode}",
            PrimaryScenarioCode = scenarioCode,
            OwnerUser = user,
            MaintenanceWorkflowMode = "staged"
        };
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            Name = "Тестовая техника",
            RegistrationPlate = "ТЕСТ001",
            MileageUnit = VehicleMileageUnit.Kilometers
        };
        var member = new OrganizationMember
        {
            Organization = organization,
            User = user,
            RoleCode = OrganizationRoleCodes.Owner
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = member,
            Name = "Мастер приёмки"
        };
        var serviceItem = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            ScenarioCode = scenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Code = "acceptance-service",
            Name = "Диагностика",
            UnitPrice = 1200
        };
        var materialItem = new OrganizationScenarioCatalogItem
        {
            Organization = organization,
            ScenarioCode = scenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.Material,
            Code = "acceptance-material",
            Name = "Очиститель",
            UnitPrice = 100
        };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization,
            CatalogItem = materialItem,
            Name = materialItem.Name,
            Unit = "шт",
            QuantityOnHand = 5,
            AverageUnitCost = 100,
            InventoryValue = 500
        };
        context.OrganizationEmployees.Add(employee);
        context.Vehicles.Add(vehicle);
        context.OrganizationScenarioCatalogItems.AddRange(serviceItem, materialItem);
        context.OrganizationMaterialStocks.Add(stock);
        await context.SaveChangesAsync();

        var services = new ServiceCollection();
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        services.AddSingleton(subscription.Object);
        services.AddSingleton<ISystemClock>(new TestSystemClock());
        services.AddOpenCRMCatalog();
        services.AddSingleton(Mock.Of<ICatalogInitializationService>());
        services.AddScoped(_ => context);
        services.AddScoped<IMaintenanceRepository, MaintenanceRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddOpenCRMMaintenance();
        services.AddOpenCRMInventory();
        services.AddOpenCRMVehicles();
        services.AddOpenCRMPlanning();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        var organizationId = organization.Id;
        var create = scoped.GetRequiredService<IMaintenanceCreateService>();
        var maintenanceId = await create.Create(
            user.Id,
            organizationId,
            vehicle.Id,
            new CreateMaintenanceDto
            {
                Status = MaintenanceStatus.Planned,
                Type = MaintenanceType.Maintenance,
                PlannedDate = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
                PlannedStartTime = TimeSpan.FromHours(9),
                PlannedEndTime = TimeSpan.FromHours(10),
                CompletedDate = null,
                Location = null,
                EmployeeId = null,
                Performer = null,
                Mileage = 12345,
                Comment = "Сквозная приёмка",
                Details =
                [
                    new CreateMaintenanceDetailDto
                    {
                        Name = serviceItem.Name,
                        Price = 1200,
                        CatalogItemId = serviceItem.Id,
                        Quantity = 1
                    },
                    new CreateMaintenanceDetailDto
                    {
                        Name = materialItem.Name,
                        MaterialName = materialItem.Name,
                        MaterialCatalogItemId = materialItem.Id,
                        MaterialQuantity = 2,
                        MaterialUnit = "шт"
                    }
                ]
            },
            new MaintenanceSchedulerLink("maintenance", null, $"acceptance-{scenarioCode}"));

        var maintenance = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        var serviceLine = Assert.Single(maintenance.Details, detail => detail.CatalogItemId == serviceItem.Id);
        var materialLine = Assert.Single(maintenance.Details, detail => detail.MaterialCatalogItemId == materialItem.Id);
        Assert.Equal(100, materialLine.MaterialUnitCost);
        Assert.Equal(200, maintenance.MaterialCostAmount);
        Assert.Equal(3, (await context.OrganizationMaterialStocks.SingleAsync(item => item.Id == stock.Id)).QuantityOnHand);
        Assert.Equal(-2, await context.OrganizationMaterialMovements
            .Where(movement => movement.MaintenanceId == maintenanceId && movement.TypeCode == OrganizationMaterialMovementTypeCodes.MaintenanceIssue)
            .SumAsync(movement => movement.Quantity));
        Assert.Equal(maintenanceId, await context.OrganizationSchedulerTaskStates
            .Where(state => state.TriggerKey == $"acceptance-{scenarioCode}")
            .Select(state => state.MaintenanceId)
            .SingleAsync());

        var assignment = scoped.GetRequiredService<IMaintenanceAssignmentService>();
        await assignment.AssignEmployee(user.Id, organizationId, maintenanceId,
            new AssignMaintenanceEmployeeDto { EmployeeId = employee.Id, Version = maintenance.Version });

        var work = scoped.GetRequiredService<IMaintenanceWorkExecutionService>();
        var detailId = serviceLine.DetailId;
        var operation = await work.ExecuteAsync(user.Id, organizationId, maintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Version = 0, Action = "start" });
        Assert.Equal(MaintenanceWorkStatus.InProgress, operation.Status);
        operation = await work.ExecuteAsync(user.Id, organizationId, maintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Version = operation.Version, Action = "pause", Reason = "Ожидание проверки" });
        Assert.Equal(MaintenanceWorkStatus.Paused, operation.Status);
        operation = await work.ExecuteAsync(user.Id, organizationId, maintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Version = operation.Version, Action = "resume" });
        Assert.Equal(MaintenanceWorkStatus.InProgress, operation.Status);
        operation = await work.ExecuteAsync(user.Id, organizationId, maintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Version = operation.Version, Action = "complete" });
        Assert.Equal(MaintenanceWorkStatus.Completed, operation.Status);

        await work.ReturnForReworkAsync(user.Id, organizationId, maintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Version = operation.Version, Reason = "Повторная проверка результата" });
        var reworkVersion = await context.MaintenanceWorkOperations.AsNoTracking()
            .Where(item => item.Id == detailId)
            .Select(item => item.Version)
            .SingleAsync();
        operation = await work.ExecuteAsync(user.Id, organizationId, maintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Version = reworkVersion, Action = "start" });
        operation = await work.ExecuteAsync(user.Id, organizationId, maintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Version = operation.Version, Action = "complete" });

        var workflow = scoped.GetRequiredService<IMaintenanceWorkflowService>();
        await workflow.ChangeWorkflow(user.Id, organizationId, maintenanceId,
            new MaintenanceWorkflowActionDto { Action = "complete", Mileage = 12345 });
        context.ChangeTracker.Clear();
        maintenance = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        Assert.Equal(MaintenanceStatus.Completed, maintenance.Status);
        Assert.NotNull(maintenance.ReadyForHandoverAt);
        Assert.Equal(12345, (await context.Vehicles.SingleAsync(item => item.Id == vehicle.Id)).CurrentMileage);
        Assert.Equal(12345, await context.VehicleCounterEntries
            .Where(entry => entry.MaintenanceId == maintenanceId && !entry.IsDeleted)
            .Select(entry => entry.Value)
            .SingleAsync());
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.Closed, await context.OrganizationSchedulerTaskStates
            .Where(state => state.TriggerKey == $"acceptance-{scenarioCode}")
            .Select(state => state.StatusCode)
            .SingleAsync());

        var payments = scoped.GetRequiredService<IMaintenancePaymentService>();
        var cancelledPaymentId = Guid.NewGuid();
        await payments.AddPayment(user.Id, organizationId, maintenanceId, new AddMaintenancePaymentDto
        {
            Id = cancelledPaymentId,
            Amount = 400,
            MethodCode = "cash",
            ExpectedPaidAmount = 0,
            Comment = "Тестовый аванс"
        });
        await payments.CancelPayment(user.Id, organizationId, maintenanceId, cancelledPaymentId, "Проверка отмены тестовой оплаты");
        await payments.AddPayment(user.Id, organizationId, maintenanceId, new AddMaintenancePaymentDto
        {
            Id = Guid.NewGuid(),
            Amount = 1200,
            MethodCode = "card",
            ExpectedPaidAmount = 0,
            Comment = "Тестовая окончательная оплата"
        });
        var paymentHistory = await payments.GetPayments(user.Id, organizationId, maintenanceId);
        Assert.Equal(2, paymentHistory.Count);
        Assert.Contains(paymentHistory, payment => payment.CancelledAt.HasValue);
        Assert.Contains(paymentHistory, payment => !payment.CancelledAt.HasValue && payment.Amount == 1200);

        await workflow.ChangeWorkflow(user.Id, organizationId, maintenanceId,
            new MaintenanceWorkflowActionDto { Action = "handover" });
        context.ChangeTracker.Clear();
        maintenance = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        Assert.NotNull(maintenance.HandedOverAt);
        Assert.Equal(1200, maintenance.PaidAmount);
        Assert.Equal(3, (await context.OrganizationMaterialStocks.SingleAsync(item => item.Id == stock.Id)).QuantityOnHand);
        Assert.Equal(2, await context.MaintenanceWorkEvents.CountAsync(item => item.MaintenanceId == maintenanceId
            && item.Status == MaintenanceWorkStatus.Completed));
        var intervals = await context.MaintenanceWorkIntervals
            .Where(interval => interval.OperationId == detailId)
            .ToListAsync();
        Assert.Equal(3, intervals.Count);
        Assert.All(intervals, interval => Assert.NotNull(interval.EndedAt));
    }

    private sealed class TestSystemClock : ISystemClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

        public DateTime UtcToday => this.UtcNow.Date;
    }
}
