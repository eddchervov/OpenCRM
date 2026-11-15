using Helpers;
using OpenCRM.Modules.Planning.Contracts;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;
using Npgsql;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceDefectRetryTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task UnlinkOnlyUpdateRetriesCommitWithoutDuplicatingLinksOrHistory(bool failAfterCommit, int expectedAttempts)
    {
        await using var seedContext = fixture.CreateContext();
        var work = MaintenanceDefectRetryTests.CreateWork();
        var defect = new VehicleDefect
        {
            Vehicle = work.Vehicle,
            Organization = work.Vehicle.Organization!,
            User = work.User,
            Title = "Неисправность для повторной попытки"
        };
        MaintenanceDefectRetryTests.AddDefectLink(work, defect);
        seedContext.Maintenance.Add(work);
        seedContext.VehicleDefects.Add(defect);
        await seedContext.SaveChangesAsync();
        var originalVersion = work.Version;
        var interceptor = new CommitFailureInterceptor(failAfterCommit);
        await using var context = MaintenanceDefectRetryTests.CreateRetryContext(seedContext, interceptor);
        var workflowSyncCount = 0;
        using var service = MaintenanceDefectRetryTests.CreateService(context, () =>
        {
            Assert.NotNull(context.Database.CurrentTransaction);
            workflowSyncCount++;
        });
        var request = MaintenanceDefectRetryTests.CreateUpdateRequest(work);
        request.LinkedDefectIds = [];

        await service.Update(work.User.Id, work.Vehicle.OrganizationId, work.Id, request);

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.NotEqual(originalVersion, savedWork.Version);
        Assert.Equal(expectedAttempts, workflowSyncCount);
        Assert.Equal(expectedAttempts, interceptor.CommitAttempts);
        Assert.False(await context.MaintenanceDefects.AnyAsync(value => value.MaintenanceId == work.Id));
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value =>
            value.MaintenanceId == work.Id && value.EventType == "defect_unlinked").ToListAsync());
        Assert.Single(await context.VehicleHistoryEvents.Where(value =>
            value.VehicleId == work.VehicleId && value.EventType == "defect_unlinked").ToListAsync());
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task NoOpUpdateRetriesCommitWithoutChangingVersionOrCreatingHistory()
    {
        await using var seedContext = fixture.CreateContext();
        var work = MaintenanceDefectRetryTests.CreateWork();
        seedContext.Maintenance.Add(work);
        await seedContext.SaveChangesAsync();
        var originalVersion = work.Version;
        seedContext.ChangeTracker.Clear();
        var persistedWork = await seedContext.Maintenance
            .Include(value => value.Details)
            .SingleAsync(value => value.Id == work.Id);
        var interceptor = new CommitFailureInterceptor(failAfterCommit: true);
        await using var context = MaintenanceDefectRetryTests.CreateRetryContext(seedContext, interceptor);
        var workflowSyncCount = 0;
        using var service = MaintenanceDefectRetryTests.CreateService(context, () =>
        {
            Assert.NotNull(context.Database.CurrentTransaction);
            workflowSyncCount++;
        });

        await service.Update(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            MaintenanceDefectRetryTests.CreateUpdateRequest(persistedWork));

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.Equal(2, workflowSyncCount);
        Assert.Equal(2, interceptor.CommitAttempts);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateServicesRetriesTransientCommitWithoutDuplicatingHistory(bool failAfterCommit)
    {
        await using var seedContext = fixture.CreateContext();
        var work = MaintenanceDefectRetryTests.CreateWork();
        await seedContext.Maintenance.AddAsync(work);
        await seedContext.SaveChangesAsync();
        var originalVersion = work.Version;
        var originalDetail = Assert.Single(work.Details);
        var interceptor = new CommitFailureInterceptor(failAfterCommit);
        await using var context = MaintenanceDefectRetryTests.CreateRetryContext(seedContext, interceptor);
        using var service = MaintenanceDefectRetryTests.CreateService(context, () => { });

        await service.UpdateServices(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id,
            new UpdateMaintenanceServicesDto
            {
                Version = originalVersion,
                Details =
                [
                    new CreateMaintenanceDetailDto
                    {
                        DetailId = originalDetail.DetailId,
                        Name = "Расширенная диагностика",
                        Price = 1000,
                        VatRateCode = "without_vat",
                        Quantity = 1
                    }
                ]
            });

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        Assert.NotEqual(originalVersion, savedWork.Version);
        Assert.Equal("Расширенная диагностика", Assert.Single(savedWork.Details).Name);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
        Assert.Equal(failAfterCommit ? 1 : 2, interceptor.CommitAttempts);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateVatRatesRetriesTransientCommitWithoutDuplicatingHistory(bool failAfterCommit)
    {
        await using var seedContext = fixture.CreateContext();
        var work = MaintenanceDefectRetryTests.CreateWork();
        var detail = Assert.Single(work.Details);
        detail.VatRateCode = "without_vat";
        await seedContext.Maintenance.AddAsync(work);
        await seedContext.SaveChangesAsync();
        var originalVersion = work.Version;
        var interceptor = new CommitFailureInterceptor(failAfterCommit);
        await using var context = MaintenanceDefectRetryTests.CreateRetryContext(seedContext, interceptor);
        using var service = MaintenanceDefectRetryTests.CreateService(context, () => { });

        await service.UpdateVatRates(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id,
            new UpdateMaintenanceVatRatesDto
            {
                Version = originalVersion,
                Lines = [new MaintenanceVatRateDto { LineIndex = 0, VatRateCode = "20" }]
            });

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == work.Id);
        Assert.NotEqual(originalVersion, savedWork.Version);
        Assert.Equal("20", Assert.Single(savedWork.Details).VatRateCode);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
        Assert.Equal(failAfterCommit ? 1 : 2, interceptor.CommitAttempts);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssignEmployeeRetriesTransientCommitWithoutDuplicatingHistory(bool failAfterCommit)
    {
        await using var seedContext = fixture.CreateContext();
        var work = MaintenanceDefectRetryTests.CreateWork();
        var employee = new OrganizationEmployee
        {
            Organization = work.Vehicle.Organization!,
            Name = "Мастер повторного назначения"
        };
        await seedContext.Maintenance.AddAsync(work);
        await seedContext.OrganizationEmployees.AddAsync(employee);
        await seedContext.SaveChangesAsync();
        var originalVersion = work.Version;
        var interceptor = new CommitFailureInterceptor(failAfterCommit);
        await using var context = MaintenanceDefectRetryTests.CreateRetryContext(seedContext, interceptor);
        using var service = MaintenanceDefectRetryTests.CreateService(context, () => { });

        await service.AssignEmployee(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id,
            new AssignMaintenanceEmployeeDto { EmployeeId = employee.Id, Version = originalVersion });

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.NotEqual(originalVersion, savedWork.Version);
        Assert.Equal(employee.Id, savedWork.EmployeeId);
        Assert.Equal(employee.Name, savedWork.Performer);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
        Assert.Equal(failAfterCommit ? 1 : 2, interceptor.CommitAttempts);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("services")]
    [InlineData("vat")]
    [InlineData("assignment")]
    public async Task NoOpCommandRetriesAfterCommitWhenVersionWasNotSaved(string command)
    {
        await using var seedContext = fixture.CreateContext();
        var work = MaintenanceDefectRetryTests.CreateWork();
        var detail = Assert.Single(work.Details);
        detail.VatRateCode = "without_vat";
        detail.MaterialUnitCost = 0;
        detail.MaterialTotalCost = 0;
        var employee = new OrganizationEmployee
        {
            Organization = work.Vehicle.Organization!,
            Name = "Мастер без изменений"
        };
        work.EmployeeId = employee.Id;
        work.Performer = employee.Name;
        await seedContext.Maintenance.AddAsync(work);
        await seedContext.OrganizationEmployees.AddAsync(employee);
        await seedContext.SaveChangesAsync();
        var originalVersion = work.Version;
        var interceptor = new CommitFailureInterceptor(failAfterCommit: true);
        await using var context = MaintenanceDefectRetryTests.CreateRetryContext(seedContext, interceptor);
        using var service = MaintenanceDefectRetryTests.CreateService(context, () => { });

        switch (command)
        {
            case "services":
                await service.UpdateServices(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id,
                    new UpdateMaintenanceServicesDto
                    {
                        Version = originalVersion,
                        Details =
                        [
                            new CreateMaintenanceDetailDto
                            {
                                DetailId = detail.DetailId,
                                Name = detail.Name,
                                Price = detail.Price,
                                VatRateCode = detail.VatRateCode,
                                Quantity = detail.Quantity
                            }
                        ]
                    });
                break;
            case "vat":
                await service.UpdateVatRates(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id,
                    new UpdateMaintenanceVatRatesDto
                    {
                        Version = originalVersion,
                        Lines = [new MaintenanceVatRateDto { LineIndex = 0, VatRateCode = detail.VatRateCode! }]
                    });
                break;
            case "assignment":
                await service.AssignEmployee(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id,
                    new AssignMaintenanceEmployeeDto { EmployeeId = employee.Id, Version = originalVersion });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }

        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.Equal(2, interceptor.CommitAttempts);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
        Assert.Null(context.Database.CurrentTransaction);
    }

    private static void AddDefectLink(MaintenanceEntity work, VehicleDefect defect)
    {
        work.LinkedDefects.Add(new MaintenanceDefect
        {
            Maintenance = work,
            VehicleDefect = defect,
            Organization = work.Vehicle.Organization!,
            User = work.User
        });
    }

    private static OpenCRMContext CreateRetryContext(OpenCRMContext seedContext, CommitFailureInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(seedContext.Database.GetConnectionString(), database => database.EnableRetryOnFailure())
            .AddInterceptors(interceptor)
            .Options;

        return new OpenCRMContext(options);
    }

    private static MaintenanceTestScope CreateService(OpenCRMContext context, Action syncWorkflow)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(value => value.SyncMaintenanceWorkflowAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .Callback(syncWorkflow)
            .Returns(Task.CompletedTask);
        var consumption = new Mock<IMaintenanceConsumptionService>();
        consumption.Setup(value => value.SynchronizeAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
            .ReturnsAsync(new MaintenanceConsumptionResult(0, []));

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(value => value.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.MembersModule))
            .ReturnsAsync(true);

        return new MaintenanceTestScope(
            new MaintenanceRepository(context),
            Mock.Of<IVehicleCounterCommandService>(),
            clock.Object,
            context,
            subscriptions.Object,
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            consumption.Object,
            scheduler: scheduler.Object);
    }

    private static MaintenanceEntity CreateWork()
    {
        var user = new ApplicationUser
        {
            Email = $"defect-retry-{Guid.NewGuid():N}@example.test",
            UserName = $"defect-retry-{Guid.NewGuid():N}"
        };
        var organization = new Organization
        {
            Name = "Повторное обновление связи неисправности",
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
            MaintenanceWorkflowMode = "simple",
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            RegistrationPlate = "А777АА154"
        };
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = user,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Date = date.AddDays(1),
            PlannedDate = date.AddDays(1),
            PlannedEndDate = date.AddDays(1),
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            DateTimeCreation = date.AddDays(-1),
            Details = [new() { Name = "Диагностика", Price = 1000 }]
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

    private sealed class CommitFailureInterceptor(bool failAfterCommit) : DbTransactionInterceptor
    {
        private bool _hasFailed;

        internal int CommitAttempts { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            this.CommitAttempts++;
            if (!failAfterCommit)
            {
                this.FailOnce();
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (failAfterCommit)
            {
                this.FailOnce();
            }

            return Task.CompletedTask;
        }

        private void FailOnce()
        {
            if (this._hasFailed)
            {
                return;
            }

            this._hasFailed = true;
            throw new PostgresException("Временный сбой фиксации изменений.", "ERROR", "ERROR", "40001");
        }
    }
}
