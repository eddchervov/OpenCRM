using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCRM.Domain.Assistant;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant.Telegram;
using Moq;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class TelegramUpdateIntegrationTests(AssistantIntegrationFixture fixture) : IClassFixture<AssistantIntegrationFixture>
{
    [Fact]
    public async Task ReadOnlyDraft_OffersShowButton()
    {
        var reply = await ProcessAssistantReplyAsync(
            "telegram-readonly",
            new FakeAssistantActionExecutor(readOnly: true),
            90002);
        var buttons = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>>(reply.Buttons);

        Assert.Contains(buttons.SelectMany(row => row), button => button.Text == "Показать");
        Assert.Contains(buttons.SelectMany(row => row), button => button.Text == "Отменить");
    }

    [Fact]
    public async Task DraftLongerThanTelegramLimit_HidesConfirmationButtons()
    {
        var maximumTextCharacters = fixture.Configuration.GetValue<int>("AssistantTelegram:MaximumTextCharacters");
        var reply = await ProcessAssistantReplyAsync(
            "telegram-long-preview",
            new FakeAssistantActionExecutor(changes: [new string('я', maximumTextCharacters)]),
            90003);

        Assert.Null(reply.Buttons);
        Assert.Contains("Подтверждение здесь отключено", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateWebhookUpdate_IsQueuedAndAppliedOnlyOnce()
    {
        var organizationId = await fixture.CreateOrganizationAsync("telegram-dedup");
        await fixture.ClearUsageAsync();
        var executor = new FakeAssistantActionExecutor();
        var router = new FakeRouterHandler([CreateVisitCommand()]);
        await using var scope = fixture.CreateAssistant(executor, router);
        var ownerUserId = await scope.Context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.OwnerUserId)
            .SingleAsync();
        const long telegramUserId = 7123456;
        scope.Context.Set<AssistantTelegramBinding>().Add(new AssistantTelegramBinding
        {
            UserId = ownerUserId,
            TelegramUserId = telegramUserId,
            ChatId = telegramUserId,
            SelectedOrganizationId = organizationId
        });
        await scope.Context.SaveChangesAsync();

        var telegramApi = new Mock<IAssistantTelegramApiClient>(MockBehavior.Strict);
        telegramApi.Setup(client => client.SendMessageAsync(
                telegramUserId,
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        var telegramService = new AssistantTelegramService(
            scope.Context,
            scope.Access,
            subscriptions.Object,
            scope.Service,
            telegramApi.Object,
            fixture.Configuration,
            fixture.Options,
            NullLogger<AssistantTelegramService>.Instance);
        using var updateDocument = JsonDocument.Parse($$"""
            {
              "update_id": 90001,
              "message": {
                "message_id": 17,
                "from": { "id": {{telegramUserId}} },
                "chat": { "id": {{telegramUserId}}, "type": "private" },
                "text": "запиши автомобиль А123ВС77 на мойку за 1000 рублей"
              }
            }
            """);

        Assert.True(await telegramService.EnqueueWebhookAsync(
            updateDocument.RootElement,
            "IntegrationWebhookSecret_123",
            CancellationToken.None));
        Assert.True(await telegramService.EnqueueWebhookAsync(
            updateDocument.RootElement,
            "IntegrationWebhookSecret_123",
            CancellationToken.None));

        var queued = await scope.Context.Set<AssistantTelegramUpdate>().Where(item => item.UpdateId == 90001).ToListAsync();
        Assert.Single(queued);
        Assert.True(await telegramService.ProcessNextUpdateAsync(CancellationToken.None));
        await scope.Context.Entry(queued[0]).ReloadAsync();

        Assert.Equal("completed", queued[0].Status);
        Assert.Single(router.RequestBodies);
        Assert.Equal(1, await scope.Context.Set<AssistantDraft>().CountAsync(item => item.OrganizationId == organizationId));
        telegramApi.Verify(client => client.SendMessageAsync(
            telegramUserId,
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, executor.ExecutionCount);
    }

    private async Task<(string Text, IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>? Buttons)> ProcessAssistantReplyAsync(
        string organizationSuffix,
        FakeAssistantActionExecutor executor,
        long updateId)
    {
        var organizationId = await fixture.CreateOrganizationAsync(organizationSuffix);
        await fixture.ClearUsageAsync();
        var router = new FakeRouterHandler([CreateVisitCommand()]);
        await using var scope = fixture.CreateAssistant(executor, router);
        var ownerUserId = await scope.Context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.OwnerUserId)
            .SingleAsync();
        var telegramUserId = updateId + 7123457;
        scope.Context.Set<AssistantTelegramBinding>().Add(new AssistantTelegramBinding
        {
            UserId = ownerUserId,
            TelegramUserId = telegramUserId,
            ChatId = telegramUserId,
            SelectedOrganizationId = organizationId
        });
        await scope.Context.SaveChangesAsync();

        string? sentText = null;
        IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>? sentButtons = null;
        var telegramApi = new Mock<IAssistantTelegramApiClient>(MockBehavior.Strict);
        telegramApi.Setup(client => client.SendMessageAsync(
                telegramUserId,
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<long, string, IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>?, CancellationToken>((_, text, buttons, _) =>
            {
                sentText = text;
                sentButtons = buttons;
            })
            .Returns(Task.CompletedTask);
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        var telegramService = new AssistantTelegramService(
            scope.Context,
            scope.Access,
            subscriptions.Object,
            scope.Service,
            telegramApi.Object,
            fixture.Configuration,
            fixture.Options,
            NullLogger<AssistantTelegramService>.Instance);
        using var updateDocument = JsonDocument.Parse($$"""
            {
              "update_id": {{updateId}},
              "message": {
                "message_id": 18,
                "from": { "id": {{telegramUserId}} },
                "chat": { "id": {{telegramUserId}}, "type": "private" },
                "text": "создай визит"
              }
            }
            """);
        Assert.True(await telegramService.EnqueueWebhookAsync(
            updateDocument.RootElement,
            "IntegrationWebhookSecret_123",
            CancellationToken.None));
        Assert.True(await telegramService.ProcessNextUpdateAsync(CancellationToken.None));

        return (sentText!, sentButtons);
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
