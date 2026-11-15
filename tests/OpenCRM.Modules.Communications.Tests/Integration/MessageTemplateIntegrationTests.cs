using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using MaintenanceRecord = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Communications.Tests.Integration;

[Collection(ConversationDatabaseCollection.Name)]
public sealed class MessageTemplateIntegrationTests(ConversationDatabaseFixture fixture)
{
    [Fact]
    public async Task MaintenanceAndTemplateSnapshotSurviveRetryAfterTemplateArchiving()
    {
        await using var context = fixture.CreateContext();
        using var services = fixture.CreateServices(context);
        var conversations = services.GetRequiredService<IOrganizationConversationService>();
        var templates = services.GetRequiredService<IOrganizationMessageTemplateService>();
        var scenario = await this.CreateScenarioAsync(context, conversations, 13101);
        var group = await templates.SaveGroupAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateGroupDto { Name = "Запись" });
        var template = await templates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId, new SaveMessageTemplateDto
        {
            GroupId = group.Id,
            Name = "Подтверждение",
            Body = "{{client.name}}, ждём {{maintenance.date}} в {{maintenance.time}}. {{maintenance.vehicle}}"
        });
        var preview = await templates.PreviewAsync(scenario.UserId, scenario.OrganizationId, new PreviewMessageTemplateDto
        {
            ConversationId = scenario.ConversationId,
            TemplateId = template.Id,
            TemplateVersion = template.Version,
            MaintenanceId = scenario.Maintenance.Id
        });
        Assert.Contains("Иван", preview.Text);
        Assert.Contains("23.10.2026 в 10:00", preview.Text);
        Assert.Contains("М777ММ154", preview.Text);
        var request = new SendConversationMessageDto
        {
            RequestId = Guid.NewGuid(),
            Text = preview.Text + " Дополнение сотрудника.",
            MaintenanceId = scenario.Maintenance.Id,
            MessageTemplateId = template.Id,
            MessageTemplateVersion = template.Version
        };

        var sent = await conversations.SendAsync(scenario.UserId, scenario.OrganizationId, scenario.ConversationId, request);
        Assert.Equal(scenario.Maintenance.Id, sent.MaintenanceId);
        Assert.Equal(template.Id, sent.MessageTemplateId);
        Assert.Equal(template.Version, sent.MessageTemplateVersion);
        Assert.True(await templates.ArchiveTemplateAsync(scenario.UserId, scenario.OrganizationId, template.Id, template.Version));
        var repeated = await conversations.SendAsync(scenario.UserId, scenario.OrganizationId, scenario.ConversationId, request);
        Assert.Equal(sent.Id, repeated.Id);
        Assert.Equal(request.Text, repeated.Text);
        Assert.Equal(1, await context.OrganizationConversationMessages.CountAsync(item => item.RequestId == request.RequestId));
        await Assert.ThrowsAsync<ArgumentException>(() => conversations.SendAsync(scenario.UserId,
            scenario.OrganizationId, scenario.ConversationId, request with { RequestId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task TemplateVersionsPermissionsAndVariablesAreValidated()
    {
        await using var context = fixture.CreateContext();
        using var services = fixture.CreateServices(context);
        var conversations = services.GetRequiredService<IOrganizationConversationService>();
        var templates = services.GetRequiredService<IOrganizationMessageTemplateService>();
        var scenario = await this.CreateScenarioAsync(context, conversations, 13102);
        var group = await templates.SaveGroupAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateGroupDto { Name = "Ответы" });
        var template = await templates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateDto { GroupId = group.Id, Name = "Приветствие", Body = "Добрый день!" });
        var update = new SaveMessageTemplateDto
        {
            Id = template.Id, GroupId = group.Id, Name = template.Name, Body = "Здравствуйте!", ExpectedVersion = template.Version
        };
        var updated = await templates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId, update);
        Assert.Equal(template.Version + 1, updated.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => templates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId, update));
        await Assert.ThrowsAsync<InvalidOperationException>(() => templates.ArchiveTemplateAsync(scenario.UserId,
            scenario.OrganizationId, template.Id, template.Version));
        await Assert.ThrowsAsync<ArgumentException>(() => templates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateDto { GroupId = group.Id, Name = "Ошибка", Body = "{{client.password}}" }));
        using var restricted = fixture.CreateServices(context, new HashSet<string> { "messages.templates.manage" });
        var restrictedTemplates = restricted.GetRequiredService<IOrganizationMessageTemplateService>();
        Assert.Single(await restrictedTemplates.GetGroupsAsync(scenario.UserId, scenario.OrganizationId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restrictedTemplates.SaveGroupAsync(scenario.UserId,
            scenario.OrganizationId, new SaveMessageTemplateGroupDto { Name = "Запрещено" }));
    }

    [Fact]
    public async Task ExplicitBuyerTakesPrecedenceOverVehicleOwnerForMaintenanceContext()
    {
        await using var context = fixture.CreateContext();
        using var services = fixture.CreateServices(context);
        var conversations = services.GetRequiredService<IOrganizationConversationService>();
        var templates = services.GetRequiredService<IOrganizationMessageTemplateService>();
        var scenario = await this.CreateScenarioAsync(context, conversations, 13103);
        var otherBuyer = new OrganizationClient { OrganizationId = scenario.OrganizationId, Name = "Другой заказчик" };
        context.OrganizationClients.Add(otherBuyer);
        scenario.Maintenance.BuyerClientId = otherBuyer.Id;
        await context.SaveChangesAsync();
        var group = await templates.SaveGroupAsync(scenario.UserId, scenario.OrganizationId, new SaveMessageTemplateGroupDto { Name = "Запись" });
        var template = await templates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateDto { GroupId = group.Id, Name = "Дата", Body = "{{maintenance.date}}" });
        var preview = new PreviewMessageTemplateDto
        {
            ConversationId = scenario.ConversationId, TemplateId = template.Id,
            TemplateVersion = template.Version, MaintenanceId = scenario.Maintenance.Id
        };
        await Assert.ThrowsAsync<ArgumentException>(() => templates.PreviewAsync(scenario.UserId, scenario.OrganizationId, preview));
        await Assert.ThrowsAsync<ArgumentException>(() => conversations.SendAsync(scenario.UserId, scenario.OrganizationId,
            scenario.ConversationId, new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Проверка", MaintenanceId = scenario.Maintenance.Id }));
        scenario.Maintenance.BuyerClientId = null;
        await context.SaveChangesAsync();
        Assert.Equal("23.10.2026", (await templates.PreviewAsync(scenario.UserId, scenario.OrganizationId, preview)).Text);
        using var restricted = fixture.CreateServices(context, new HashSet<string> { "maintenance.view" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restricted.GetRequiredService<IOrganizationMessageTemplateService>()
            .PreviewAsync(scenario.UserId, scenario.OrganizationId, preview));
    }

    [Fact]
    public async Task FreeWorkspaceCannotReadOrManageTemplatesWhilePaidWorkspaceCan()
    {
        await using var context = fixture.CreateContext();
        using var paidServices = fixture.CreateServices(context);
        var conversations = paidServices.GetRequiredService<IOrganizationConversationService>();
        var templates = paidServices.GetRequiredService<IOrganizationMessageTemplateService>();
        var scenario = await this.CreateScenarioAsync(context, conversations, 13104);
        var group = await templates.SaveGroupAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateGroupDto { Name = "Платные шаблоны" });
        var template = await templates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateDto { GroupId = group.Id, Name = "Ответ", Body = "Здравствуйте!" });

        using var freeServices = fixture.CreateServices(context, hasClientMessagingFeature: false);
        var freeTemplates = freeServices.GetRequiredService<IOrganizationMessageTemplateService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => freeTemplates.GetGroupsAsync(scenario.UserId, scenario.OrganizationId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => freeTemplates.SaveGroupAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateGroupDto { Name = "Запрещено" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => freeTemplates.SaveTemplateAsync(scenario.UserId, scenario.OrganizationId,
            new SaveMessageTemplateDto { GroupId = group.Id, Name = "Запрещено", Body = "Текст" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => freeTemplates.ArchiveTemplateAsync(scenario.UserId,
            scenario.OrganizationId, template.Id, template.Version));
        await Assert.ThrowsAsync<InvalidOperationException>(() => freeTemplates.PreviewAsync(scenario.UserId, scenario.OrganizationId,
            new PreviewMessageTemplateDto { ConversationId = scenario.ConversationId, TemplateId = template.Id, TemplateVersion = template.Version }));
        Assert.Single(await templates.GetGroupsAsync(scenario.UserId, scenario.OrganizationId));
    }

    private async Task<(Guid OrganizationId, string UserId, Guid ConversationId, MaintenanceRecord Maintenance)> CreateScenarioAsync(
        OpenCRMContext context,
        IOrganizationConversationService conversations,
        long botId)
    {
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var client = new OrganizationClient { OrganizationId = organization.Id, Name = "Иван" };
        var vehicle = new Vehicle
        {
            OrganizationId = organization.Id, OrganizationClientId = client.Id,
            UserId = owner.Id, RegistrationPlate = "М777ММ154"
        };
        var maintenance = new MaintenanceRecord
        {
            VehicleId = vehicle.Id, BuyerClientId = client.Id, UserId = owner.Id,
            Date = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc),
            PlannedStartTime = TimeSpan.FromHours(10), Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance
        };
        context.OrganizationClients.Add(client);
        context.Vehicles.Add(vehicle);
        context.Maintenance.Add(maintenance);
        context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, botId));
        await context.SaveChangesAsync();
        await conversations.IngestTelegramMessageAsync(new IngestTelegramMessageDto
        {
            OrganizationId = organization.Id, BotId = botId, BotUsername = $"studio_{botId}_bot",
            UpdateId = botId, ChatId = botId + 100000, SenderTelegramUserId = botId + 100000,
            ProviderMessageId = 1, Text = "Здравствуйте", SentAt = DateTime.UtcNow
        });
        var conversation = await context.OrganizationConversations.SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.True(await conversations.LinkClientAsync(owner.Id, organization.Id, conversation.Id, client.Id));

        return (organization.Id, owner.Id, conversation.Id, maintenance);
    }
}
