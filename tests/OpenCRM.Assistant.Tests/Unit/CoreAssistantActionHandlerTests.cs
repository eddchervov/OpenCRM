using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Infrastructure.Assistant.Actions;
using Moq;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class CoreAssistantActionHandlerTests
{
    [Fact]
    public void Actions_ContainsOnlyTheCoreAllowlistedActions()
    {
        var handler = CreateHandler();

        Assert.Contains("visit", handler.Actions);
        Assert.Contains("service_add", handler.Actions);
        Assert.Contains("vehicle_create", handler.Actions);
        Assert.Contains("client_comment", handler.Actions);
        Assert.DoesNotContain("work_start", handler.Actions);
        Assert.Equal(handler.Actions.Count, handler.Actions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task PrepareAsync_ClarifiesUnknownActionsBeforeReadingBusinessData()
    {
        var handler = CreateHandler();
        var command = new AiPilotCommand { Action = "delete_everything" };

        var exception = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            command,
            new AssistantActor("user-1", Guid.NewGuid(), "web"),
            new AssistantActionSelection(),
            CancellationToken.None));

        Assert.Equal("action", exception.Field);
    }

    [Fact]
    public async Task PrepareAsync_RequiresAWorkLineBeforeCreatingAVisit()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.GetById("user-1", organizationId, vehicleId))
            .ReturnsAsync(CreateVehicle(vehicleId, organizationId));
        var catalogQuery = new Mock<ICatalogQueryService>();
        catalogQuery.Setup(service => service.GetAsync("user-1", organizationId))
            .ReturnsAsync(new OrganizationScenarioCatalogDto { OrganizationId = organizationId });
        var maintenanceCreate = new Mock<IMaintenanceCreateService>();
        var handler = CreateHandler(vehicleQuery.Object, catalogQuery.Object, maintenanceCreate.Object);
        var command = new AiPilotCommand
        {
            Action = "visit",
            RegistrationPlate = "A123AA",
            Parameters = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["vehicleId"] = System.Text.Json.JsonSerializer.SerializeToElement(vehicleId.ToString())
            }
        };

        var exception = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            command,
            new AssistantActor("user-1", organizationId, "web"),
            new AssistantActionSelection(),
            CancellationToken.None));

        Assert.Equal("services", exception.Field);
        maintenanceCreate.Verify(service => service.Create(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<OpenCRM.Modules.Maintenance.Contracts.Dtos.CreateMaintenanceDto>()), Times.Never);
    }

    [Fact]
    public async Task VisitWithUnknownPlate_CreatesVehicleOnlyAfterGuardThenCreatesMaintenance()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([]));
        var vehicleCommand = new Mock<IVehicleCommandService>();
        var mutationOrder = new List<string>();
        var maintenanceCreate = new Mock<IMaintenanceCreateService>();
        maintenanceCreate.Setup(service => service.Create("user-1", organizationId, vehicleId, It.IsAny<CreateMaintenanceDto>()))
            .Callback(() => mutationOrder.Add("maintenance"))
            .ReturnsAsync(maintenanceId);
        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<AssistantActor, string, CancellationToken>((_, action, _) => mutationOrder.Add($"guard:{action}"))
            .Returns(Task.CompletedTask);
        CreateVehicleDto? createdVehicle = null;
        vehicleCommand.Setup(service => service.Create("user-1", organizationId, It.IsAny<CreateVehicleDto>()))
            .Callback<string, Guid?, CreateVehicleDto>((_, _, dto) =>
            {
                createdVehicle = dto;
                mutationOrder.Add("vehicle");
            })
            .ReturnsAsync(vehicleId);
        var handler = CreateHandler(vehicleQuery: vehicleQuery.Object, maintenanceCreate: maintenanceCreate.Object, guard: guard.Object,
            vehicleCommand: vehicleCommand.Object);
        var actor = new AssistantActor("user-1", organizationId, "web");
        var command = VisitCommand("visit", price: 3000);

        var prepared = await handler.PrepareAsync(command, actor, new AssistantActionSelection(), CancellationToken.None);

        Assert.Contains(prepared.Changes, change => change.Contains("Создать автомобиль", StringComparison.Ordinal));
        Assert.DoesNotContain("vehicle", mutationOrder);
        Assert.DoesNotContain("maintenance", mutationOrder);
        Assert.Equal("guard:vehicle_create", mutationOrder.Single());

        mutationOrder.Clear();
        var result = await prepared.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(createdVehicle);
        Assert.Equal("A123AA", createdVehicle.Name);
        Assert.Equal("A123AA", createdVehicle.RegistrationPlate);
        Assert.Null(createdVehicle.ManualBrandName);
        Assert.Null(createdVehicle.ManualModelName);
        Assert.Equal(vehicleId, result.VehicleId);
        Assert.Equal(["guard:visit", "guard:vehicle_create", "vehicle", "guard:visit", "maintenance"], mutationOrder);
        maintenanceCreate.Verify(service => service.Create("user-1", organizationId, vehicleId,
            It.Is<CreateMaintenanceDto>(dto => dto.Status == OpenCRM.Domain.Enums.MaintenanceStatus.Completed)), Times.Once);
    }

    [Theory]
    [InlineData("2", "Пост 2")]
    [InlineData("Пост 2", "Пост 2")]
    [InlineData("2-й пост", "Пост 2")]
    [InlineData("Бокс 2", "Пост 2")]
    [InlineData("2", "Подъемник 2")]
    [InlineData("2", "Подъёмник 2")]
    [InlineData("Подъемник 2", "Подъёмник 2")]
    public async Task Booking_ResolvesPostNumberAndShowsDateTimeBeforeCreating(string location, string postName)
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var vehicle = CreateVehicle(vehicleId, organizationId);
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([vehicle]));
        vehicleQuery.Setup(service => service.GetById("user-1", organizationId, vehicleId)).ReturnsAsync(vehicle);
        var catalog = new Mock<ICatalogQueryService>();
        catalog.Setup(service => service.GetAsync("user-1", organizationId)).ReturnsAsync(new OrganizationScenarioCatalogDto
        {
            WorkPosts = [WorkPost("Подъемник 12"), WorkPost(postName), WorkPost("Пост 2", false)]
        });
        CreateMaintenanceDto? captured = null;
        var create = new Mock<IMaintenanceCreateService>();
        create.Setup(service => service.Create("user-1", organizationId, vehicleId, It.IsAny<CreateMaintenanceDto>()))
            .Callback<string, Guid?, Guid, CreateMaintenanceDto>((_, _, _, dto) => captured = dto)
            .ReturnsAsync(Guid.NewGuid());
        var handler = CreateHandler(vehicleQuery: vehicleQuery.Object, catalogQuery: catalog.Object, maintenanceCreate: create.Object);
        var plan = await handler.PrepareAsync(BookingCommand(location), new AssistantActor("user-1", organizationId, "web"), new(), CancellationToken.None);

        Assert.Null(captured);
        Assert.Contains($"Пост: {postName}", plan.Changes);
        Assert.Contains("Дата: 28.10.2026", plan.Changes);
        Assert.Contains("Начало: 16:00", plan.Changes);

        await plan.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(postName, captured.Location);
        Assert.Equal(new DateTime(2026, 10, 28), captured.PlannedDate);
        Assert.Equal(TimeSpan.FromHours(16), captured.PlannedStartTime);
    }

    [Fact]
    public async Task Booking_OffersActivePostsWhenRequestedNumberDoesNotExist()
    {
        var organizationId = Guid.NewGuid();
        var vehicle = CreateVehicle(Guid.NewGuid(), organizationId);
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([vehicle]));
        var existingPost = WorkPost("Пост 12");
        var inactivePost = WorkPost("Пост 2", false);
        var catalog = new Mock<ICatalogQueryService>();
        catalog.Setup(service => service.GetAsync("user-1", organizationId)).ReturnsAsync(new OrganizationScenarioCatalogDto
        {
            WorkPosts = [existingPost, inactivePost]
        });
        var handler = CreateHandler(vehicleQuery: vehicleQuery.Object, catalogQuery: catalog.Object);

        var error = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            BookingCommand("2"), new AssistantActor("user-1", organizationId, "web"), new(), CancellationToken.None));

        Assert.Equal("locationName", error.Field);
        Assert.Equal(existingPost.Id.ToString(), Assert.Single(error.Choices).Value);
    }

    [Fact]
    public async Task Booking_UsesExplicitChoiceForAmbiguousPostAndBlocksDeactivatedPost()
    {
        var organizationId = Guid.NewGuid();
        var vehicle = CreateVehicle(Guid.NewGuid(), organizationId);
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([vehicle]));
        var first = WorkPost("Пост 2");
        var second = WorkPost("Бокс 2");
        var currentCatalog = new OrganizationScenarioCatalogDto { WorkPosts = [first, second] };
        var catalog = new Mock<ICatalogQueryService>();
        catalog.Setup(service => service.GetAsync("user-1", organizationId)).ReturnsAsync(() => currentCatalog);
        var create = new Mock<IMaintenanceCreateService>();
        var handler = CreateHandler(vehicleQuery: vehicleQuery.Object, catalogQuery: catalog.Object, maintenanceCreate: create.Object);
        var actor = new AssistantActor("user-1", organizationId, "web");
        var command = BookingCommand("2");

        var error = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(command, actor, new(), CancellationToken.None));
        Assert.Equal(2, error.Choices.Count);
        var selected = new AssistantActionSelection(CatalogChoices: new Dictionary<string, string> { ["locationName"] = second.Id.ToString() });
        var plan = await handler.PrepareAsync(command, actor, selected, CancellationToken.None);
        Assert.Contains("Пост: Бокс 2", plan.Changes);
        currentCatalog = new OrganizationScenarioCatalogDto { WorkPosts = [first] };

        await Assert.ThrowsAsync<AssistantClarificationException>(() => plan.ExecuteAsync(CancellationToken.None));

        create.Verify(service => service.Create(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<CreateMaintenanceDto>()), Times.Never);
    }

    [Theory]
    [InlineData(null, 700, false, null, 700)]
    [InlineData(500, 700, false, null, 500)]
    [InlineData(0, 700, false, null, 0)]
    [InlineData(null, 0, false, null, 0)]
    [InlineData(null, null, true, null, null)]
    [InlineData(null, 700, true, null, null)]
    [InlineData(null, 700, false, 900, 900)]
    [InlineData(500, 700, false, 900, 500)]
    [InlineData(null, 700, false, 0, 0)]
    public async Task Booking_UsesExplicitPriceThenCatalogAndKeepsDeferredPriceNull(
        int? explicitPrice, int? catalogPrice, bool isDeferred, int? total, int? expectedPrice)
    {
        var organizationId = Guid.NewGuid();
        var vehicle = CreateVehicle(Guid.NewGuid(), organizationId);
        var vehicles = new Mock<IVehicleQueryService>();
        vehicles.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([vehicle]));
        vehicles.Setup(service => service.GetById("user-1", organizationId, vehicle.Id)).ReturnsAsync(vehicle);
        var catalogItem = new OrganizationScenarioCatalogItemDto
        {
            Id = Guid.NewGuid(), Name = "Мойка", IsActive = true, UnitPrice = catalogPrice
        };
        var catalog = new Mock<ICatalogQueryService>();
        catalog.Setup(service => service.GetAsync("user-1", organizationId))
            .ReturnsAsync(new OrganizationScenarioCatalogDto { Services = [catalogItem] });
        CreateMaintenanceDto? captured = null;
        var create = new Mock<IMaintenanceCreateService>();
        create.Setup(service => service.Create("user-1", organizationId, vehicle.Id, It.IsAny<CreateMaintenanceDto>()))
            .Callback<string, Guid?, Guid, CreateMaintenanceDto>((_, _, _, dto) => captured = dto)
            .ReturnsAsync(Guid.NewGuid());
        var handler = CreateHandler(vehicleQuery: vehicles.Object, catalogQuery: catalog.Object, maintenanceCreate: create.Object);
        var command = new AiPilotCommand
        {
            Action = "book", RegistrationPlate = "A123AA", Date = "2026-10-28", TotalAmount = total,
            Services = [new AiPilotServiceLine { Name = "Мойка", Price = explicitPrice, IsPriceDeferred = isDeferred }]
        };

        var plan = await handler.PrepareAsync(command, new AssistantActor("user-1", organizationId, "web"), new(), CancellationToken.None);

        Assert.Null(captured);
        Assert.Contains(plan.Changes, change => change.StartsWith("Мойка:", StringComparison.Ordinal));
        await plan.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        var line = Assert.Single(captured.Details);
        Assert.Equal((decimal?)expectedPrice, line.Price);
        Assert.Equal(catalogItem.Id, line.CatalogItemId);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 700)]
    public async Task Booking_AsksPriceForUnpricedWorkAndDoesNotUseInactiveCatalogPrice(bool isActive, int? price)
    {
        var organizationId = Guid.NewGuid();
        var vehicle = CreateVehicle(Guid.NewGuid(), organizationId);
        var vehicles = new Mock<IVehicleQueryService>();
        vehicles.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([vehicle]));
        var catalog = new Mock<ICatalogQueryService>();
        catalog.Setup(service => service.GetAsync("user-1", organizationId)).ReturnsAsync(new OrganizationScenarioCatalogDto
        {
            Services = [new OrganizationScenarioCatalogItemDto { Id = Guid.NewGuid(), Name = "Полировка", IsActive = isActive, UnitPrice = price }]
        });
        var create = new Mock<IMaintenanceCreateService>();
        var handler = CreateHandler(vehicleQuery: vehicles.Object, catalogQuery: catalog.Object, maintenanceCreate: create.Object);
        var command = new AiPilotCommand
        {
            Action = "book", RegistrationPlate = "A123AA", Date = "2026-10-28",
            Services = [new AiPilotServiceLine { Name = "Мойка", Price = 500 }, new AiPilotServiceLine { Name = "Полировка" }],
            TotalAmount = 1000
        };

        var question = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(
            command, new AssistantActor("user-1", organizationId, "web"), new(), CancellationToken.None));

        Assert.Equal("book.price:1", question.Field);
        Assert.Contains("Полировка", question.Message);
        Assert.Contains("пока неизвестно", question.Message);
        create.Verify(service => service.Create(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<CreateMaintenanceDto>()), Times.Never);
    }

    [Fact]
    public async Task Booking_UsesChosenCatalogPriceAndBlocksPriceChangeBeforeExecution()
    {
        var organizationId = Guid.NewGuid();
        var vehicle = CreateVehicle(Guid.NewGuid(), organizationId);
        var vehicles = new Mock<IVehicleQueryService>();
        vehicles.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([vehicle]));
        var first = new OrganizationScenarioCatalogItemDto { Id = Guid.NewGuid(), Name = "Мойка", IsActive = true, UnitPrice = 500, CurrencyCode = CurrencyCodes.Cny };
        var second = new OrganizationScenarioCatalogItemDto { Id = Guid.NewGuid(), Name = "Мойка", IsActive = true, UnitPrice = 700, CurrencyCode = CurrencyCodes.Cny };
        var items = new OrganizationScenarioCatalogDto { CurrencyCode = CurrencyCodes.Cny, Services = [first, second] };
        var catalog = new Mock<ICatalogQueryService>();
        catalog.Setup(service => service.GetAsync("user-1", organizationId)).ReturnsAsync(() => items);
        var create = new Mock<IMaintenanceCreateService>();
        var handler = CreateHandler(vehicleQuery: vehicles.Object, catalogQuery: catalog.Object, maintenanceCreate: create.Object);
        var actor = new AssistantActor("user-1", organizationId, "web");
        var command = new AiPilotCommand
        {
            Action = "book", RegistrationPlate = "A123AA", Date = "2026-10-28",
            Services = [new AiPilotServiceLine { Name = "Мойка" }, new AiPilotServiceLine { Name = "Полировка", Price = 500 }],
            TotalAmount = 1000
        };

        var question = await Assert.ThrowsAsync<AssistantClarificationException>(() => handler.PrepareAsync(command, actor, new(), CancellationToken.None));
        Assert.Equal(2, question.Choices.Count);
        var selection = new AssistantActionSelection(CatalogChoices: new Dictionary<string, string> { [question.Field] = second.Id.ToString() });
        var plan = await handler.PrepareAsync(command, actor, selection, CancellationToken.None);

        Assert.Contains("Мойка: 700 CNY", plan.Changes);
        Assert.Contains("Проверьте сумму: названо 1000 CNY, сумма работ 1200 CNY.", plan.Changes);
        items = new OrganizationScenarioCatalogDto
        {
            CurrencyCode = CurrencyCodes.Cny,
            Services = [first, new OrganizationScenarioCatalogItemDto { Id = second.Id, Name = second.Name, IsActive = true, UnitPrice = 900, CurrencyCode = CurrencyCodes.Cny }]
        };

        await Assert.ThrowsAsync<AssistantClarificationException>(() => plan.ExecuteAsync(CancellationToken.None));

        create.Verify(service => service.Create(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<CreateMaintenanceDto>()), Times.Never);
    }

    private static OrganizationScenarioCatalogItemDto WorkPost(string name, bool isActive = true) =>
        new() { Id = Guid.NewGuid(), Name = name, IsActive = isActive };

    private static AiPilotCommand BookingCommand(string location) => new()
    {
        Action = "book",
        RegistrationPlate = "A123AA",
        Date = "2026-10-28",
        StartTime = "16:00",
        Services = [new AiPilotServiceLine { Name = "Мойка", Price = 500 }],
        Parameters = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["locationName"] = System.Text.Json.JsonSerializer.SerializeToElement(location)
        }
    };

    [Fact]
    public async Task VisitWithUnknownPlate_BlocksIfVehicleAppearsBeforeConfirmation()
    {
        var organizationId = Guid.NewGuid();
        var appearedVehicle = CreateVehicle(Guid.NewGuid(), organizationId);
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.SetupSequence(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([]))
            .ReturnsAsync(Vehicles([appearedVehicle]));
        var vehicleCommand = new Mock<IVehicleCommandService>();
        var maintenanceCreate = new Mock<IMaintenanceCreateService>();
        var handler = CreateHandler(vehicleQuery: vehicleQuery.Object, vehicleCommand: vehicleCommand.Object, maintenanceCreate: maintenanceCreate.Object);
        var prepared = await handler.PrepareAsync(VisitCommand("visit", price: 3000),
            new AssistantActor("user-1", organizationId, "web"), new AssistantActionSelection(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AssistantClarificationException>(() => prepared.ExecuteAsync(CancellationToken.None));

        Assert.Equal("vehicleId", exception.Field);
        vehicleCommand.Verify(service => service.Create(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CreateVehicleDto>()), Times.Never);
        maintenanceCreate.Verify(service => service.Create(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<CreateMaintenanceDto>()), Times.Never);
    }

    [Fact]
    public async Task ArriveWithUnknownPlate_CreatesInProgressMaintenance()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([]));
        var vehicleCommand = new Mock<IVehicleCommandService>();
        vehicleCommand.Setup(service => service.Create("user-1", organizationId, It.IsAny<CreateVehicleDto>())).ReturnsAsync(vehicleId);
        CreateMaintenanceDto? captured = null;
        var maintenanceCreate = new Mock<IMaintenanceCreateService>();
        maintenanceCreate.Setup(service => service.Create("user-1", organizationId, vehicleId, It.IsAny<CreateMaintenanceDto>()))
            .Callback<string, Guid?, Guid, CreateMaintenanceDto>((_, _, _, dto) => captured = dto)
            .ReturnsAsync(maintenanceId);
        var handler = CreateHandler(vehicleQuery: vehicleQuery.Object, vehicleCommand: vehicleCommand.Object, maintenanceCreate: maintenanceCreate.Object);

        var prepared = await handler.PrepareAsync(VisitCommand("arrive", price: null),
            new AssistantActor("user-1", organizationId, "web"), new AssistantActionSelection(), CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(OpenCRM.Domain.Enums.MaintenanceStatus.InProgress, captured.Status);
        Assert.Null(captured.Details.First().Price);
    }

    [Fact]
    public async Task ArriveWithPlannedMaintenanceStartsExistingRecord()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var vehicle = CreateVehicle(vehicleId, organizationId);
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.Get("user-1", organizationId, 1, 100, false, null, null, "A123AA", null, true))
            .ReturnsAsync(Vehicles([vehicle]));
        var planned = CreateMaintenance(maintenanceId, OpenCRM.Domain.Enums.MaintenanceStatus.Planned, vehicleId);
        var maintenanceQuery = new Mock<IMaintenanceQueryService>();
        maintenanceQuery.Setup(service => service.Get("user-1", organizationId, vehicleId)).ReturnsAsync([new GetMaintenanceDto
        {
            Id = maintenanceId,
            Status = OpenCRM.Domain.Enums.MaintenanceStatus.Planned,
            Type = OpenCRM.Domain.Enums.MaintenanceType.Repair,
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
        }]);
        maintenanceQuery.Setup(service => service.GetById("user-1", organizationId, maintenanceId)).ReturnsAsync(planned);
        var workflow = new Mock<IMaintenanceWorkflowService>();
        workflow.Setup(service => service.ChangeWorkflow("user-1", organizationId, maintenanceId, It.IsAny<MaintenanceWorkflowActionDto>()))
            .Returns(Task.CompletedTask);
        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var handler = CreateHandler(vehicleQuery: vehicleQuery.Object, maintenanceQuery: maintenanceQuery.Object, guard: guard.Object, maintenanceWorkflow: workflow.Object);

        var prepared = await handler.PrepareAsync(VisitCommand("arrive", price: null),
            new AssistantActor("user-1", organizationId, "web"), new AssistantActionSelection(), CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        workflow.Verify(service => service.ChangeWorkflow("user-1", organizationId, maintenanceId,
            It.Is<MaintenanceWorkflowActionDto>(dto => dto.Action == "start")), Times.Once);
        guard.Verify(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), "start", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task VisitWithPayment_CreatesUnpaidMaintenanceThenAddsPaymentWithFinancePermission()
    {
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var vehicle = CreateVehicle(vehicleId, organizationId);
        var vehicleQuery = new Mock<IVehicleQueryService>();
        vehicleQuery.Setup(service => service.GetById("user-1", organizationId, vehicleId)).ReturnsAsync(vehicle);
        var catalogQuery = new Mock<ICatalogQueryService>();
        catalogQuery.Setup(service => service.GetAsync("user-1", organizationId))
            .ReturnsAsync(new OrganizationScenarioCatalogDto { OrganizationId = organizationId });
        CreateMaintenanceDto? capturedMaintenance = null;
        var maintenanceCreate = new Mock<IMaintenanceCreateService>();
        maintenanceCreate.Setup(service => service.Create("user-1", organizationId, vehicleId, It.IsAny<CreateMaintenanceDto>()))
            .Callback<string, Guid?, Guid, CreateMaintenanceDto>((_, _, _, dto) => capturedMaintenance = dto)
            .ReturnsAsync(maintenanceId);
        var payment = new Mock<IMaintenancePaymentService>();
        payment.Setup(service => service.AddPayment("user-1", organizationId, maintenanceId, It.IsAny<AddMaintenancePaymentDto>()))
            .Returns(Task.CompletedTask);
        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(vehicleQuery.Object, catalogQuery.Object, maintenanceCreate.Object, payment.Object, guard.Object);
        var command = new AiPilotCommand
        {
            Action = "visit",
            RegistrationPlate = "A123AA",
            PaidAmount = 1200,
            Services = [new AiPilotServiceLine { Name = "Полировка", Price = 3000 }],
            Parameters = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["vehicleId"] = System.Text.Json.JsonSerializer.SerializeToElement(vehicleId.ToString())
            }
        };
        var actor = new AssistantActor("user-1", organizationId, "web");

        var prepared = await handler.PrepareAsync(command, actor, new AssistantActionSelection(), CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(capturedMaintenance);
        Assert.Equal(0m, capturedMaintenance.PaidAmount);
        payment.Verify(service => service.AddPayment("user-1", organizationId, maintenanceId,
            It.Is<AddMaintenancePaymentDto>(dto => dto.Amount == 1200 && dto.ExpectedPaidAmount == 0)), Times.Once);
        guard.Verify(service => service.EnsureAllowedAsync(actor, "payment_add", It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task IntakeUpdate_PreservesUnmentionedFields()
    {
        var maintenanceId = Guid.NewGuid();
        var current = CreateMaintenance(maintenanceId);
        var maintenanceQuery = new Mock<IMaintenanceQueryService>();
        maintenanceQuery.Setup(service => service.GetById("user-1", It.IsAny<Guid?>(), maintenanceId)).ReturnsAsync(current);
        SaveMaintenanceIntakeDto? captured = null;
        var intake = new Mock<IMaintenanceIntakeService>();
        intake.Setup(service => service.SaveIntake("user-1", It.IsAny<Guid>(), maintenanceId, It.IsAny<SaveMaintenanceIntakeDto>()))
            .Callback<string, Guid, Guid, SaveMaintenanceIntakeDto>((_, _, _, dto) => captured = dto)
            .Returns(Task.CompletedTask);
        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(maintenanceQuery: maintenanceQuery.Object, maintenanceIntake: intake.Object, guard: guard.Object);
        var command = new AiPilotCommand
        {
            Action = "intake_update",
            Parameters = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["mileage"] = System.Text.Json.JsonSerializer.SerializeToElement(98765)
            }
        };
        var actor = new AssistantActor("user-1", Guid.NewGuid(), "web");

        var prepared = await handler.PrepareAsync(command, actor, new AssistantActionSelection(maintenanceId), CancellationToken.None);
        await prepared.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(98765, captured.Mileage);
        Assert.Equal("Чехол запаски и домкрат", captured.Completeness);
        Assert.Equal("Царапина на двери", captured.VisibleDamage);
        Assert.Equal("Клиент сообщил о стуке", captured.Notes);
    }

    [Fact]
    public async Task VehiclePlateUpdateUsesExistingTrailerKindBeforeApplyingItsMask()
    {
        var organizationId = Guid.NewGuid();
        var vehicle = CreateVehicle(Guid.NewGuid(), organizationId, OpenCRM.Domain.Enums.VehicleKind.Trailer);
        var query = new Mock<IVehicleQueryService>();
        query.Setup(service => service.GetById("user-1", organizationId, vehicle.Id)).ReturnsAsync(vehicle);
        var policy = new Mock<IVehicleRegistrationPlatePolicy>();
        policy.Setup(service => service.MatchesFormat("АА5678000", OpenCRM.Domain.Enums.VehicleKind.Trailer)).Returns(true);
        UpdateVehicleDto? captured = null;
        var commands = new Mock<IVehicleCommandService>();
        commands.Setup(service => service.Update("user-1", organizationId, vehicle.Id, It.IsAny<UpdateVehicleDto>()))
            .Callback<string, Guid?, Guid, UpdateVehicleDto>((_, _, _, dto) => captured = dto)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(vehicleQuery: query.Object, vehicleCommand: commands.Object, registrationPlates: policy.Object);
        var command = new AiPilotCommand
        {
            Action = "vehicle_update", RegistrationPlate = vehicle.RegistrationPlate,
            Parameters = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["vehicleId"] = System.Text.Json.JsonSerializer.SerializeToElement(vehicle.Id.ToString()),
                ["newRegistrationPlate"] = System.Text.Json.JsonSerializer.SerializeToElement("АА5678000")
            }
        };

        var plan = await handler.PrepareAsync(command, new AssistantActor("user-1", organizationId, "web"), new(), CancellationToken.None);

        Assert.Null(captured);
        await plan.ExecuteAsync(CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(OpenCRM.Domain.Enums.VehicleKind.Trailer, captured.Kind);
        Assert.Equal("АА5678000", captured.RegistrationPlate);
        Assert.False(captured.AllowOtherRegistrationPlateFormat);
    }

    private static CoreAssistantActionHandler CreateHandler(
        IVehicleQueryService? vehicleQuery = null,
        ICatalogQueryService? catalogQuery = null,
        IMaintenanceCreateService? maintenanceCreate = null,
        IMaintenancePaymentService? maintenancePayment = null,
        IAssistantExecutionGuard? guard = null,
        IMaintenanceQueryService? maintenanceQuery = null,
        IMaintenanceIntakeService? maintenanceIntake = null,
        IVehicleCommandService? vehicleCommand = null,
        IMaintenanceWorkflowService? maintenanceWorkflow = null,
        IVehicleRegistrationPlatePolicy? registrationPlates = null) => new(
        maintenanceQuery ?? Mock.Of<IMaintenanceQueryService>(),
        maintenanceCreate ?? Mock.Of<IMaintenanceCreateService>(),
        Mock.Of<IMaintenanceUpdateService>(),
        Mock.Of<IMaintenanceDeleteService>(),
        maintenanceWorkflow ?? Mock.Of<IMaintenanceWorkflowService>(),
        Mock.Of<IMaintenanceSchedulingService>(),
        maintenancePayment ?? Mock.Of<IMaintenancePaymentService>(),
        Mock.Of<IMaintenanceAssignmentService>(),
        maintenanceIntake ?? Mock.Of<IMaintenanceIntakeService>(),
        vehicleQuery ?? Mock.Of<IVehicleQueryService>(),
        vehicleCommand ?? Mock.Of<IVehicleCommandService>(),
        Mock.Of<IOrganizationClientQueryService>(),
        Mock.Of<IOrganizationClientCommandService>(),
        Mock.Of<IOrganizationEmployeeService>(),
        catalogQuery ?? CreateCatalogQuery(),
        guard ?? CreateGuard(),
        AssistantOptionsTestFixture.Options,
        registrationPlates);

    private static ICatalogQueryService CreateCatalogQuery()
    {
        var catalog = new Mock<ICatalogQueryService>();
        catalog.Setup(service => service.GetAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new OrganizationScenarioCatalogDto());

        return catalog.Object;
    }

    private static IAssistantExecutionGuard CreateGuard()
    {
        var guard = new Mock<IAssistantExecutionGuard>();
        guard.Setup(service => service.EnsureAllowedAsync(It.IsAny<AssistantActor>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return guard.Object;
    }

    private static AiPilotCommand VisitCommand(string action, decimal? price) => new()
    {
        Action = action,
        RegistrationPlate = "A123AA",
        Services = [new AiPilotServiceLine { Name = "Полировка", Price = price }]
    };

    private static GetVehiclesResponse Vehicles(IEnumerable<GetVehicleDto> vehicles) => new()
    {
        Items = vehicles,
        TotalCount = vehicles.Count(),
        Page = 1,
        PageSize = 100,
        AllCount = vehicles.Count(),
        UserBrands = [],
        UserKinds = []
    };

    private static GetVehicleDto CreateVehicle(Guid id, Guid organizationId,
        OpenCRM.Domain.Enums.VehicleKind kind = OpenCRM.Domain.Enums.VehicleKind.PassengerCar) => new()
    {
        Id = id,
        Kind = kind,
        MileageUnit = OpenCRM.Domain.Enums.VehicleMileageUnit.Kilometers,
        BrandId = null,
        BrandName = "Toyota",
        ModelId = null,
        ModelName = "Camry",
        YearIssue = null,
        PhotoId = null,
        IsArchived = false,
        MaintenanceCount = 0,
        Name = "Toyota Camry",
        RegistrationPlate = "A123AA",
        Description = null,
        OrganizationId = organizationId
    };

    private static GetMaintenanceByIdDto CreateMaintenance(
        Guid id,
        OpenCRM.Domain.Enums.MaintenanceStatus status = OpenCRM.Domain.Enums.MaintenanceStatus.InProgress,
        Guid? vehicleId = null) => new()
    {
        Id = id,
        Status = status,
        Type = OpenCRM.Domain.Enums.MaintenanceType.Repair,
        Date = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc),
        PlannedDate = null,
        CompletedDate = null,
        Location = null,
        Performer = null,
        Mileage = null,
        IntakeMileage = 76543,
        IntakeCompleteness = "Чехол запаски и домкрат",
        IntakeVisibleDamage = "Царапина на двери",
        IntakeNotes = "Клиент сообщил о стуке",
        Comment = null,
        Price = null,
        PaidAmount = 0,
        VehicleId = vehicleId ?? Guid.NewGuid(),
        PhotoIds = [],
        MaintenanceDetails = [],
        LinkedDefects = []
    };
}
