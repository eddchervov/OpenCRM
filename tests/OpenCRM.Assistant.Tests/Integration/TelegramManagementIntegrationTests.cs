using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant.Telegram;
using Moq;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class TelegramManagementIntegrationTests(AssistantIntegrationFixture fixture) : IClassFixture<AssistantIntegrationFixture>
{
    [Fact]
    public async Task Status_ReportsOwnGlobalBindingWhenItBelongsToAnotherOrganization()
    {
        var selectedOrganizationId = await fixture.CreateOrganizationAsync("telegram-status-selected");
        var boundOrganizationId = await fixture.CreateOrganizationAsync("telegram-status-bound");
        var userId = await fixture.GetOwnerUserIdAsync(selectedOrganizationId);
        await this.AddMemberAsync(selectedOrganizationId, userId, OrganizationRoleCodes.Admin);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        scope.Context.Set<AssistantTelegramBinding>().Add(new AssistantTelegramBinding
        {
            UserId = userId,
            TelegramUserId = 9_223_372_036_854_775_000,
            ChatId = 9_223_372_036_854_775_000,
            SelectedOrganizationId = boundOrganizationId
        });
        await scope.Context.SaveChangesAsync();
        var service = this.CreateService(scope);

        var status = await service.GetStatusAsync(userId, selectedOrganizationId, CancellationToken.None);

        Assert.True(status.IsBound);
        Assert.False(status.IsConnected);
        Assert.Equal(boundOrganizationId, status.OrganizationId);
        Assert.Equal("9223372036854775000", status.TelegramUserId);
        Assert.NotNull(status.ConnectedAt);
        Assert.Equal("integration_test_bot", status.BotUsername);
        Assert.True(status.CanViewOrganizationConnections);
    }

    [Fact]
    public async Task OrganizationConnections_AreRestrictedAndExcludeOtherOrgFormerAndRevokedBindings()
    {
        var organizationId = await fixture.CreateOrganizationAsync("telegram-connections");
        var otherOrganizationId = await fixture.CreateOrganizationAsync("telegram-connections-other");
        var ownerId = await fixture.GetOwnerUserIdAsync(organizationId);
        var adminId = await fixture.GetOwnerUserIdAsync(otherOrganizationId);
        var employeeOrganizationId = await fixture.CreateOrganizationAsync("telegram-connections-employee");
        var employeeId = await fixture.GetOwnerUserIdAsync(employeeOrganizationId);
        var formerOrganizationId = await fixture.CreateOrganizationAsync("telegram-connections-former");
        var formerId = await fixture.GetOwnerUserIdAsync(formerOrganizationId);
        var otherOrganizationMemberId = await fixture.GetOwnerUserIdAsync(await fixture.CreateOrganizationAsync("telegram-connections-unrelated"));
        var revokedBindingUserId = await fixture.GetOwnerUserIdAsync(await fixture.CreateOrganizationAsync("telegram-connections-revoked"));
        await this.AddMemberAsync(organizationId, ownerId, OrganizationRoleCodes.Employee);
        await this.AddMemberAsync(organizationId, adminId, OrganizationRoleCodes.Admin);
        await this.AddMemberAsync(organizationId, employeeId, OrganizationRoleCodes.Employee);
        await this.AddMemberAsync(organizationId, formerId, OrganizationRoleCodes.Employee);
        await this.AddMemberAsync(organizationId, otherOrganizationMemberId, OrganizationRoleCodes.Employee);
        await this.AddMemberAsync(organizationId, revokedBindingUserId, OrganizationRoleCodes.Employee);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        scope.Context.Set<AssistantTelegramBinding>().AddRange(
            TelegramManagementIntegrationTests.Binding(ownerId, organizationId, 101),
            TelegramManagementIntegrationTests.Binding(adminId, organizationId, 102),
            TelegramManagementIntegrationTests.Binding(employeeId, organizationId, 103),
            TelegramManagementIntegrationTests.Binding(formerId, organizationId, 104),
            TelegramManagementIntegrationTests.Binding(otherOrganizationMemberId, otherOrganizationId, 105),
            TelegramManagementIntegrationTests.Binding(revokedBindingUserId, organizationId, 106, DateTime.UtcNow));
        await scope.Context.SaveChangesAsync();
        await scope.Context.OrganizationMembers.Where(member => member.OrganizationId == organizationId && member.UserId == formerId)
            .ExecuteDeleteAsync();
        var service = this.CreateService(scope);

        var ownerPage = await service.GetOrganizationConnectionsAsync(ownerId, organizationId, 1, CancellationToken.None);
        var adminPage = await service.GetOrganizationConnectionsAsync(adminId, organizationId, 1, CancellationToken.None);
        var secondPage = await service.GetOrganizationConnectionsAsync(ownerId, organizationId, 2, CancellationToken.None);

        Assert.Equal(3, ownerPage.TotalCount);
        Assert.Equal(3, adminPage.TotalCount);
        Assert.Equal(1, ownerPage.Page);
        Assert.Equal(fixture.Options.LookupPageSize, ownerPage.PageSize);
        Assert.Equal(2, secondPage.Page);
        Assert.Empty(secondPage.Items);
        Assert.Contains(ownerPage.Items, item => item.TelegramUserId == "101");
        Assert.Contains(ownerPage.Items, item => item.TelegramUserId == "102");
        Assert.Contains(ownerPage.Items, item => item.TelegramUserId == "103");
        Assert.Contains(ownerPage.Items, item => item.TelegramUserId == "101" && item.IsCurrentUser);
        Assert.DoesNotContain(ownerPage.Items, item => item.TelegramUserId != "101" && item.IsCurrentUser);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetOrganizationConnectionsAsync(employeeId, organizationId, 1, CancellationToken.None));
    }

    [Fact]
    public async Task Disconnect_InvalidatesPendingLinksAndRevokesOnlyOwnBinding()
    {
        var organizationId = await fixture.CreateOrganizationAsync("telegram-disconnect");
        var otherOrganizationId = await fixture.CreateOrganizationAsync("telegram-disconnect-other");
        var userId = await fixture.GetOwnerUserIdAsync(organizationId);
        var bindingUserId = await fixture.GetOwnerUserIdAsync(await fixture.CreateOrganizationAsync("telegram-disconnect-binding"));
        var otherUserId = await fixture.GetOwnerUserIdAsync(otherOrganizationId);
        await this.AddMemberAsync(organizationId, userId, OrganizationRoleCodes.Admin);
        await this.AddMemberAsync(organizationId, bindingUserId, OrganizationRoleCodes.Employee);
        await this.AddMemberAsync(otherOrganizationId, otherUserId, OrganizationRoleCodes.Admin);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        var service = this.CreateService(scope);
        await service.CreateLinkAsync(userId, organizationId, CancellationToken.None);
        var bindingLink = await service.CreateLinkAsync(bindingUserId, organizationId, CancellationToken.None);
        var conversation = new AssistantConversation
        {
            OrganizationId = organizationId,
            UserId = bindingUserId,
            Channel = "telegram",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        scope.Context.AssistantConversations.Add(conversation);
        scope.Context.Set<AssistantTelegramBinding>().Add(new AssistantTelegramBinding
        {
            UserId = bindingUserId,
            TelegramUserId = 205,
            ChatId = 205,
            SelectedOrganizationId = organizationId,
            ConversationId = conversation.Id
        });
        scope.Context.Set<AssistantTelegramBinding>().Add(new AssistantTelegramBinding
        {
            UserId = otherUserId,
            TelegramUserId = 206,
            ChatId = 206,
            SelectedOrganizationId = otherOrganizationId
        });
        await scope.Context.SaveChangesAsync();
        await scope.Context.OrganizationMembers.Where(member => member.OrganizationId == organizationId && member.UserId == userId)
            .ExecuteDeleteAsync();

        await service.DisconnectAsync(userId, organizationId, CancellationToken.None);

        var expiredLink = await scope.Context.Set<AssistantTelegramLink>().SingleAsync(item => item.UserId == userId);
        Assert.True(expiredLink.ExpiresAt <= DateTime.UtcNow);
        Assert.Null(await scope.Context.Set<AssistantTelegramBinding>().SingleOrDefaultAsync(item => item.UserId == userId && item.RevokedAt == null));
        var ownBinding = await scope.Context.Set<AssistantTelegramBinding>().SingleAsync(item => item.UserId == bindingUserId);
        Assert.Null(ownBinding.RevokedAt);
        Assert.Equal(conversation.Id, ownBinding.ConversationId);
        await service.DisconnectAsync(bindingUserId, organizationId, CancellationToken.None);
        Assert.True((await scope.Context.Set<AssistantTelegramLink>().SingleAsync(item => item.UserId == bindingUserId)).ExpiresAt <= DateTime.UtcNow);
        ownBinding = await scope.Context.Set<AssistantTelegramBinding>().SingleAsync(item => item.UserId == bindingUserId);
        Assert.NotNull(ownBinding.RevokedAt);
        Assert.Null(ownBinding.ConversationId);
        Assert.True(await scope.Context.AssistantConversations.AnyAsync(item => item.Id == conversation.Id));
        Assert.NotNull(await scope.Context.Set<AssistantTelegramBinding>().SingleOrDefaultAsync(item => item.UserId == otherUserId && item.RevokedAt == null));

        var bindingToken = new Uri(bindingLink.Url).Query.Split("start=", StringSplitOptions.None)[1];
        using var update = JsonDocument.Parse($$"""
            {
              "update_id": 80001,
              "message": {
                "message_id": 1,
                "from": { "id": 207 },
                "chat": { "id": 207, "type": "private" },
                "text": "/start {{bindingToken}}"
              }
            }
            """);
        Assert.True(await service.EnqueueWebhookAsync(update.RootElement, "IntegrationWebhookSecret_123", CancellationToken.None));
        Assert.True(await service.ProcessNextUpdateAsync(CancellationToken.None));
        Assert.Null(await scope.Context.Set<AssistantTelegramBinding>().SingleOrDefaultAsync(item => item.UserId == bindingUserId && item.RevokedAt == null));
    }

    [Fact]
    public async Task CreateLink_RacingDisconnect_LeavesNoNewPendingLink()
    {
        var organizationId = await fixture.CreateOrganizationAsync("telegram-link-disconnect-race");
        var userId = await fixture.GetOwnerUserIdAsync(organizationId);
        await this.AddMemberAsync(organizationId, userId, OrganizationRoleCodes.Admin);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        await using var disconnectScope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        await using var blockerContext = fixture.CreateContext();
        await using var blockerTransaction = await blockerContext.Database.BeginTransactionAsync();
        await using var blockerCommand = blockerContext.Database.GetDbConnection().CreateCommand();
        blockerCommand.Transaction = blockerTransaction.GetDbTransaction();
        blockerCommand.CommandText = "SELECT \"Id\" FROM identity.\"AspNetUsers\" WHERE \"Id\" = @userId FOR NO KEY UPDATE";
        var userParameter = blockerCommand.CreateParameter();
        userParameter.ParameterName = "userId";
        userParameter.Value = userId;
        blockerCommand.Parameters.Add(userParameter);
        await blockerCommand.ExecuteScalarAsync();
        blockerCommand.CommandText = "SELECT pg_backend_pid()";
        blockerCommand.Parameters.Clear();
        var blockerProcessId = Convert.ToInt32(await blockerCommand.ExecuteScalarAsync());
        var service = this.CreateService(scope);
        var disconnectService = this.CreateService(disconnectScope);
        var createTask = service.CreateLinkAsync(userId, organizationId, CancellationToken.None);
        await using var observerContext = fixture.CreateContext();
        Task? disconnectTask = null;
        var blockerReleased = false;

        try
        {
            await this.WaitForUserLockWaitersAsync(observerContext, blockerProcessId, 1);
            disconnectTask = disconnectService.DisconnectAsync(userId, organizationId, CancellationToken.None);
            await this.WaitForUserLockWaitersAsync(observerContext, blockerProcessId, 2);
            await blockerTransaction.CommitAsync();
            blockerReleased = true;
            await Task.WhenAll(createTask, disconnectTask);

            Assert.Equal(0, await scope.Context.Set<AssistantTelegramLink>()
                .CountAsync(item => item.UserId == userId && item.UsedAt == null && item.ExpiresAt > DateTime.UtcNow));
        }
        finally
        {
            if (!blockerReleased)
            {
                await blockerTransaction.RollbackAsync();
            }

            await createTask;
            if (disconnectTask is not null)
            {
                await disconnectTask;
            }
        }
    }

    private AssistantTelegramService CreateService(AssistantTestScope scope)
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var telegramApi = new Mock<IAssistantTelegramApiClient>();
        telegramApi.Setup(client => client.SendMessageAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new AssistantTelegramService(
            scope.Context,
            scope.Access,
            subscriptions.Object,
            scope.Service,
            telegramApi.Object,
            fixture.Configuration,
            fixture.Options,
            NullLogger<AssistantTelegramService>.Instance);
    }

    private async Task AddMemberAsync(Guid organizationId, string userId, string roleCode)
    {
        await using var context = fixture.CreateContext();
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = userId,
            RoleCode = roleCode
        });
        await context.SaveChangesAsync();
    }

    private async Task WaitForUserLockWaitersAsync(OpenCRMContext context, int blockerProcessId, int expectedCount)
    {
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE blocked_processes(pid) AS (
                SELECT @blockerProcessId
                UNION
                SELECT activity.pid
                FROM pg_stat_activity activity
                JOIN blocked_processes blocker ON blocker.pid = ANY(pg_blocking_pids(activity.pid))
                WHERE activity.datname = current_database()
            )
            SELECT (COUNT(*) - 1)::int FROM blocked_processes
            """;
        var blockerParameter = command.CreateParameter();
        blockerParameter.ParameterName = "blockerProcessId";
        blockerParameter.Value = blockerProcessId;
        command.Parameters.Add(blockerParameter);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var result = await command.ExecuteScalarAsync();
            if (Convert.ToInt32(result) >= expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"За отведённое время не появилось {expectedCount} ожидающих блокировку запроса к AspNetUsers.");
    }

    private static AssistantTelegramBinding Binding(string userId, Guid organizationId, long telegramUserId, DateTime? revokedAt = null) => new()
    {
        UserId = userId,
        TelegramUserId = telegramUserId,
        ChatId = telegramUserId,
        SelectedOrganizationId = organizationId,
        RevokedAt = revokedAt
    };
}
