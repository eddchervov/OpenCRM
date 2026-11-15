using System.Text.Json;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Infrastructure.Assistant.Actions;
using Moq;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class OperationsAssistantCatalogActionHandlerTests
{
    [Fact]
    public async Task PrepareCatalogCreateDoesNotMutateUntilExecuteAndGuardsBeforeMutation()
    {
        var catalogQuery = new Mock<ICatalogQueryService>();
        var catalogCommand = new Mock<ICatalogCommandService>();
        var employeeService = new Mock<IOrganizationEmployeeService>();
        var shiftService = new Mock<IOrganizationShiftService>();
        var executionGuard = new Mock<IAssistantExecutionGuard>();
        var executionOrder = new List<string>();
        var organizationId = Guid.NewGuid();
        var actor = new AssistantActor("user", organizationId, "web");
        var catalog = new OrganizationScenarioCatalogDto { OrganizationId = organizationId };
        catalogQuery.Setup(service => service.GetAsync(actor.UserId, organizationId)).ReturnsAsync(catalog);
        executionGuard.Setup(guard => guard.EnsureAllowedAsync(actor, "catalog_create", It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("guard"))
            .Returns(Task.CompletedTask);
        catalogCommand.Setup(service => service.CreateAsync(actor.UserId, organizationId, It.IsAny<CreateOrganizationScenarioCatalogItemDto>()))
            .Callback(() => executionOrder.Add("mutation"))
            .ReturnsAsync(new OrganizationScenarioCatalogItemDto());
        var handler = new OperationsAssistantCatalogEmployeeShiftActionHandler(
            catalogQuery.Object,
            catalogCommand.Object,
            employeeService.Object,
            shiftService.Object,
            executionGuard.Object,
            AssistantOptionsTestFixture.Options);

        var prepared = await handler.PrepareAsync(
            Command("catalog_create", "{\"catalogKind\":\"service\",\"name\":\"Полировка\"}"),
            actor,
            new AssistantActionSelection(),
            CancellationToken.None);

        Assert.Empty(executionOrder);
        catalogCommand.Verify(service => service.CreateAsync(actor.UserId, organizationId, It.IsAny<CreateOrganizationScenarioCatalogItemDto>()), Times.Never);

        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["guard", "mutation"], executionOrder);
    }

    [Fact]
    public async Task PrepareCatalogUpdateRequiresChoiceWhenCatalogNamesAreAmbiguous()
    {
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var catalogQuery = new Mock<ICatalogQueryService>();
        catalogQuery.Setup(service => service.GetAsync("user", organizationId)).ReturnsAsync(new OrganizationScenarioCatalogDto
        {
            OrganizationId = organizationId,
            Services =
            [
                new OrganizationScenarioCatalogItemDto { Id = firstItemId, Name = "Полировка", KindCode = "service" },
                new OrganizationScenarioCatalogItemDto { Id = secondItemId, Name = "Полировка", KindCode = "service" }
            ]
        });
        var handler = new OperationsAssistantCatalogEmployeeShiftActionHandler(
            catalogQuery.Object,
            Mock.Of<ICatalogCommandService>(),
            Mock.Of<IOrganizationEmployeeService>(),
            Mock.Of<IOrganizationShiftService>(),
            Mock.Of<IAssistantExecutionGuard>(),
            AssistantOptionsTestFixture.Options);

        var exception = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            Command("catalog_update", "{\"catalogKind\":\"service\",\"name\":\"Полировка\"}"),
            new AssistantActor("user", organizationId, "web"),
            new AssistantActionSelection(),
            CancellationToken.None));

        Assert.Equal("name", exception.Field);
        Assert.Equal([firstItemId.ToString(), secondItemId.ToString()], exception.Choices.Select(choice => choice.Value));
    }

    [Fact]
    public async Task CreateServiceBundle_OffersDuplicateActiveServiceChoicesAndUsesSelectedId()
    {
        var organizationId = Guid.NewGuid();
        var firstServiceId = Guid.NewGuid();
        var secondServiceId = Guid.NewGuid();
        var inactiveServiceId = Guid.NewGuid();
        var catalogQuery = new Mock<ICatalogQueryService>();
        catalogQuery.Setup(service => service.GetAsync("user", organizationId)).ReturnsAsync(new OrganizationScenarioCatalogDto
        {
            OrganizationId = organizationId,
            Services =
            [
                new OrganizationScenarioCatalogItemDto { Id = firstServiceId, Name = "Воск", KindCode = "service", IsActive = true, UnitPrice = 1000 },
                new OrganizationScenarioCatalogItemDto { Id = secondServiceId, Name = "Воск", KindCode = "service", IsActive = true, UnitPrice = 1500 },
                new OrganizationScenarioCatalogItemDto { Id = inactiveServiceId, Name = "Воск", KindCode = "service", IsActive = false, UnitPrice = 2000 }
            ]
        });
        CreateOrganizationScenarioCatalogItemDto? captured = null;
        var catalogCommand = new Mock<ICatalogCommandService>();
        catalogCommand.Setup(service => service.CreateAsync("user", organizationId, It.IsAny<CreateOrganizationScenarioCatalogItemDto>()))
            .Callback<string, Guid, CreateOrganizationScenarioCatalogItemDto>((_, _, dto) => captured = dto)
            .ReturnsAsync(new OrganizationScenarioCatalogItemDto());
        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new OperationsAssistantCatalogEmployeeShiftActionHandler(
            catalogQuery.Object,
            catalogCommand.Object,
            Mock.Of<IOrganizationEmployeeService>(),
            Mock.Of<IOrganizationShiftService>(),
            guard.Object,
            AssistantOptionsTestFixture.Options);
        var command = Command("catalog_create", "{\"catalogKind\":\"service_bundle\",\"name\":\"Комплекс\",\"serviceNames\":\"Воск\"}");
        var actor = new AssistantActor("user", organizationId, "web");
        var field = "bundleService:0:Воск";

        var clarification = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(),
            CancellationToken.None));

        Assert.Equal(field, clarification.Field);
        Assert.Equal([firstServiceId.ToString(), secondServiceId.ToString()], clarification.Choices.Select(choice => choice.Value));

        var prepared = await handler.PrepareAsync(
            command,
            actor,
            new AssistantActionSelection(CatalogChoices: new Dictionary<string, string> { [field] = secondServiceId.ToString() }),
            CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal([secondServiceId], captured.ServiceIds);
        catalogCommand.Verify(service => service.CreateAsync("user", organizationId, It.IsAny<CreateOrganizationScenarioCatalogItemDto>()), Times.Once);
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
