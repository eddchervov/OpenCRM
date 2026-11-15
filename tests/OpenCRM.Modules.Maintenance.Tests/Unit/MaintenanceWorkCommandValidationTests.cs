using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Work;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceWorkCommandValidationTests
{
    [Fact]
    public async Task AdjustTimeRejectsMissingOutOfRangeAndNegativeResultInputsBeforeDatabaseAccess()
    {
        await using var context = CreateContext();
        var subscriptionService = CreateSubscriptionService();
        var service = new MaintenanceWorkTimeService(
            context, subscriptionService, Mock.Of<ISystemClock>());
        var userId = "user";
        var organizationId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var invalidCommands = new[]
        {
            new AdjustMaintenanceWorkTimeDto { Minutes = 1, Reason = "Причина" },
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Reason = "Причина" },
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 10081, Reason = "Причина" },
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = -10081, Reason = "Причина" },
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 1 },
            new AdjustMaintenanceWorkTimeDto
            {
                CommandId = Guid.NewGuid(), Minutes = 1, Reason = new string('а', 1001)
            }
        };

        foreach (var command in invalidCommands)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustTimeAsync(
                userId, organizationId, maintenanceId, detailId, command));
        }
    }

    [Fact]
    public async Task ReturnForReworkRequiresCommandIdAndBoundedReasonBeforeDatabaseAccess()
    {
        await using var context = CreateContext();
        var subscriptionService = CreateSubscriptionService();
        var service = new MaintenanceWorkExecutionService(
            context, subscriptionService, Mock.Of<ISystemClock>());
        var userId = "user";
        var organizationId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var invalidCommands = new[]
        {
            new MaintenanceWorkCommandDto { Action = "return", Reason = "Причина" },
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "return" },
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(), Action = "return", Reason = new string('а', 1001)
            }
        };

        foreach (var command in invalidCommands)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReturnForReworkAsync(
                userId, organizationId, maintenanceId, detailId, command));
        }
    }

    private static OpenCRMContext CreateContext() => new(new DbContextOptionsBuilder<OpenCRMContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
        .Options);

    private static IOrganizationSubscriptionService CreateSubscriptionService()
    {
        var service = new Mock<IOrganizationSubscriptionService>();
        service.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return service.Object;
    }
}
