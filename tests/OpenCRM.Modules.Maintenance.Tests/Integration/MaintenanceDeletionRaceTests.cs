using OpenCRM.Modules.Planning.Contracts;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Work;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceDeletionRaceTests(MaintenanceDatabaseFixture fixture)
    : IClassFixture<MaintenanceDatabaseFixture>
{
    [Theory]
    [InlineData("intake", false)]
    [InlineData("update", false)]
    [InlineData("workflow", false)]
    [InlineData("schedule", true)]
    [InlineData("delete", false)]
    [InlineData("claim", true)]
    [InlineData("execute", true)]
    [InlineData("return_for_rework", true)]
    [InlineData("add_report", true)]
    [InlineData("decide_report", true)]
    [InlineData("adjust_time", true)]
    public async Task CommandRejectsMaintenanceDeletedAfterInitialRead(string command, bool expectsKeyNotFound)
    {
        var maintenanceId = Guid.NewGuid();
        var raceInterceptor = new DeleteAfterMaintenanceLockInterceptor(fixture, maintenanceId);
        await using var context = fixture.CreateContext(raceInterceptor);
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            Name = "Гонка удаления обслуживания",
            OwnerUser = user,
            MaintenanceWorkflowMode = "simple",
            Members = [member]
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = member,
            Name = "Мастер"
        };
        var detailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            Id = maintenanceId,
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
            Details =
            [
                new MaintenanceDetail
                {
                    DetailId = detailId,
                    Name = "Работа",
                    Price = 1000,
                    EmployeeId = employee.Id,
                    EmployeeName = employee.Name
                }
            ]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        var counter = new Mock<IVehicleCounterCommandService>(MockBehavior.Strict);
        var scheduler = new Mock<IMaintenanceSchedulerSyncService>(MockBehavior.Strict);
        var consumption = new Mock<IMaintenanceConsumptionService>(MockBehavior.Strict);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.MaintenanceIntake))
            .ReturnsAsync(true);
        using var scope = new MaintenanceTestScope(
            new MaintenanceRepository(context),
            counter.Object,
            clock.Object,
            context,
            subscriptions.Object,
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            consumption.Object,
            scheduler: scheduler.Object);

        var exception = await Record.ExceptionAsync(() => MaintenanceDeletionRaceTests.ExecuteAsync(
            scope, command, user.Id, organization.Id, maintenance.Id, detailId));

        Assert.NotNull(exception);
        Assert.Equal(expectsKeyNotFound ? typeof(KeyNotFoundException) : typeof(InvalidOperationException), exception.GetType());
        Assert.Equal("Обслуживание не найдено.", exception.Message);
        Assert.True(raceInterceptor.DidDeleteMaintenance);
        Assert.Equal(0, await context.MaintenanceHistoryEvents.CountAsync(item => item.MaintenanceId == maintenance.Id));
        Assert.Equal(0, await context.MaintenanceWorkOperations.CountAsync(item => item.MaintenanceId == maintenance.Id));
        Assert.Equal(0, await context.MaintenanceWorkIntervals.CountAsync(item => context.MaintenanceWorkOperations
            .Any(operation => operation.Id == item.OperationId && operation.MaintenanceId == maintenance.Id)));
        Assert.Equal(0, await context.MaintenanceWorkEvents.CountAsync(item => item.MaintenanceId == maintenance.Id));
        Assert.Equal(0, await context.MaintenanceWorkReports.CountAsync(item => item.MaintenanceId == maintenance.Id));

        var savedMaintenance = await context.Set<OpenCRM.Domain.Maintenance>().AsNoTracking()
            .SingleAsync(item => item.Id == maintenance.Id);
        Assert.True(savedMaintenance.IsDeleted);
        Assert.Equal(maintenance.Comment, savedMaintenance.Comment);
        Assert.Equal(MaintenanceStatus.Planned, savedMaintenance.Status);
        var savedDetail = await context.MaintenanceDetails.AsNoTracking().SingleAsync(item => item.MaintenanceId == maintenance.Id);
        Assert.Equal(detailId, savedDetail.DetailId);
        Assert.Equal("Работа", savedDetail.Name);
        Assert.Equal(1000, savedDetail.Price);
        Assert.Equal(employee.Id, savedDetail.EmployeeId);
        counter.VerifyNoOtherCalls();
        scheduler.VerifyNoOtherCalls();
        consumption.VerifyNoOtherCalls();
    }

    private static Task ExecuteAsync(
        MaintenanceTestScope scope,
        string command,
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId)
    {
        return command switch
        {
            "intake" => scope.SaveIntake(userId, organizationId, maintenanceId, new SaveMaintenanceIntakeDto { Mileage = 100 }),
            "update" => scope.Update(userId, organizationId, maintenanceId, new UpdateMaintenanceDto
            {
                Status = MaintenanceStatus.Planned,
                Type = MaintenanceType.Maintenance,
                PlannedDate = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
                PlannedStartTime = TimeSpan.FromHours(9),
                PlannedEndTime = TimeSpan.FromHours(10),
                CompletedDate = null,
                Location = null,
                Performer = "Мастер",
                Mileage = null,
                Comment = null,
                Details = [new CreateMaintenanceDetailDto { DetailId = detailId, Name = "Работа", Price = 1000, EmployeeName = "Мастер" }]
            }),
            "workflow" => scope.ChangeWorkflow(userId, organizationId, maintenanceId,
                new MaintenanceWorkflowActionDto { Action = "cancel" }),
            "schedule" => scope.UpdatePlannedDate(userId, organizationId, maintenanceId,
                new UpdateMaintenancePlannedDateDto { PlannedDate = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc) }),
            "delete" => scope.Delete(userId, organizationId, maintenanceId),
            "claim" => scope.Get<IMaintenanceWorkExecutionService>().ClaimAsync(userId, organizationId, maintenanceId, detailId),
            "execute" => scope.Get<IMaintenanceWorkExecutionService>().ExecuteAsync(
                userId, organizationId, maintenanceId, detailId,
                new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "start" }),
            "return_for_rework" => scope.Get<IMaintenanceWorkExecutionService>().ReturnForReworkAsync(
                userId, organizationId, maintenanceId, detailId,
                new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Reason = "Повторная проверка" }),
            "add_report" => scope.Get<IMaintenanceWorkReportService>().AddReportAsync(
                userId, organizationId, maintenanceId, detailId,
                new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "comment", Text = "Проверено" }),
            "decide_report" => scope.Get<IMaintenanceWorkReportService>().DecideReportAsync(
                userId, organizationId, maintenanceId, Guid.NewGuid(),
                new DecideMaintenanceWorkReportDto { Decision = "approved", Price = 100, VatRateCode = "without_vat" }),
            "adjust_time" => scope.Get<IMaintenanceWorkTimeService>().AdjustTimeAsync(
                userId, organizationId, maintenanceId, detailId,
                new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 5, Reason = "Уточнение" }),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Неизвестная команда теста.")
        };
    }

    private sealed class DeleteAfterMaintenanceLockInterceptor(MaintenanceDatabaseFixture fixture, Guid maintenanceId) : DbCommandInterceptor
    {
        private int _didDeleteMaintenance;

        public bool DidDeleteMaintenance => Volatile.Read(ref this._didDeleteMaintenance) == 1;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT 1 FROM \"Maintenance\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && Interlocked.Exchange(ref this._didDeleteMaintenance, 1) == 0)
            {
                await using var concurrentContext = fixture.CreateContext();
                var affectedRows = await concurrentContext.Maintenance
                    .Where(item => item.Id == maintenanceId && !item.IsDeleted)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsDeleted, true), cancellationToken);
                Assert.Equal(1, affectedRows);
            }

            return result;
        }
    }
}
