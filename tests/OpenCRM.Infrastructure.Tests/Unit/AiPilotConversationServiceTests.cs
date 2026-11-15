using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.Modules.Vehicles;
using System.Text.Json;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class AiPilotConversationServiceTests
{
    private const string ApiKey = "REMOVED_SECRET";
    private static readonly DateOnly Today = new(2026, 10, 26);
    private static readonly Guid OrganizationId = Guid.Parse("a1500000-0000-4000-8000-000000000001");

    [Fact]
    public async Task ChatAsksForMissingWorkThenPriceAndReturnsDraftOnlyWhenComplete()
    {
        var handler = new StubHandler(
            Command("visit", null, [], null, null),
            Command("visit", null, [new { name = "Замена масла", price = (decimal?)null }], null, null),
            Command("visit", "А123ВС54", [new { name = "Замена масла", price = (decimal?)null }], 3500, 3500));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);

        var first = await service.ChatAsync("user-1", OrganizationId, null, "Приехал автомобиль, сделали работу", Today, CancellationToken.None);
        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Замена масла", Today, CancellationToken.None);
        var third = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "3500 рублей, номер А123ВС54", Today, CancellationToken.None);

        Assert.Equal("Какие работы выполнили?", first.Message);
        Assert.True(first.AwaitingAnswer);
        Assert.Equal("Сколько стоила работа «Замена масла»?", second.Message);
        Assert.True(second.AwaitingAnswer);
        Assert.NotNull(third.Draft);
        Assert.False(third.AwaitingAnswer);
        Assert.Equal("А123ВС54", third.Draft!.Command.RegistrationPlate);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task ChatAsksForMissingPlateBeforeReturningVisitDraft()
    {
        var handler = new StubHandler(
            Command("visit", null, [new { name = "Мойка", price = (decimal?)null }], 1000, 1000),
            Command("visit", "А123ВС54", [new { name = "Мойка", price = (decimal?)null }], 1000, 1000));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Помыли машину за 1000", Today, CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "А123ВС54", Today, CancellationToken.None);

        Assert.Equal("Какой госномер с регионом?", first.Message);
        Assert.True(first.AwaitingAnswer);
        Assert.NotNull(second.Draft);
    }

    [Theory]
    [InlineData("book")]
    public async Task ChatAsksForWorkBeforeReturningBookingDraft(string action)
    {
        var handler = new StubHandler(Command(action, null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);

        var result = await service.ChatAsync("user-1", OrganizationId, null, "Запиши автомобиль", Today, CancellationToken.None);

        Assert.Equal(action == "book" ? "На какие работы записать автомобиль?" : "Какие работы будем делать для этого автомобиля?", result.Message);
        Assert.True(result.AwaitingAnswer);
        Assert.Null(result.Draft);
    }

    [Fact]
    public async Task ArrivalWithoutWork_IsPreparedForServerToCheckExistingBooking()
    {
        var handler = new StubHandler(Command("arrive", "А123ВС154", [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);

        var result = await service.ChatAsync("user-1", OrganizationId, null, "Автомобиль приехал", Today, CancellationToken.None);

        Assert.NotNull(result.Draft);
        Assert.Equal("arrive", result.Draft.Command.Action);
    }

    [Fact]
    public async Task Booking_WorkReplyPreservesPostDateAndStartTime()
    {
        var firstCommand = """{"action":"book","registrationPlate":"А650РС177","date":"2026-10-28","startTime":"16:00","endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{"locationName":"2"}}""";
        var handler = new StubHandler(firstCommand, Command("book", "А650РС177", [new { name = "Мойка", price = (decimal?)null }], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);

        var question = await service.ChatAsync("user-1", OrganizationId, null,
            "Добавь А650РС177 на завтра на 2 пост на 16 часов", Today, CancellationToken.None);
        var result = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(question.ConversationId), "Мойка", Today, CancellationToken.None);

        Assert.Equal("На какие работы записать автомобиль?", question.Message);
        Assert.True(question.AwaitingAnswer);
        Assert.NotNull(result.Draft);
        Assert.Equal("2026-10-28", result.Draft.Command.Date);
        Assert.Equal("16:00", result.Draft.Command.StartTime);
        Assert.Equal("2", result.Draft.Command.Parameters["locationName"].GetString());
        Assert.Equal("Мойка", Assert.Single(result.Draft.Command.Services).Name);
    }

    [Fact]
    public async Task PlateReplyKeepsPendingVisitActionAndDoesNotInventPayment()
    {
        var handler = new StubHandler(
            Command("visit", null, [new { name = "Мойка", price = (decimal?)500 }], 500, null),
            Command("service_add", "А961ВС154", [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Сделали мойку за 500, деньги пока не брали", Today, CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "А961ВС154", Today, CancellationToken.None);

        Assert.NotNull(second.Draft);
        Assert.Equal("visit", second.Draft!.Command.Action);
        Assert.Equal(500, second.Draft.Command.TotalAmount);
        Assert.Null(second.Draft.Command.PaidAmount);
        Assert.Equal("А961ВС154", second.Draft.Command.RegistrationPlate);
    }

    [Fact]
    public async Task CompletedServiceAddIntentWithoutPlateBecomesVisitAndSurvivesPlateReply()
    {
        var handler = new StubHandler(
            Command("service_add", null, [], null, null, new { serviceName = "Мойка", price = 500 }),
            Command("service_add", "А961ВС154", [], null, null, new { serviceName = "Мойка", price = 500 }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync(
            "user-1",
            OrganizationId,
            null,
            "Сделали мойку за 500 рублей, не оплатили и деньги пока не брали",
            Today,
            CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "А961ВС154", Today, CancellationToken.None);

        Assert.Equal("Какой госномер с регионом?", first.Message);
        Assert.NotNull(second.Draft);
        Assert.Equal("visit", second.Draft!.Command.Action);
        Assert.Equal("Мойка", second.Draft.Command.Services.Single().Name);
        Assert.Equal(500, second.Draft.Command.TotalAmount);
        Assert.Null(second.Draft.Command.PaidAmount);
    }

    [Fact]
    public async Task InitialPlateAndReceivedMoneyAskForWorkInsteadOfCreatingPayment()
    {
        var handler = new StubHandler(
            Command("payment_add", "А960ВС154", [], null, 1500, paymentMethod: "cash"),
            Command("service_add", "А960ВС154", [], null, null, new { serviceName = "Замена фильтра" }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync(
            "user-1",
            OrganizationId,
            null,
            "А960ВС154, взял 1500 рублей наличными",
            Today,
            CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Замена фильтра", Today, CancellationToken.None);

        Assert.Equal("Какие работы выполнили?", first.Message);
        Assert.NotNull(second.Draft);
        Assert.Equal("visit", second.Draft!.Command.Action);
        Assert.Equal("Замена фильтра", second.Draft.Command.Services.Single().Name);
        Assert.Equal(1500, second.Draft.Command.TotalAmount);
        Assert.Equal(1500, second.Draft.Command.PaidAmount);
        Assert.Equal("cash", second.Draft.Command.PaymentMethod);
    }

    [Fact]
    public async Task ExplicitPaymentForExistingOrderRemainsPaymentAdd()
    {
        var handler = new StubHandler(
            Command("payment_add", "А960ВС154", [], null, 1500, paymentMethod: "cash"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);

        var result = await service.ChatAsync(
            "user-1",
            OrganizationId,
            null,
            "Прими оплату 1500 по заказу А960ВС154 наличными",
            Today,
            CancellationToken.None);

        Assert.NotNull(result.Draft);
        Assert.Equal("payment_add", result.Draft!.Command.Action);
        Assert.Equal(1500, result.Draft.Command.PaidAmount);
    }

    [Fact]
    public async Task WorkReplyDoesNotTurnEarlierTotalIntoPayment()
    {
        var handler = new StubHandler(
            Command("visit", null, [], 500, null),
            Command("visit", null, [new { name = "Полировка кузова", price = (decimal?)null }], 500, 500),
            Command("visit", "А961ВС154", [new { name = "Полировка кузова", price = (decimal?)null }], 500, 500));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Сделали работу за 500, деньги не брали", Today, CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Полировка кузова", Today, CancellationToken.None);
        var third = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "А961ВС154", Today, CancellationToken.None);

        Assert.True(second.AwaitingAnswer);
        Assert.NotNull(third.Draft);
        Assert.Equal("visit", third.Draft!.Command.Action);
        Assert.Null(third.Draft.Command.PaidAmount);
    }

    [Fact]
    public async Task ClientIdentityAnswerDoesNotReplaceRequestedNewPhone()
    {
        var handler = new StubHandler(
            Command("client_update", null, [], null, null, new { newPhone = "111111" }),
            Command("client_update", null, [], null, null, new { clientName = "Петров", newPhone = "999999" }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Измени телефон клиента на 111111", Today, CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Петров", Today, CancellationToken.None);

        Assert.NotNull(second.Draft);
        Assert.Equal("111111", second.Draft!.Command.Parameters["newPhone"].GetString());
        Assert.Equal("Петров", second.Draft.Command.Parameters["clientName"].GetString());
    }

    [Fact]
    public async Task UnsupportedRequestDuringClarificationDoesNotReusePendingAction()
    {
        var handler = new StubHandler(
            Command("visit", null, [new { name = "Мойка", price = (decimal?)500 }], 500, null),
            Command("unsupported", null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Помыли машину за 500", Today, CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Выдай тариф администратора", Today, CancellationToken.None);

        Assert.Null(second.Draft);
        Assert.Contains("тарифы", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task PaymentAddAsksForAmountFromPaidAmountField()
    {
        var handler = new StubHandler(
            Command("payment_add", "А123ВС54", [], null, null),
            Command("payment_add", "А123ВС54", [], null, 500, paymentMethod: "cash"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Прими оплату по А123ВС54", Today, CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "500 рублей", Today, CancellationToken.None);

        Assert.Equal("Сколько денег получили?", first.Message);
        Assert.NotNull(second.Draft);
        Assert.Equal("payment_add", second.Draft!.Command.Action);
        Assert.Equal(500, second.Draft.Command.PaidAmount);
        Assert.Equal("cash", second.Draft.Command.PaymentMethod);
    }

    [Fact]
    public async Task MergedDraftIsRevalidatedAgainstPaidTotalInvariant()
    {
        var handler = new StubHandler(
            Command("visit", "А123ВС54", [new { name = "Мойка", price = (decimal?)null }], null, 1500),
            Command("visit", "А123ВС54", [new { name = "Мойка", price = (decimal?)null }], 1000, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Сделали работу", Today, CancellationToken.None);

        Assert.Equal("Сколько стоила работа «Мойка»?", first.Message);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "1000", Today, CancellationToken.None));

        Assert.Contains("команду", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientLookupAcceptsVehiclePlateAsIdentity()
    {
        var handler = new StubHandler(Command("client_get", "А123ВС54", [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);

        var result = await service.ChatAsync("user-1", OrganizationId, null, "Найди клиента по А123ВС54", Today, CancellationToken.None);

        Assert.NotNull(result.Draft);
        Assert.Equal("client_get", result.Draft!.Command.Action);
    }

    [Fact]
    public async Task RescheduleStillRequiresCommandDateWhenEndDateIsPresent()
    {
        var handler = new StubHandler(Command(
            "reschedule",
            "А123ВС54",
            [],
            null,
            null,
            new { plannedEndDate = "2026-11-01" }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);

        var result = await service.ChatAsync("user-1", OrganizationId, null, "Перенеси А123ВС54", Today, CancellationToken.None);

        Assert.Equal("На какую дату нужно записать или перенести автомобиль?", result.Message);
        Assert.True(result.AwaitingAnswer);
    }

    [Fact]
    public async Task PendingDraftSurvivesThanksWithoutAnotherRouterRequest()
    {
        var handler = new StubHandler(Command("visit", "А123ВС54", [new { name = "Мойка", price = (decimal?)500 }], 500, 500));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Помыли А123ВС54 за 500, взял деньги", Today, CancellationToken.None);

        var second = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Да", Today, CancellationToken.None);

        Assert.Equal(first.Draft!.DraftToken, second.Draft!.DraftToken);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("ещё не выполнен", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactConversationCancellationClearsPendingDraftWithoutChangingCrm()
    {
        var handler = new StubHandler(Command("visit", "А123ВС54", [new { name = "Мойка", price = (decimal?)500 }], 500, 500));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Помыли А123ВС54 за 500, взял деньги", Today, CancellationToken.None);

        var cancelled = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "отмена", Today, CancellationToken.None);
        var following = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "да", Today, CancellationToken.None);
        await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Найди клиента", Today, CancellationToken.None);

        Assert.Null(cancelled.Draft);
        Assert.Contains("CRM не изменялась", cancelled.Message, StringComparison.Ordinal);
        Assert.Null(following.Draft);
        Assert.DoesNotContain(handler.LastRequestMessages.Skip(1), message => message.Contains("Помыли А123ВС54", StringComparison.Ordinal));
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ExpiredConversationDoesNotSilentlyStartWithLostContext()
    {
        var handler = new StubHandler(Command("unsupported", null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Привет", Today, CancellationToken.None);
        cache.Compact(1);

        var expired = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Замена масла", Today, CancellationToken.None);

        Assert.Contains("истёк или не найден", expired.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task CompletedFeedbackKeepsLastVehicleAsContextForNextAction()
    {
        var handler = new StubHandler(
            Command("visit", "А123ВС54", [new { name = "Мойка", price = (decimal?)500 }], 500, 500),
            Command("unsupported", null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Помыли А123ВС54 за 500", Today, CancellationToken.None);
        await service.AddFeedbackAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "completed", new string('x', 1000), CancellationToken.None);

        await service.ChatAsync("user-1", OrganizationId, Guid.Parse(first.ConversationId), "Ещё фильтр", Today, CancellationToken.None);

        var messages = handler.LastRequestMessages;
        Assert.Contains(messages, message => message.Contains("автомобиль А123ВС54", StringComparison.Ordinal));
        Assert.All(messages.Skip(1), message => Assert.True(message.Length <= 1000));
    }

    [Fact]
    public async Task RouterFailureRollsBackUserTurnBeforeRetry()
    {
        var invalidCommand = """{"action":"not_in_catalog","registrationPlate":null,"date":null,"startTime":null,"endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{}}""";
        var handler = new StubHandler(invalidCommand, Command("unsupported", null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Привет", Today, CancellationToken.None);
        var conversationId = Guid.Parse(first.ConversationId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChatAsync("user-1", OrganizationId, conversationId, "Приехал клиент", Today, CancellationToken.None));

        var second = await service.ChatAsync("user-1", OrganizationId, conversationId, "Приехал клиент", Today, CancellationToken.None);

        Assert.Contains("могу помогать", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, handler.LastRequestMessages.Count);
        Assert.Equal("Приехал клиент", handler.LastRequestMessages.Last());
        Assert.Single(handler.LastRequestMessages.Skip(1), message => message == "Приехал клиент");
    }

    [Fact]
    public async Task PartialCommandStillRejectsUnknownFields()
    {
        var invalidCommand = """{"action":"visit","registrationPlate":null,"date":null,"startTime":null,"endTime":null,"services":[],"totalAmount":null,"paidAmount":null,"paymentMethod":null,"comment":null,"parameters":{"secret":"x"}}""";
        var handler = new StubHandler(invalidCommand);
        var router = CreateRouter(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => router.PrepareConversationAsync(
            [new AiPilotConversationMessage("user", "Заехал автомобиль")], Today, CancellationToken.None));

        Assert.Equal("AI-сервис вернул некорректный ответ.", error.Message);
    }

    [Fact]
    public async Task ConversationIsScopedToUserAndOrganization()
    {
        var handler = new StubHandler(Command("visit", null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Приехал клиент", Today, CancellationToken.None);
        var id = Guid.Parse(first.ConversationId);

        var otherUser = await service.ChatAsync("user-2", OrganizationId, id, "Замена масла", Today, CancellationToken.None);
        var otherOrganization = await service.ChatAsync("user-1", Guid.NewGuid(), id, "Замена масла", Today, CancellationToken.None);

        Assert.Contains("истёк или не найден", otherUser.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("истёк или не найден", otherOrganization.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task UserTurnLimitRequiresNewConversationInsteadOfDroppingContext()
    {
        var handler = new StubHandler(Command("unsupported", null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var first = await service.ChatAsync("user-1", OrganizationId, null, "Скажи что ты умеешь", Today, CancellationToken.None);
        var id = Guid.Parse(first.ConversationId);

        for (var turn = 1; turn < 12; turn++)
        {
            await service.ChatAsync("user-1", OrganizationId, id, $"Команда номер {turn}", Today, CancellationToken.None);
        }

        var limited = await service.ChatAsync("user-1", OrganizationId, id, "Ещё команда", Today, CancellationToken.None);

        Assert.Contains("Начните новый", limited.Message, StringComparison.Ordinal);
        Assert.Equal(12, handler.RequestCount);
    }

    [Fact]
    public async Task CharacterLimitStopsBeforeAppendingUnboundedHistory()
    {
        var handler = new StubHandler(Command("unsupported", null, [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var longText = new string('x', 1000);
        var first = await service.ChatAsync("user-1", OrganizationId, null, longText, Today, CancellationToken.None);
        var conversationId = Guid.Parse(first.ConversationId);
        AiPilotChatResponse? limited = null;

        for (var turn = 1; turn < 12; turn++)
        {
            limited = await service.ChatAsync("user-1", OrganizationId, conversationId, longText, Today, CancellationToken.None);
            if (limited.Message.Contains("лимит контекста", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        Assert.NotNull(limited);
        Assert.Contains("лимит контекста", limited.Message, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(handler.RequestCount, 1, 11);
    }

    [Theory]
    [InlineData("700 рублей", "700")]
    [InlineData("0", "0")]
    [InlineData("цена 700", "700")]
    [InlineData("1 500,50 ₽", "1500.50")]
    public async Task BookingPriceReplyUpdatesOnlyRequestedWorkAndKeepsSchedule(string text, string expectedPrice)
    {
        var handler = new StubHandler(Command("book", null,
            [new { name = "Мойка", price = (decimal?)500 }, new { name = "Полировка", price = (decimal?)700 }], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var conversationId = Guid.NewGuid();
        var pending = PendingBooking();
        service.Restore("user-1", OrganizationId, conversationId,
            new AiPilotConversationSnapshot([], 1, null, pending, "book.price:1"));

        var result = await service.ChatAsync("user-1", OrganizationId, conversationId, text, Today, CancellationToken.None);

        Assert.True(result.ContinuesPendingCommand);
        Assert.NotNull(result.Draft);
        Assert.Equal(500, result.Draft.Command.Services[0].Price);
        Assert.Equal(decimal.Parse(expectedPrice, System.Globalization.CultureInfo.InvariantCulture), result.Draft.Command.Services[1].Price);
        Assert.False(result.Draft.Command.Services[1].IsPriceDeferred);
        Assert.Equal("2026-10-28", result.Draft.Command.Date);
        Assert.Equal("16:00", result.Draft.Command.StartTime);
        Assert.Equal("2", result.Draft.Command.Parameters["locationName"].GetString());
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("семьсот рублей", true)]
    [InlineData("700 за всё", false)]
    [InlineData("Итого семьсот рублей", false)]
    [InlineData("За весь заказ семьсот рублей", false)]
    public async Task BookingPriceReplyUsesSpokenAmountButDoesNotAllocateTotal(string text, bool appliesToWork)
    {
        var handler = new StubHandler(Command("book", null, [], 700, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(cache, handler);
        var conversationId = Guid.NewGuid();
        service.Restore("user-1", OrganizationId, conversationId,
            new AiPilotConversationSnapshot([], 1, null, PendingBooking(), "book.price:1"));

        var result = await service.ChatAsync("user-1", OrganizationId, conversationId, text, Today, CancellationToken.None);

        Assert.NotNull(result.Draft);
        Assert.Equal(appliesToWork ? 700m : (decimal?)null, result.Draft.Command.Services[1].Price);
        Assert.Equal(500, result.Draft.Command.Services[0].Price);
        Assert.Null(result.Draft.Command.TotalAmount);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("Пока неизвестно")]
    [InlineData("Не знаю.")]
    [InlineData("Цена пока неизвестна")]
    public async Task BookingDeferredPriceSurvivesRestoreAndDoesNotSkipNextWork(string text)
    {
        var handler = new StubHandler(Command("book", null,
            [new { name = "Мойка", price = (decimal?)null }, new { name = "Полировка", price = (decimal?)700 }], null, null));
        using var firstCache = new MemoryCache(new MemoryCacheOptions());
        var firstService = CreateService(firstCache, handler);
        var conversationId = Guid.NewGuid();
        var pending = new AiPilotCommand
        {
            Action = "book", RegistrationPlate = "А123ВС54", Date = "2026-10-28",
            Services = [new AiPilotServiceLine { Name = "Мойка" }, new AiPilotServiceLine { Name = "Полировка" }]
        };
        firstService.Restore("user-1", OrganizationId, conversationId,
            new AiPilotConversationSnapshot([], 1, null, pending, "book.price:0"));

        var deferred = await firstService.ChatAsync("user-1", OrganizationId, conversationId, text, Today, CancellationToken.None);

        Assert.NotNull(deferred.Draft);
        Assert.True(deferred.ContinuesPendingCommand);
        Assert.Null(deferred.Draft.Command.Services[0].Price);
        Assert.True(deferred.Draft.Command.Services[0].IsPriceDeferred);
        Assert.False(deferred.Draft.Command.Services[1].IsPriceDeferred);
        Assert.Equal(0, handler.RequestCount);
        var snapshot = firstService.Export("user-1", OrganizationId, conversationId) with
        {
            PendingCommand = deferred.Draft.Command, PendingDraft = null, RequestedSlot = "book.price:1"
        };
        var restored = System.Text.Json.JsonSerializer.Deserialize<AiPilotConversationSnapshot>(System.Text.Json.JsonSerializer.Serialize(snapshot))!;
        using var secondCache = new MemoryCache(new MemoryCacheOptions());
        var secondService = CreateService(secondCache, handler);
        secondService.Restore("user-1", OrganizationId, conversationId, restored);

        var result = await secondService.ChatAsync("user-1", OrganizationId, conversationId, "Полировка 700 рублей", Today, CancellationToken.None);

        Assert.NotNull(result.Draft);
        Assert.Null(result.Draft.Command.Services[0].Price);
        Assert.True(result.Draft.Command.Services[0].IsPriceDeferred);
        Assert.Equal(700, result.Draft.Command.Services[1].Price);
        Assert.False(result.Draft.Command.Services[1].IsPriceDeferred);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain("isPriceDeferred", handler.LastRequestMessages[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedPlateRequiresCorrectionAndKeepsBookingDetails()
    {
        var original = PlateBooking("A123777000");
        var handler = new StubHandler(SerializeCommand(original), Command("book", "А123АХ000", [], null, null));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreatePlateAwareService(cache, handler);

        var question = await service.ChatAsync("user-1", OrganizationId, null, "Запиши тестовую машину", Today, CancellationToken.None);
        var id = Guid.Parse(question.ConversationId);

        Assert.True(question.AwaitingAnswer);
        Assert.Null(question.Draft);
        Assert.Equal("registrationPlate.confirm", service.Export("user-1", OrganizationId, id).RequestedSlot);
        var corrected = await service.ChatAsync("user-1", OrganizationId, id, "Номер А123АХ000", Today, CancellationToken.None);

        Assert.NotNull(corrected.Draft);
        Assert.Equal("А123АХ000", corrected.Draft.Command.RegistrationPlate);
        Assert.Null(corrected.Draft.Command.ConfirmedRegistrationPlate);
        Assert.Equal("2026-10-28", corrected.Draft.Command.Date);
        Assert.Equal("16:00", corrected.Draft.Command.StartTime);
        Assert.Equal(500, Assert.Single(corrected.Draft.Command.Services).Price);
        Assert.Equal("2", corrected.Draft.Command.Parameters["locationName"].GetString());
    }

    [Theory]
    [InlineData("номер верный", true)]
    [InlineData("да", false)]
    public async Task OnlyExplicitReplyConfirmsExactNonstandardPlateWithoutCallingProvider(string answer, bool confirms)
    {
        var handler = new StubHandler(SerializeCommand(PlateBooking("A123777000")));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreatePlateAwareService(cache, handler);
        var question = await service.ChatAsync("user-1", OrganizationId, null, "Запиши тестовую машину", Today, CancellationToken.None);
        var id = Guid.Parse(question.ConversationId);

        var result = await service.ChatAsync("user-1", OrganizationId, id, answer, Today, CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
        if (!confirms)
        {
            Assert.Null(result.Draft);
            Assert.Null(service.Export("user-1", OrganizationId, id).PendingCommand?.ConfirmedRegistrationPlate);

            return;
        }

        Assert.NotNull(result.Draft);
        Assert.Equal("A123777000", result.Draft.Command.ConfirmedRegistrationPlate);
        Assert.Equal("A123777000", result.Draft.Command.RegistrationPlate);
    }

    [Fact]
    public async Task PlateReplyCanSpecifyTrailerKindInsteadOfBypassingItsFormat()
    {
        var handler = new StubHandler(SerializeCommand(PlateBooking("АА1234000")),
            Command("book", null, [], null, null, new { vehicleKind = 10 }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreatePlateAwareService(cache, handler);
        var question = await service.ChatAsync("user-1", OrganizationId, null, "Запиши тестовую технику", Today, CancellationToken.None);

        var result = await service.ChatAsync("user-1", OrganizationId, Guid.Parse(question.ConversationId), "Это прицеп", Today, CancellationToken.None);

        Assert.NotNull(result.Draft);
        Assert.Equal(10, result.Draft.Command.Parameters["vehicleKind"].GetInt32());
        Assert.Null(result.Draft.Command.ConfirmedRegistrationPlate);
        Assert.Equal("АА1234000", result.Draft.Command.RegistrationPlate);
        Assert.Equal("16:00", result.Draft.Command.StartTime);
    }

    private static string SerializeCommand(AiPilotCommand command) => JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static AiPilotCommand PlateBooking(string plate) => new()
    {
        Action = "book", RegistrationPlate = plate, Date = "2026-10-28", StartTime = "16:00",
        Services = [new AiPilotServiceLine { Name = "Мойка", Price = 500 }],
        Parameters = new Dictionary<string, JsonElement> { ["locationName"] = JsonSerializer.SerializeToElement("2") }
    };

    private static AiPilotConversationService CreatePlateAwareService(IMemoryCache cache, HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "assistant-test-settings.json")).Build();

        return new AiPilotConversationService(cache, CreateRouter(handler), new VehicleRegistrationPlatePolicy(configuration));
    }

    private static AiPilotCommand PendingBooking() => new()
    {
        Action = "book", RegistrationPlate = "А123ВС54", Date = "2026-10-28", StartTime = "16:00",
        Services = [new AiPilotServiceLine { Name = "Мойка", Price = 500 }, new AiPilotServiceLine { Name = "Полировка" }],
        Parameters = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["locationName"] = System.Text.Json.JsonSerializer.SerializeToElement("2")
        }
    };

    private static AiPilotConversationService CreateService(IMemoryCache cache, HttpMessageHandler handler) =>
        new(cache, CreateRouter(handler));

    private static RouterAiPilotClient CreateRouter(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "assistant-test-settings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiPilot:ApiKey"] = ApiKey })
            .Build();
        return new RouterAiPilotClient(new HttpClient(handler), configuration);
    }

    private static string Command(
        string action,
        string? plate,
        object[] services,
        decimal? total,
        decimal? paid,
        object? parameters = null,
        string? paymentMethod = null) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            action,
            registrationPlate = plate,
            date = (string?)null,
            startTime = (string?)null,
            endTime = (string?)null,
            services,
            totalAmount = total,
            paidAmount = paid,
            paymentMethod,
            comment = (string?)null,
            parameters = parameters ?? new { }
        });

    private sealed class StubHandler(params string[] commands) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => this._requestCount;
        public IReadOnlyList<string> LastRequestMessages { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref this._requestCount) - 1;
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                using var document = System.Text.Json.JsonDocument.Parse(body);
                this.LastRequestMessages = document.RootElement.GetProperty("messages")
                    .EnumerateArray()
                    .Select(message => message.GetProperty("content").GetString()!)
                    .ToArray();
                Assert.Equal("system", document.RootElement.GetProperty("messages").EnumerateArray().First().GetProperty("role").GetString());
                Assert.True(document.RootElement.GetProperty("messages").GetArrayLength() >= 2);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = commands[Math.Min(index, commands.Length - 1)] } } },
                    usage = new { cost = 0.001m }
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
