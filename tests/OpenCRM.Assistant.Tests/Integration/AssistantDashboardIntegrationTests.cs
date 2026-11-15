using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class AssistantDashboardIntegrationTests(AssistantIntegrationFixture fixture) : IClassFixture<AssistantIntegrationFixture>
{
    [Fact]
    public async Task History_RemainsReadableWhenAssistantFeatureExpires()
    {
        var organizationId = await fixture.CreateOrganizationAsync("history-after-plan");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        var conversation = AddConversation(context, organizationId, owner, "web", "Сохранённая история");
        await context.SaveChangesAsync();
        var service = this.CreateService(context, hasAssistantFeature: false);

        var history = AsJson(await service.GetHistoryAsync(new AssistantActor(owner, organizationId, "web"), null, 1, CancellationToken.None));

        Assert.Contains(history.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == conversation.Id);
        Assert.All(history.GetProperty("items").EnumerateArray(), item => Assert.False(item.GetProperty("canResume").GetBoolean()));
        var detail = AsJson((await service.GetConversationAsync(new AssistantActor(owner, organizationId, "web"), conversation.Id, 1, CancellationToken.None))!);
        Assert.False(detail.GetProperty("canResume").GetBoolean());
        Assert.Equal("Сохранённая история", Assert.Single(detail.GetProperty("messages").EnumerateArray()).GetProperty("text").GetString());
    }

    [Fact]
    public async Task History_ShowsClosedTelegramAndWebConversationsOnlyInsideOrganization()
    {
        var organizationId = await fixture.CreateOrganizationAsync("history-owner");
        var otherOrganizationId = await fixture.CreateOrganizationAsync("history-other");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        var staff = await AddMemberAsync(context, organizationId);
        var web = AddConversation(context, organizationId, owner, "web", "Своя запись");
        var telegram = AddConversation(context, organizationId, staff, "telegram", "Архив Telegram", isClosed: true);
        AddConversation(context, otherOrganizationId, await fixture.GetOwnerUserIdAsync(otherOrganizationId), "web", "Другая компания");
        await context.SaveChangesAsync();
        var service = this.CreateService(context);
        var actor = new AssistantActor(owner, organizationId, "web");

        var history = AsJson(await service.GetHistoryAsync(actor, null, 1, CancellationToken.None));
        var detail = AsJson((await service.GetConversationAsync(actor, telegram.Id, 1, CancellationToken.None))!);

        Assert.Equal("organization", history.GetProperty("scope").GetString());
        Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
        Assert.Contains(history.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == web.Id);
        Assert.False(detail.GetProperty("canResume").GetBoolean());
        Assert.True(detail.GetProperty("isClosed").GetBoolean());
        Assert.Equal("Архив Telegram", Assert.Single(detail.GetProperty("messages").EnumerateArray()).GetProperty("text").GetString());
        Assert.False(detail.TryGetProperty("draft", out _));
        Assert.False(detail.TryGetProperty("engineStateJson", out _));
    }

    [Fact]
    public async Task History_EmployeeCannotReadColleagueEvenWithFinanceView()
    {
        var organizationId = await fixture.CreateOrganizationAsync("history-self");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        var staff = await AddMemberAsync(context, organizationId);
        AddConversation(context, organizationId, staff, "web", "Своя беседа");
        var colleague = AddConversation(context, organizationId, owner, "telegram", "Чужая беседа");
        await context.SaveChangesAsync();
        var actor = new AssistantActor(staff, organizationId, "web");
        var service = this.CreateService(context);

        var history = AsJson(await service.GetHistoryAsync(actor, null, 1, CancellationToken.None));

        Assert.Equal("self", history.GetProperty("scope").GetString());
        Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
        Assert.Null(await service.GetConversationAsync(actor, colleague.Id, 1, CancellationToken.None));
    }

    [Fact]
    public async Task History_AdminReadsTeamButOnlyOwnActiveWebConversationCanResume()
    {
        var organizationId = await fixture.CreateOrganizationAsync("history-admin");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        var admin = await AddMemberAsync(context, organizationId, OrganizationRoleCodes.Admin);
        var active = AddConversation(context, organizationId, admin, "web", "Активная");
        var other = AddConversation(context, organizationId, owner, "web", "Другой автор");
        var telegram = AddConversation(context, organizationId, admin, "telegram", "Другой канал");
        var expired = AddConversation(context, organizationId, admin, "web", "Истекла");
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        var actor = new AssistantActor(admin, organizationId, "web");
        var service = this.CreateService(context);

        var history = AsJson(await service.GetHistoryAsync(actor, null, 1, CancellationToken.None));

        Assert.Equal("organization", history.GetProperty("scope").GetString());
        var items = history.GetProperty("items").EnumerateArray().ToArray();
        Assert.True(items.Single(item => item.GetProperty("id").GetGuid() == active.Id).GetProperty("canResume").GetBoolean());
        Assert.All(items.Where(item => item.GetProperty("id").GetGuid() != active.Id), item => Assert.False(item.GetProperty("canResume").GetBoolean()));
        Assert.Null(await service.GetConversationAsync(actor, Guid.NewGuid(), 1, CancellationToken.None));
    }

    [Fact]
    public async Task Usage_SeparatesEmployeesChannelsActualCostsAndUnknownReservations()
    {
        var organizationId = await fixture.CreateOrganizationAsync("usage-team");
        var otherOrganizationId = await fixture.CreateOrganizationAsync("usage-other");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        var staff = await AddMemberAsync(context, organizationId);
        var now = DateTime.UtcNow;
        AddUsage(context, organizationId, owner, "web", now, 3, 2);
        AddUsage(context, organizationId, staff, "telegram", now, null, 2);
        AddUsage(context, otherOrganizationId, await fixture.GetOwnerUserIdAsync(otherOrganizationId), "web", now, 100, 2);
        await context.SaveChangesAsync();
        var service = this.CreateService(context);

        var result = AsJson(await service.GetUsageAsync(new AssistantActor(owner, organizationId, "web"), null, null, null, CancellationToken.None));
        var self = AsJson(await service.GetUsageAsync(new AssistantActor(staff, organizationId, "web"), null, null, null, CancellationToken.None));
        var telegram = AsJson(await service.GetUsageAsync(new AssistantActor(owner, organizationId, "web"), null, null, "telegram", CancellationToken.None));

        Assert.Equal("organization", result.GetProperty("scope").GetString());
        Assert.Equal(3, result.GetProperty("totals").GetProperty("costRub").GetDecimal());
        Assert.Equal(2, result.GetProperty("totals").GetProperty("reservedRub").GetDecimal());
        Assert.Equal(5, result.GetProperty("day").GetProperty("chargedRub").GetDecimal());
        Assert.Equal(995, result.GetProperty("day").GetProperty("remainingRub").GetDecimal());
        Assert.Equal(2, result.GetProperty("byUser").GetArrayLength());
        Assert.Equal(2, result.GetProperty("byChannel").GetArrayLength());
        Assert.Equal("self", self.GetProperty("scope").GetString());
        Assert.Equal(1, self.GetProperty("byUser").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, self.GetProperty("totals").GetProperty("costRub").ValueKind);
        Assert.Equal(1, self.GetProperty("totals").GetProperty("unknownCount").GetInt32());
        Assert.Equal(1, telegram.GetProperty("totals").GetProperty("requestCount").GetInt32());
        Assert.Equal(5, telegram.GetProperty("day").GetProperty("chargedRub").GetDecimal());
    }

    [Fact]
    public async Task Usage_UsesOrganizationTimezoneForPeriodAndRejectsInvalidPeriods()
    {
        var organizationId = await fixture.CreateOrganizationAsync("usage-timezone");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        AddUsage(context, organizationId, owner, "web", new DateTime(2026, 1, 1, 16, 59, 59, DateTimeKind.Utc), 1, 2);
        AddUsage(context, organizationId, owner, "web", new DateTime(2026, 1, 1, 17, 0, 0, DateTimeKind.Utc), 4, 2);
        AddUsage(context, organizationId, owner, "web", new DateTime(2026, 1, 2, 17, 0, 0, DateTimeKind.Utc), 8, 2);
        await context.SaveChangesAsync();
        var options = AssistantOptions.Load(fixture.Configuration);
        options.BudgetTimeZone = "UTC";
        options.DefaultTimeZone = "Europe/Moscow";
        await context.Organizations.Where(item => item.Id == organizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ShiftTimeZoneId, "Asia/Novosibirsk"));
        var service = this.CreateService(context, options);
        var actor = new AssistantActor(owner, organizationId, "web");
        var date = new DateOnly(2026, 1, 2);

        var result = AsJson(await service.GetUsageAsync(actor, date, date, null, CancellationToken.None));

        Assert.Equal(4, result.GetProperty("totals").GetProperty("costRub").GetDecimal());
        Assert.Equal("Asia/Novosibirsk", result.GetProperty("timeZone").GetString());
        Assert.Equal("UTC", result.GetProperty("budgetTimeZone").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetUsageAsync(actor, date, date.AddDays(-1), null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetUsageAsync(actor, date, date.AddDays(options.MaximumUsagePeriodDays), null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetUsageAsync(actor, null, null, "other", CancellationToken.None));
    }

    [Theory]
    [InlineData("America/Santiago", 2026, 9, 6, 4, 3)]
    [InlineData("America/Havana", 2025, 11, 2, 4, 5)]
    public async Task Usage_IncludesEntireLocalDayAcrossMidnightClockChanges(
        string timeZoneId,
        int year,
        int month,
        int day,
        int startHourUtc,
        int endHourUtc)
    {
        var organizationId = await fixture.CreateOrganizationAsync("usage-midnight-transition");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        await context.Organizations.Where(item => item.Id == organizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ShiftTimeZoneId, timeZoneId));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var midnight = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        Assert.True(zone.IsAmbiguousTime(midnight) || zone.IsInvalidTime(midnight));
        var start = new DateTime(year, month, day, startHourUtc, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year, month, day, endHourUtc, 0, 0, DateTimeKind.Utc).AddDays(1);
        AddUsage(context, organizationId, owner, "web", start.AddSeconds(-1), 1, 2);
        AddUsage(context, organizationId, owner, "web", start, 4, 2);
        AddUsage(context, organizationId, owner, "web", start.AddHours(1), 8, 2);
        AddUsage(context, organizationId, owner, "web", end.AddSeconds(-1), 16, 2);
        AddUsage(context, organizationId, owner, "web", end, 32, 2);
        await context.SaveChangesAsync();
        var service = this.CreateService(context);
        var date = new DateOnly(year, month, day);

        var result = AsJson(await service.GetUsageAsync(
            new AssistantActor(owner, organizationId, "web"), date, date, null, CancellationToken.None));

        Assert.Equal(28, result.GetProperty("totals").GetProperty("costRub").GetDecimal());
        Assert.Equal(3, result.GetProperty("totals").GetProperty("requestCount").GetInt32());
    }

    [Fact]
    public async Task Transcript_HidesEngineNotesAndPrematureDraftButPreservesUserText()
    {
        var organizationId = await fixture.CreateOrganizationAsync("transcript-projection");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        const string note = "Интерфейс отметил предыдущий черновик выполненным; служебная инструкция";
        var conversation = AddConversation(context, organizationId, owner, "telegram", note);
        var messages = new[]
        {
            ("assistant", "Подготовила черновик: предварительный"),
            ("assistant", "Какая цена?"),
            ("user", "500 рублей"),
            ("assistant", "Подготовила черновик: итоговый"),
            ("assistant", note),
            ("assistant", "Результат интерфейса (completed): технические подробности"),
            ("assistant", "Результат интерфейса (failed): техническая ошибка"),
            ("assistant", "Результат интерфейса (clarification): служебное уточнение")
        };
        for (var index = 0; index < messages.Length; index++)
        {
            context.Add(new AssistantMessage { ConversationId = conversation.Id, Sequence = index + 2, Role = messages[index].Item1, Text = messages[index].Item2 });
        }
        await context.SaveChangesAsync();
        var service = this.CreateService(context);

        var result = AsJson((await service.GetConversationAsync(new AssistantActor(owner, organizationId, "web"), conversation.Id, 1, CancellationToken.None))!);
        var texts = result.GetProperty("messages").EnumerateArray().Select(item => item.GetProperty("text").GetString()).ToArray();

        Assert.Equal(new[] { note, "Какая цена?", "500 рублей", "Подготовила черновик: итоговый", "Действие выполнено.", "Действие не выполнено." }, texts);
        Assert.Equal(6, result.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Transcript_PaginatesVisibleMessagesWithoutGaps()
    {
        var organizationId = await fixture.CreateOrganizationAsync("transcript-pages");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        var conversation = AddConversation(context, organizationId, owner, "web", "Сообщение 0");
        context.Add(new AssistantMessage { ConversationId = conversation.Id, Sequence = 2, Role = "assistant",
            Text = "Интерфейс отметил предыдущий черновик выполненным; служебная инструкция" });
        for (var index = 1; index <= fixture.Options.LookupPageSize; index++)
        {
            context.Add(new AssistantMessage { ConversationId = conversation.Id, Sequence = index + 2, Role = "user", Text = "Сообщение " + index });
        }
        await context.SaveChangesAsync();
        var service = this.CreateService(context);
        var actor = new AssistantActor(owner, organizationId, "web");

        var first = AsJson((await service.GetConversationAsync(actor, conversation.Id, 1, CancellationToken.None))!);
        var second = AsJson((await service.GetConversationAsync(actor, conversation.Id, 2, CancellationToken.None))!);

        Assert.Equal(fixture.Options.LookupPageSize + 1, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(fixture.Options.LookupPageSize, first.GetProperty("messages").GetArrayLength());
        Assert.Equal("Сообщение " + fixture.Options.LookupPageSize,
            Assert.Single(second.GetProperty("messages").EnumerateArray()).GetProperty("text").GetString());
    }

    [Fact]
    public async Task Dashboard_DeniesOrganizationNonmemberAndDisabledAssistant()
    {
        var organizationId = await fixture.CreateOrganizationAsync("dashboard-access");
        var otherId = await fixture.CreateOrganizationAsync("dashboard-stranger");
        var owner = await fixture.GetOwnerUserIdAsync(organizationId);
        var otherOwner = await fixture.GetOwnerUserIdAsync(otherId);
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context);
        var stranger = new AssistantActor(otherOwner, organizationId, "web");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetHistoryAsync(stranger, null, 1, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetUsageAsync(stranger, null, null, null, CancellationToken.None));
        await fixture.SetAssistantEnabledAsync(false);
        Assert.NotNull(await service.GetHistoryAsync(new AssistantActor(owner, organizationId, "web"), null, 1, CancellationToken.None));
    }

    private AssistantDashboardService CreateService(
        OpenCRMContext context,
        AssistantOptions? options = null,
        bool hasAssistantFeature = true)
    {
        options ??= fixture.Options;
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(item => item.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(async (string userId, Guid organizationId, string _) =>
            {
                if (!await context.Organizations.AnyAsync(item => item.Id == organizationId && item.OwnerUserId == userId)
                    && !await context.Set<OrganizationMember>().AnyAsync(item => item.OrganizationId == organizationId && item.UserId == userId))
                {
                    throw new UnauthorizedAccessException("Нет доступа к организации.");
                }
            });
        subscriptions.Setup(item => item.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.Assistant))
            .ReturnsAsync(hasAssistantFeature);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(item => item.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(async (string userId, Guid organizationId, string permission) => permission == OrganizationPermissionCodes.FinanceView
                || await context.Organizations.AnyAsync(item => item.Id == organizationId && item.OwnerUserId == userId));
        var access = new AssistantAccessService(context, subscriptions.Object, permissions.Object, options);

        return new AssistantDashboardService(context, access, permissions.Object, subscriptions.Object, options);
    }

    private static async Task<string> AddMemberAsync(OpenCRMContext context, Guid organizationId, string role = OrganizationRoleCodes.Employee)
    {
        var userId = Guid.NewGuid().ToString();
        context.Users.Add(new ApplicationUser { Id = userId, UserName ="Сотрудник " + userId });
        context.Set<OrganizationMember>().Add(new OrganizationMember { OrganizationId = organizationId, UserId = userId, RoleCode = role });
        await context.SaveChangesAsync();

        return userId;
    }

    private static AssistantConversation AddConversation(OpenCRMContext context, Guid organizationId, string userId, string channel, string text, bool isClosed = false)
    {
        var conversation = new AssistantConversation { OrganizationId = organizationId, UserId = userId, Channel = channel,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30), IsClosed = isClosed };
        context.Add(conversation);
        context.Add(new AssistantMessage { ConversationId = conversation.Id, Role = "user", Text = text, Sequence = 1 });

        return conversation;
    }

    private static void AddUsage(OpenCRMContext context, Guid organizationId, string userId, string channel, DateTime at, decimal? cost, decimal reservation)
    {
        context.Add(new AssistantUsage { OrganizationId = organizationId, UserId = userId, Channel = channel, CreatedAt = at,
            CostRub = cost, ReservedCostRub = reservation, Status = cost.HasValue ? "completed" : "unknown", InputTokens = 12, OutputTokens = 4 });
    }

    private static JsonElement AsJson(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
