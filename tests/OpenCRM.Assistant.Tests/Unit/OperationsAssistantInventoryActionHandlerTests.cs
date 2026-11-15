using System.Text.Json;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Infrastructure.Assistant.Actions;
using Moq;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class OperationsAssistantInventoryActionHandlerTests
{
    [Fact]
    public async Task InventoryAdjustmentUsesPreparedStockVersionAndDoesNotMutateBeforeExecution()
    {
        var organizationId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        var updatedAt = new DateTime(2026, 11, 3, 12, 30, 0, DateTimeKind.Utc);
        var actor = new AssistantActor("user", organizationId, "web");
        var inventoryQuery = new Mock<IInventoryQueryService>();
        var stockCommand = new Mock<IStockCommandService>();
        var executionGuard = new Mock<IAssistantExecutionGuard>();
        AdjustOrganizationMaterialStockDto? capturedRequest = null;
        var executionOrder = new List<string>();

        inventoryQuery.Setup(service => service.GetAsync(
                actor.UserId,
                organizationId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<Guid?>()))
            .ReturnsAsync(new OrganizationMaterialInventoryDto
            {
                Stocks =
                [
                    new OrganizationMaterialStockDto
                    {
                        Id = stockId,
                        Name = "Моторное масло",
                        Unit = "л",
                        QuantityOnHand = 5,
                        AverageUnitCost = 200,
                        UpdatedAt = updatedAt
                    }
                ]
            });
        executionGuard.Setup(guard => guard.EnsureAllowedAsync(actor, "inventory_adjust", It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("guard"))
            .Returns(Task.CompletedTask);
        stockCommand.Setup(service => service.AdjustAsync(
                actor.UserId,
                organizationId,
                stockId,
                It.IsAny<AdjustOrganizationMaterialStockDto>()))
            .Callback<string, Guid, Guid, AdjustOrganizationMaterialStockDto>((_, _, _, request) =>
            {
                executionOrder.Add("mutation");
                capturedRequest = request;
            })
            .ReturnsAsync(new OrganizationMaterialStockDto());

        var handler = new OperationsAssistantInventoryActionHandler(
            inventoryQuery.Object,
            stockCommand.Object,
            Mock.Of<ICatalogQueryService>(),
            executionGuard.Object,
            AssistantOptionsTestFixture.Options);
        var prepared = await handler.PrepareAsync(
            OperationsAssistantInventoryActionHandlerTests.Command(
                "inventory_adjust", "{\"name\":\"Моторное масло\",\"quantityOnHand\":4,\"comment\":\"Пересчет\"}"),
            actor,
            new AssistantActionSelection(),
            CancellationToken.None);

        Assert.Empty(executionOrder);
        stockCommand.Verify(service => service.AdjustAsync(
            actor.UserId,
            organizationId,
            stockId,
            It.IsAny<AdjustOrganizationMaterialStockDto>()), Times.Never);

        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["guard", "mutation"], executionOrder);
        Assert.NotNull(capturedRequest);
        Assert.Equal(updatedAt, capturedRequest.ExpectedUpdatedAt);
        Assert.Equal(4, capturedRequest.QuantityOnHand);
    }

    private static AiPilotCommand Command(string action, string parametersJson)
    {
        using var document = JsonDocument.Parse(parametersJson);

        return new AiPilotCommand
        {
            Action = action,
            Parameters = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone())
        };
    }
}
