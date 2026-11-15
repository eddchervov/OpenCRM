using System.Text.Json;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Defects;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Infrastructure.Assistant.Actions;
using Moq;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class OperationsAssistantVehicleRecordActionHandlerTests
{
    [Fact]
    public async Task DefectCreate_ResolvesVehicleFromCommandRegistrationPlate()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var vehicleQuery = CreateVehicleQuery(organizationId, vehicleId);
        var defectService = new Mock<IVehicleDefectService>();
        defectService.Setup(service => service.GetAsync("user", organizationId, 1, 100, null, null, vehicleId, null))
            .ReturnsAsync(EmptyDefects(vehicleId));
        CreateVehicleDefectDto? captured = null;
        defectService.Setup(service => service.CreateAsync("user", organizationId, It.IsAny<CreateVehicleDefectDto>()))
            .Callback<string, Guid, CreateVehicleDefectDto>((_, _, dto) => captured = dto)
            .ReturnsAsync(Guid.NewGuid());
        var handler = CreateHandler(vehicleQuery.Object, defectService.Object);
        var command = Command("defect_create", "A123AA", "{\"name\":\"Скол на двери\"}");
        var actor = new AssistantActor("user", organizationId, "web");

        var prepared = await handler.PrepareAsync(command, actor, new AssistantActionSelection(), CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(vehicleId, captured.VehicleId);
        Assert.Equal("Скол на двери", captured.Title);
        vehicleQuery.Verify(service => service.GetOptionsAsync(organizationId, "A123AA", null, 1000), Times.Once);
    }

    [Fact]
    public async Task CounterAdd_ResolvesVehicleFromCommandRegistrationPlate()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var vehicleQuery = CreateVehicleQuery(organizationId, vehicleId);
        var counterQuery = new Mock<IVehicleCounterQueryService>();
        counterQuery.Setup(service => service.GetCounterHistoryAsync("user", organizationId, vehicleId, 100))
            .ReturnsAsync(Array.Empty<VehicleCounterEntryDto>());
        UpdateVehicleCounterDto? captured = null;
        var counterCommand = new Mock<IVehicleCounterCommandService>();
        counterCommand.Setup(service => service.UpdateCounterAsync("user", organizationId, It.IsAny<UpdateVehicleCounterDto>()))
            .Callback<string, Guid?, UpdateVehicleCounterDto>((_, _, dto) => captured = dto)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(vehicleQuery.Object, counterQuery: counterQuery.Object, counterCommand: counterCommand.Object);
        var command = Command("counter_add", "A123AA", "{\"value\":123456}");
        var actor = new AssistantActor("user", organizationId, "web");

        var prepared = await handler.PrepareAsync(command, actor, new AssistantActionSelection(), CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(vehicleId, captured.VehicleId);
        Assert.Equal(123456, captured.Value);
        vehicleQuery.Verify(service => service.GetOptionsAsync(organizationId, "A123AA", null, 1000), Times.Once);
    }

    [Fact]
    public async Task CounterRemove_OffersEntryChoicesWithoutEntryNumberAndConsumesSelectedId()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var vehicleQuery = CreateVehicleQuery(organizationId, vehicleId);
        var counterQuery = new Mock<IVehicleCounterQueryService>();
        counterQuery.Setup(service => service.GetCounterHistoryAsync("user", organizationId, vehicleId, 100))
            .ReturnsAsync(new VehicleCounterEntryDto[]
            {
                new()
                {
                    Id = entryId,
                    VehicleId = vehicleId,
                    Value = 123456,
                    RecordedAt = DateTime.UtcNow,
                    CanManage = true
                }
            });
        var counterCommand = new Mock<IVehicleCounterCommandService>();
        counterCommand.Setup(service => service.DeleteCounterEntryAsync("user", organizationId, entryId))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(vehicleQuery.Object, counterQuery: counterQuery.Object, counterCommand: counterCommand.Object);
        var command = Command("counter_remove", "A123AA", "{}");
        var actor = new AssistantActor("user", organizationId, "web");

        var clarification = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(),
            CancellationToken.None));

        Assert.Equal("entryNumber", clarification.Field);
        Assert.Equal([entryId.ToString()], clarification.Choices.Select(choice => choice.Value));

        var prepared = await handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(CatalogChoices: new Dictionary<string, string> { ["entryNumber"] = entryId.ToString() }),
            CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        counterCommand.Verify(service => service.DeleteCounterEntryAsync("user", organizationId, entryId), Times.Once);
    }

    private static OperationsAssistantVehicleRecordActionHandler CreateHandler(
        IVehicleOptionQueryService vehicleQuery,
        IVehicleDefectService? defectService = null,
        IVehicleCounterQueryService? counterQuery = null,
        IVehicleCounterCommandService? counterCommand = null)
    {
        var lookup = new Mock<IQuickBookingVehicleLookupService>();
        lookup.Setup(service => service.NormalizePlateForSearch(It.IsAny<string?>()))
            .Returns<string?>(plate => plate?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant() ?? string.Empty);

        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new OperationsAssistantVehicleRecordActionHandler(
            vehicleQuery,
            lookup.Object,
            counterQuery ?? Mock.Of<IVehicleCounterQueryService>(),
            counterCommand ?? Mock.Of<IVehicleCounterCommandService>(),
            defectService ?? Mock.Of<IVehicleDefectService>(),
            guard.Object,
            AssistantOptionsTestFixture.Options);
    }

    private static Mock<IVehicleOptionQueryService> CreateVehicleQuery(Guid organizationId, Guid vehicleId)
    {
        var vehicleQuery = new Mock<IVehicleOptionQueryService>();
        vehicleQuery.Setup(service => service.GetOptionsAsync(organizationId, "A123AA", null, 1000))
            .ReturnsAsync(new[] { new VehicleOptionDto { Id = vehicleId, RegistrationPlate = "A123AA", DisplayName = "Lada · A123AA" } });
        return vehicleQuery;
    }

    private static VehicleDefectListResponse EmptyDefects(Guid vehicleId) => new()
    {
        Items = [],
        TotalCount = 0,
        Page = 1,
        PageSize = 100,
        AllCount = 0,
        OpenCount = 0,
        ResolvedCount = 0,
        HasMore = false,
        SelectedVehicleId = vehicleId
    };

    private static AiPilotCommand Command(string action, string plate, string parametersJson)
    {
        using var document = JsonDocument.Parse(parametersJson);
        return new AiPilotCommand
        {
            Action = action,
            RegistrationPlate = plate,
            Parameters = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone())
        };
    }
}
