using System.Text.Json;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Infrastructure.Assistant.Actions;
using Moq;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class OperationsAssistantWorkActionHandlerTests
{
    [Fact]
    public async Task WorkStart_ClarifiesDuplicateNamesAndConsumesSelectedWorkId()
    {
        var maintenanceId = Guid.NewGuid();
        var firstDetailId = Guid.NewGuid();
        var secondDetailId = Guid.NewGuid();
        var (handler, workExecution) = CreateHandler(maintenanceId,
        [
            Progress(firstDetailId, "Замена масла", 1),
            Progress(secondDetailId, "Замена масла", 1)
        ]);
        var actor = new AssistantActor("user", Guid.NewGuid(), "web");
        var command = Command("work_start", "{}");

        var clarification = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(maintenanceId),
            CancellationToken.None));

        Assert.Equal("workName", clarification.Field);
        Assert.Equal([firstDetailId.ToString(), secondDetailId.ToString()], clarification.Choices.Select(choice => choice.Value));

        var prepared = await handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(maintenanceId, new Dictionary<string, string> { ["workName"] = secondDetailId.ToString() }),
            CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        workExecution.Verify(service => service.ExecuteAsync(
            actor.UserId,
            actor.OrganizationId,
            maintenanceId,
            secondDetailId,
            It.Is<MaintenanceWorkCommandDto>(dto => dto.Action == "start")), Times.Once);
    }

    [Fact]
    public async Task WorkDecision_OffersReportChoicesWithoutReportNumberAndConsumesSelectedId()
    {
        var maintenanceId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var firstReportId = Guid.NewGuid();
        var secondReportId = Guid.NewGuid();
        var (handler, _, workReports) = CreateDecisionHandler(maintenanceId, detailId,
        [
            Report(firstReportId, maintenanceId, detailId, "Согласовать дополнительную работу"),
            Report(secondReportId, maintenanceId, detailId, "Заменить повреждённую деталь")
        ]);
        var actor = new AssistantActor("user", Guid.NewGuid(), "web");
        var command = Command("work_decision", "{\"decision\":\"rejected\",\"reason\":\"Не согласовано\"}");

        var clarification = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(maintenanceId),
            CancellationToken.None));

        Assert.Equal("reportNumber", clarification.Field);
        Assert.Equal([firstReportId.ToString(), secondReportId.ToString()], clarification.Choices.Select(choice => choice.Value));

        var prepared = await handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(maintenanceId, new Dictionary<string, string> { ["reportNumber"] = secondReportId.ToString() }),
            CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        workReports.Verify(service => service.DecideReportAsync(
            actor.UserId,
            actor.OrganizationId,
            maintenanceId,
            secondReportId,
            It.Is<DecideMaintenanceWorkReportDto>(dto => dto.Decision == "rejected")), Times.Once);
    }

    [Fact]
    public async Task WorkSnapshotIgnoresComputedActiveTime()
    {
        var maintenanceId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var workQuery = new Mock<IMaintenanceWorkQueryService>();
        var callCount = 0;
        workQuery.Setup(service => service.GetProgressAsync("user", It.IsAny<Guid>(), maintenanceId))
            .Returns(() =>
            {
                var progress = Progress(detailId, "Диагностика", 1);
                progress = new MaintenanceWorkProgressDto
                {
                    DetailId = progress.DetailId,
                    Name = progress.Name,
                    Status = progress.Status,
                    Version = progress.Version,
                    ActiveTime = TimeSpan.FromMinutes(++callCount)
                };

                return Task.FromResult<IReadOnlyList<MaintenanceWorkProgressDto>>([progress]);
            });
        var handler = new OperationsAssistantWorkActionHandler(
            MaintenanceQuery(maintenanceId).Object,
            Mock.Of<OpenCRM.Modules.Vehicles.Contracts.IVehicleOptionQueryService>(),
            Mock.Of<OpenCRM.Modules.Vehicles.Contracts.IQuickBookingVehicleLookupService>(),
            workQuery.Object,
            Mock.Of<IMaintenanceWorkExecutionService>(),
            Mock.Of<IMaintenanceWorkTimeService>(),
            Mock.Of<IMaintenanceWorkReportService>(),
            Mock.Of<OpenCRM.Modules.Inventory.Contracts.IOperationMaterialQueryService>(),
            Mock.Of<OpenCRM.Modules.Inventory.Contracts.IOperationMaterialCommandService>(),
            Guard().Object,
            AssistantOptionsTestFixture.Options);
        var actor = new AssistantActor("user", Guid.NewGuid(), "web");
        var command = Command("work_start", "{}");

        var first = await handler.PrepareAsync(command, actor, new AssistantActionSelection(maintenanceId), CancellationToken.None);
        var second = await handler.PrepareAsync(command, actor, new AssistantActionSelection(maintenanceId), CancellationToken.None);

        Assert.Equal(first.Snapshot, second.Snapshot);
    }

    private static (OperationsAssistantWorkActionHandler Handler, Mock<IMaintenanceWorkExecutionService> Execution) CreateHandler(
        Guid maintenanceId,
        IReadOnlyList<MaintenanceWorkProgressDto> progress)
    {
        var maintenanceQuery = MaintenanceQuery(maintenanceId);
        var workQuery = new Mock<IMaintenanceWorkQueryService>();
        workQuery.Setup(service => service.GetProgressAsync("user", It.IsAny<Guid>(), maintenanceId)).ReturnsAsync(progress);
        var execution = new Mock<IMaintenanceWorkExecutionService>();
        var guard = Guard();
        return (new OperationsAssistantWorkActionHandler(
            maintenanceQuery.Object,
            Mock.Of<OpenCRM.Modules.Vehicles.Contracts.IVehicleOptionQueryService>(),
            Mock.Of<OpenCRM.Modules.Vehicles.Contracts.IQuickBookingVehicleLookupService>(),
            workQuery.Object,
            execution.Object,
            Mock.Of<IMaintenanceWorkTimeService>(),
            Mock.Of<IMaintenanceWorkReportService>(),
            Mock.Of<OpenCRM.Modules.Inventory.Contracts.IOperationMaterialQueryService>(),
            Mock.Of<OpenCRM.Modules.Inventory.Contracts.IOperationMaterialCommandService>(),
            guard.Object,
            AssistantOptionsTestFixture.Options), execution);
    }

    private static (OperationsAssistantWorkActionHandler Handler, Mock<IMaintenanceWorkExecutionService> Execution, Mock<IMaintenanceWorkReportService> Reports) CreateDecisionHandler(
        Guid maintenanceId,
        Guid detailId,
        IReadOnlyList<MaintenanceWorkReportDto> reports)
    {
        var maintenanceQuery = MaintenanceQuery(maintenanceId);
        var workQuery = new Mock<IMaintenanceWorkQueryService>();
        workQuery.Setup(service => service.GetProgressAsync("user", It.IsAny<Guid>(), maintenanceId))
            .ReturnsAsync([Progress(detailId, "Диагностика", 1)]);
        workQuery.Setup(service => service.GetReportsAsync("user", It.IsAny<Guid>(), maintenanceId, null)).ReturnsAsync(reports);
        var execution = new Mock<IMaintenanceWorkExecutionService>();
        var workReports = new Mock<IMaintenanceWorkReportService>();
        workReports.Setup(service => service.DecideReportAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), maintenanceId, It.IsAny<Guid>(), It.IsAny<DecideMaintenanceWorkReportDto>()))
            .ReturnsAsync(new MaintenanceWorkReportDto());
        var handler = new OperationsAssistantWorkActionHandler(
            maintenanceQuery.Object,
            Mock.Of<OpenCRM.Modules.Vehicles.Contracts.IVehicleOptionQueryService>(),
            Mock.Of<OpenCRM.Modules.Vehicles.Contracts.IQuickBookingVehicleLookupService>(),
            workQuery.Object,
            execution.Object,
            Mock.Of<IMaintenanceWorkTimeService>(),
            workReports.Object,
            Mock.Of<OpenCRM.Modules.Inventory.Contracts.IOperationMaterialQueryService>(),
            Mock.Of<OpenCRM.Modules.Inventory.Contracts.IOperationMaterialCommandService>(),
            Guard().Object,
            AssistantOptionsTestFixture.Options);

        return (handler, execution, workReports);
    }

    private static Mock<IMaintenanceQueryService> MaintenanceQuery(Guid maintenanceId)
    {
        var query = new Mock<IMaintenanceQueryService>();
        query.Setup(service => service.GetById("user", It.IsAny<Guid?>(), maintenanceId))
            .ReturnsAsync(new GetMaintenanceByIdDto
            {
                Id = maintenanceId,
                Status = MaintenanceStatus.InProgress,
                Type = MaintenanceType.Maintenance,
                Date = DateTime.Today,
                PlannedDate = DateTime.Today,
                CompletedDate = null,
                Location = null,
                Performer = null,
                Mileage = null,
                Comment = null,
                Price = null,
                PhotoIds = [],
                MaintenanceDetails = [],
                LinkedDefects = []
            });
        return query;
    }

    private static MaintenanceWorkProgressDto Progress(Guid detailId, string name, int status) => new()
    {
        DetailId = detailId,
        Name = name,
        Status = status,
        Version = 1
    };

    private static MaintenanceWorkReportDto Report(Guid reportId, Guid maintenanceId, Guid detailId, string text) => new()
    {
        Id = reportId,
        MaintenanceId = maintenanceId,
        DetailId = detailId,
        Kind = "extra",
        Text = text
    };

    private static Mock<IAssistantExecutionGuard> Guard()
    {
        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return guard;
    }

    private static AiPilotCommand Command(string action, string parametersJson)
    {
        using var document = JsonDocument.Parse(parametersJson);
        return new AiPilotCommand
        {
            Action = action,
            Parameters = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
        };
    }
}
