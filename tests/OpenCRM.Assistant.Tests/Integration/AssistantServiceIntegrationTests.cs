using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using Moq;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class AssistantServiceIntegrationTests(AssistantIntegrationFixture fixture) : IClassFixture<AssistantIntegrationFixture>
{
    [Fact]
    public async Task Chat_IsRejectedBeforeProviderRequestWhenAssistantFeatureIsUnavailable()
    {
        var organizationId = await fixture.CreateOrganizationAsync("free-plan");
        await fixture.ClearUsageAsync();
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");
        var handler = new FakeRouterHandler([CreateVisitCommand()]);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), handler, hasAssistantFeature: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.ChatAsync(
            actor, "создай визит", null, CancellationToken.None));

        Assert.Equal("ИИ-помощник недоступен для этого рабочего пространства.", exception.Message);
        Assert.Empty(handler.RequestBodies);
        Assert.Empty(await scope.Context.Set<AssistantConversation>()
            .Where(conversation => conversation.OrganizationId == organizationId)
            .ToListAsync());
    }

    [Fact]
    public async Task Conversation_IsRestoredFromPostgresAfterNewServiceScopeAndCache()
    {
        var organizationId = await fixture.CreateOrganizationAsync("restore");
        await fixture.ClearUsageAsync();
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");
        var executor = new FakeAssistantActionExecutor();
        var handler = new FakeRouterHandler([CreateVisitCommand()]);
        Guid conversationId;

        await using (var firstScope = fixture.CreateAssistant(executor, new FakeRouterHandler([])))
        {
            var greeting = await firstScope.Service.ChatAsync(actor, "привет", null, CancellationToken.None);
            conversationId = greeting.ConversationId;
            Assert.Contains("Здравствуйте", greeting.Message);
        }

        await using var secondScope = fixture.CreateAssistant(executor, handler);
        var reply = await secondScope.Service.ChatAsync(actor, "создай визит на А123ВС77", conversationId, CancellationToken.None);

        Assert.NotNull(reply.Draft);
        Assert.Single(handler.RequestBodies);
        using var request = JsonDocument.Parse(handler.RequestBodies[0]);
        var messages = request.RootElement.GetProperty("messages").EnumerateArray()
            .Where(message => message.TryGetProperty("content", out _))
            .Select(message => message.GetProperty("content").GetString())
            .ToArray();
        Assert.Contains(messages, message => message?.Contains("Здравствуйте", StringComparison.Ordinal) == true);
        Assert.Contains(messages, message => message?.Contains("создай визит", StringComparison.Ordinal) == true);

        await using var verify = fixture.CreateContext();
        Assert.True(await verify.Set<AssistantMessage>().AnyAsync(message => message.ConversationId == conversationId && message.Role == "assistant"));
        Assert.True(await verify.Set<AssistantDraft>().AnyAsync(draft => draft.ConversationId == conversationId && draft.Status == "pending"));
    }

    [Fact]
    public async Task ConcurrentConfirm_ExecutesPreparedActionOnlyOnce()
    {
        var organizationId = await fixture.CreateOrganizationAsync("confirm");
        await fixture.ClearUsageAsync();
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");
        var executor = new FakeAssistantActionExecutor();
        var handler = new FakeRouterHandler([CreateVisitCommand()]);
        await using var firstScope = fixture.CreateAssistant(executor, handler);
        await using var secondScope = fixture.CreateAssistant(executor, new FakeRouterHandler([]));
        var prepared = await firstScope.Service.ChatAsync(actor, "запиши автомобиль А123ВС77 на мойку за 1000 рублей", null, CancellationToken.None);
        var draftId = Assert.IsType<Guid>(prepared.Draft?.Id);

        var confirmations = await Task.WhenAll(
            ConfirmWithoutThrowingAsync(firstScope.Service, actor, draftId),
            ConfirmWithoutThrowingAsync(secondScope.Service, actor, draftId));

        Assert.Equal(1, executor.ExecutionCount);
        Assert.Contains(confirmations, result => result is not null);
        await using var verify = fixture.CreateContext();
        var draft = await verify.Set<AssistantDraft>().SingleAsync(item => item.Id == draftId);
        Assert.Equal("completed", draft.Status);
    }

    [Fact]
    public async Task Draft_CannotBeConfirmedFromAnotherOrganization()
    {
        var firstOrganizationId = await fixture.CreateOrganizationAsync("isolation-a");
        var secondOrganizationId = await fixture.CreateOrganizationAsync("isolation-b");
        await fixture.ClearUsageAsync();
        var userId = await fixture.GetOwnerUserIdAsync(firstOrganizationId);
        var firstActor = new AssistantActor(userId, firstOrganizationId, "web");
        var secondActor = new AssistantActor(userId, secondOrganizationId, "web");
        var executor = new FakeAssistantActionExecutor();
        await using var scope = fixture.CreateAssistant(executor, new FakeRouterHandler([CreateVisitCommand()]));
        var prepared = await scope.Service.ChatAsync(firstActor, "запиши автомобиль А123ВС77 на мойку за 1000 рублей", null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.ConfirmAsync(
            secondActor,
            Assert.IsType<Guid>(prepared.Draft?.Id),
            true,
            CancellationToken.None));

        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task GlobalDisable_AfterPreparationPreventsConfirmation()
    {
        var organizationId = await fixture.CreateOrganizationAsync("disabled");
        await fixture.ClearUsageAsync();
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");
        var executor = new FakeAssistantActionExecutor();
        await using var scope = fixture.CreateAssistant(executor, new FakeRouterHandler([CreateVisitCommand()]));
        var prepared = await scope.Service.ChatAsync(actor, "запиши автомобиль А123ВС77 на мойку за 1000 рублей", null, CancellationToken.None);
        var draftId = Assert.IsType<Guid>(prepared.Draft?.Id);
        await fixture.SetAssistantEnabledAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service.ConfirmAsync(actor, draftId, true, CancellationToken.None));

        Assert.Equal(0, executor.ExecutionCount);
        await fixture.SetAssistantEnabledAsync(true);
    }

    [Fact]
    public async Task UsageReservations_AreSerializedAndUnknownCostRemainsReserved()
    {
        var organizationId = await fixture.CreateOrganizationAsync("usage");
        await fixture.ClearUsageAsync();
        await fixture.SetBudgetsAsync(organizationId, 2, 2);
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");

        var reservations = await Task.WhenAll(ReserveWithoutThrowingAsync(actor), ReserveWithoutThrowingAsync(actor));

        var usageId = Assert.Single(reservations, id => id.HasValue)!.Value;
        Assert.Single(reservations, id => !id.HasValue);
        await fixture.UsageService.CompleteAsync(usageId, null, false, null, "provider_response_unknown");

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UsageService.ReserveAsync(
            actor,
            null,
            "text",
            fixture.Options.TextModel,
            CancellationToken.None));

        await using var verify = fixture.CreateContext();
        var usage = await verify.Set<AssistantUsage>().SingleAsync(item => item.Id == usageId);
        Assert.Equal("unknown", usage.Status);
        Assert.Null(usage.CostRub);
        Assert.Equal(2m, usage.ReservedCostRub);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BookingPriceReply_RestoresCatalogChoiceButNewTaskResetsIt(bool startsNewTask)
    {
        var organizationId = await fixture.CreateOrganizationAsync("booking-price");
        await fixture.ClearUsageAsync();
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");
        var catalogField = "catalog:0:МОЙКА";
        var chosenId = Guid.NewGuid().ToString();
        var executor = new Mock<IAssistantActionExecutor>();
        executor.Setup(service => service.PrepareAsync(It.IsAny<AiPilotCommand>(), It.IsAny<AssistantActor>(), It.IsAny<AssistantActionSelection>(), It.IsAny<CancellationToken>()))
            .Returns<AiPilotCommand, AssistantActor, AssistantActionSelection, CancellationToken>((command, _, selection, _) =>
            {
                if (selection.CatalogChoices?.GetValueOrDefault(catalogField) != chosenId)
                {
                    throw new AssistantClarificationException("Выберите услугу.", catalogField,
                        [new AssistantChoice(chosenId, "Мойка"), new AssistantChoice(Guid.NewGuid().ToString(), "Мойка")]);
                }

                if (!command.Services[0].Price.HasValue && !command.Services[0].IsPriceDeferred)
                {
                    throw new AssistantClarificationException("Сколько будет стоить мойка?", "book.price:0");
                }

                return Task.FromResult(new AssistantPreparedAction
                {
                    Target = "Мойка", Changes = ["Запланировать мойку"], Snapshot = "booking-price",
                    ExecuteAsync = _ => throw new InvalidOperationException("В этом тесте подтверждение не выполняется.")
                });
            });
        var command = new AiPilotCommand
        {
            Action = "book", RegistrationPlate = "Т000ТТ000", Date = "2026-10-28", StartTime = "16:00",
            Services = [new AiPilotServiceLine { Name = "Мойка" }]
        };
        Guid conversationId;
        await using (var scope = fixture.CreateAssistant(executor.Object, new FakeRouterHandler([command])))
        {
            var choice = await scope.Service.ChatAsync(actor, "запиши мойку на завтра", null, CancellationToken.None);
            Assert.Equal(catalogField, choice.Clarification?.Field);
            conversationId = choice.ConversationId;

            var price = await scope.Service.ChooseAsync(actor, conversationId, catalogField, chosenId, CancellationToken.None);

            Assert.Equal("book.price:0", price.Clarification?.Field);
            Assert.Null(price.Draft);
        }

        var handler = new FakeRouterHandler(startsNewTask ? [command] : []);
        await using var restored = fixture.CreateAssistant(executor.Object, handler);
        var reply = await restored.Service.ChatAsync(actor, startsNewTask ? "создай другую запись на мойку" : "пока неизвестно",
            conversationId, CancellationToken.None);

        if (startsNewTask)
        {
            Assert.Null(reply.Draft);
            Assert.Equal(catalogField, reply.Clarification?.Field);
            Assert.Single(handler.RequestBodies);

            return;
        }

        Assert.NotNull(reply.Draft);
        Assert.True(reply.Draft.Command.Services[0].IsPriceDeferred);
        Assert.Null(reply.Draft.Command.Services[0].Price);
        Assert.Equal("16:00", reply.Draft.Command.StartTime);
        Assert.Empty(handler.RequestBodies);
    }

    [Fact]
    public async Task NonstandardPlateConfirmationSurvivesNewScopeWithoutAnotherProviderRequest()
    {
        var organizationId = await fixture.CreateOrganizationAsync("plate-confirmation");
        await fixture.ClearUsageAsync();
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");
        var executor = new FakeAssistantActionExecutor();
        Guid conversationId;
        await using (var scope = fixture.CreateAssistant(executor, new FakeRouterHandler([MalformedPlateCommand()])))
        {
            var reply = await scope.Service.ChatAsync(actor, "запиши тестовую машину", null, CancellationToken.None);
            conversationId = reply.ConversationId;
            Assert.Null(reply.Draft);
            Assert.Contains("Проверьте госномер", reply.Message);
        }

        var provider = new FakeRouterHandler([]);
        await using var restored = fixture.CreateAssistant(executor, provider);
        var confirmation = await restored.Service.ChatAsync(actor, "номер верный", conversationId, CancellationToken.None);

        Assert.NotNull(confirmation.Draft);
        Assert.Equal("A123777000", confirmation.Draft.Command.ConfirmedRegistrationPlate);
        Assert.Equal("16:00", confirmation.Draft.Command.StartTime);
        Assert.Empty(provider.RequestBodies);
        Assert.Equal(0, executor.ExecutionCount);
    }

    [Fact]
    public async Task OldDraftWithMalformedPlateCannotExecuteWithoutExplicitNumberConfirmation()
    {
        var organizationId = await fixture.CreateOrganizationAsync("old-plate-draft");
        await fixture.ClearUsageAsync();
        var actor = new AssistantActor(await fixture.GetOwnerUserIdAsync(organizationId), organizationId, "web");
        var executor = new FakeAssistantActionExecutor();
        await using var scope = fixture.CreateAssistant(executor, new FakeRouterHandler([]));
        var greeting = await scope.Service.ChatAsync(actor, "привет", null, CancellationToken.None);
        var draft = new AssistantDraft
        {
            ConversationId = greeting.ConversationId, OrganizationId = organizationId, UserId = actor.UserId,
            Action = "book", CommandJson = JsonSerializer.Serialize(MalformedPlateCommand(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Snapshot = "old-draft", ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        scope.Context.Add(draft);
        await scope.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<AssistantClarificationException>(() => scope.Service.ConfirmAsync(actor, draft.Id, true, CancellationToken.None));

        Assert.Equal(0, executor.ExecutionCount);
        await using var verify = fixture.CreateContext();
        Assert.Equal("pending", (await verify.Set<AssistantDraft>().SingleAsync(item => item.Id == draft.Id)).Status);
    }

    private static AiPilotCommand MalformedPlateCommand() => new()
    {
        Action = "book", RegistrationPlate = "A123777000", Date = "2026-10-28", StartTime = "16:00",
        Services = [new AiPilotServiceLine { Name = "Мойка", Price = 500 }]
    };

    private async Task<Guid?> ReserveWithoutThrowingAsync(AssistantActor actor)
    {
        try
        {
            return await fixture.UsageService.ReserveAsync(actor, null, "text", fixture.Options.TextModel, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<AssistantActionResult?> ConfirmWithoutThrowingAsync(AssistantService service, AssistantActor actor, Guid draftId)
    {
        try
        {
            return await service.ConfirmAsync(actor, draftId, true, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static AiPilotCommand CreateVisitCommand() => new()
    {
        Action = "visit",
        RegistrationPlate = "А123ВС77",
        Services = [new AiPilotServiceLine { Name = "Мойка", Price = 1000 }],
        TotalAmount = 1000,
        Parameters = []
    };
}
