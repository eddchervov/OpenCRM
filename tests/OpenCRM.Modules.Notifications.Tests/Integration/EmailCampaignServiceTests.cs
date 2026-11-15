using OpenCRM.Services.Interfaces.Admin.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class EmailCampaignServiceTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateCampaignAsync_DeduplicatesRecipientsAndClampsSchedulingValues()
    {
        await using var context = fixture.CreateContext();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        scheduler.Setup(item => item.ScheduleNext(It.IsAny<Guid>(), It.IsAny<TimeSpan>()));
        var service = this.CreateService(context, scheduler);
        var request = new EmailCampaignCreateDto
        {
            Subject = "  Тема  ",
            HtmlBody = "  <p>Текст</p>  ",
            TargetMode = EmailCampaignTargetModeCodes.All,
            IntervalSeconds = 1,
            StartDelayMinutes = -10,
            Recipients =
            [
                new() { Email = " person@example.com ", DisplayName = "  Пользователь  " },
                new() { Email = "PERSON@example.com", DisplayName = "Дубликат" },
                new() { Email = "second@example.com", DisplayName = "Второй получатель" },
                new() { Email = "  " }
            ]
        };

        var result = await service.CreateCampaignAsync(request, "  ");

        var campaign = await context.EmailCampaigns
            .Include(item => item.Recipients)
            .SingleAsync(item => item.Id == result.CampaignId);
        var recipient = Assert.Single(campaign.Recipients, item => item.Email == "person@example.com");
        Assert.Equal(2, result.ScheduledCount);
        Assert.Equal(5, result.IntervalSeconds);
        Assert.Equal("Тема", campaign.Subject);
        Assert.Equal("<p>Текст</p>", campaign.HtmlBody);
        Assert.Null(campaign.CreatedByUserId);
        Assert.Equal(EmailCampaignStatusCodes.Scheduled, campaign.StatusCode);
        Assert.Equal("person@example.com", recipient.Email);
        Assert.Equal("Пользователь", recipient.DisplayName);
        Assert.Equal(EmailCampaignRecipientStatusCodes.Pending, recipient.StatusCode);
        Assert.Equal(result.FirstSendAt.AddSeconds(5), result.LastSendAt);
        Assert.InRange(result.FirstSendAt, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(1));
        scheduler.Verify(item => item.ScheduleNext(campaign.Id, It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task CreateCampaignAsync_ClampsMaximumIntervalAndDelayAndUsesEmailAsDisplayName()
    {
        await using var context = fixture.CreateContext();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        scheduler.Setup(item => item.ScheduleNext(It.IsAny<Guid>(), It.IsAny<TimeSpan>()));
        var service = this.CreateService(context, scheduler);
        var createdByUserId = $"campaign-creator-{Guid.NewGuid():N}";
        context.Users.Add(new OpenCRM.Domain.Identity.ApplicationUser { Id = createdByUserId });
        await context.SaveChangesAsync();

        var result = await service.CreateCampaignAsync(new EmailCampaignCreateDto
        {
            Subject = "Тема",
            HtmlBody = "Текст",
            IntervalSeconds = 100_000,
            StartDelayMinutes = 2_000,
            Recipients = [new() { Email = " recipient@example.com ", DisplayName = " " }]
        }, createdByUserId);

        var campaign = await context.EmailCampaigns
            .Include(item => item.Recipients)
            .SingleAsync(item => item.Id == result.CampaignId);
        var recipient = Assert.Single(campaign.Recipients);
        Assert.Equal(86_400, result.IntervalSeconds);
        Assert.Equal("recipient@example.com", recipient.DisplayName);
        Assert.Equal(createdByUserId, campaign.CreatedByUserId);
        Assert.InRange(result.FirstSendAt, DateTime.UtcNow.AddMinutes(1_439), DateTime.UtcNow.AddMinutes(1_441));
        Assert.Equal(result.FirstSendAt, result.LastSendAt);
        scheduler.Verify(item => item.ScheduleNext(
            campaign.Id,
            It.Is<TimeSpan>(delay => delay > TimeSpan.FromHours(23) && delay <= TimeSpan.FromDays(1))), Times.Once);
    }

    [Fact]
    public async Task CreateCampaignAsync_WithoutUsableRecipients_Throws()
    {
        await using var context = fixture.CreateContext();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        var service = this.CreateService(context, scheduler);
        var initialCampaignCount = await context.EmailCampaigns.CountAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCampaignAsync(new EmailCampaignCreateDto
            {
                Recipients = [new() { Email = " " }, new() { Email = "" }]
            }));

        Assert.Equal("Не найдено получателей с email по выбранным условиям.", exception.Message);
        Assert.Equal(initialCampaignCount, await context.EmailCampaigns.CountAsync());
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessCampaignAsync_MissingCampaign_DoesNothing()
    {
        await using var context = fixture.CreateContext();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        var sender = CreateSender();

        await this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(Guid.NewGuid());

        sender.VerifyNoOtherCalls();
        scheduler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(EmailCampaignStatusCodes.Canceled)]
    [InlineData(EmailCampaignStatusCodes.Paused)]
    [InlineData(EmailCampaignStatusCodes.Completed)]
    public async Task ProcessCampaignAsync_TerminalCampaign_DoesNotQueueOrReschedule(string status)
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(context, status, DateTime.UtcNow.AddMinutes(-1), 1,
            EmailCampaignRecipientStatusCodes.Pending);
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        var sender = CreateSender();

        await this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(campaign.Id);

        sender.VerifyNoOtherCalls();
        scheduler.VerifyNoOtherCalls();
        await using var verification = fixture.CreateContext();
        Assert.Equal(EmailCampaignRecipientStatusCodes.Pending,
            (await verification.EmailCampaignRecipients.SingleAsync(item => item.EmailCampaignId == campaign.Id)).StatusCode);
    }

    [Fact]
    public async Task ProcessCampaignAsync_FutureCampaign_ReschedulesForStartTime()
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Scheduled, DateTime.UtcNow.AddMinutes(30), 1,
            EmailCampaignRecipientStatusCodes.Pending);
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        scheduler.Setup(item => item.ScheduleNext(It.IsAny<Guid>(), It.IsAny<TimeSpan>()));
        var sender = CreateSender();

        await this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(campaign.Id);

        sender.VerifyNoOtherCalls();
        scheduler.Verify(item => item.ScheduleNext(
            campaign.Id,
            It.Is<TimeSpan>(delay => delay > TimeSpan.FromMinutes(29) && delay <= TimeSpan.FromMinutes(30))), Times.Once);
    }

    [Theory]
    [InlineData(1, 1, 0, 0, true)]
    [InlineData(2, 0, 2, 0, true)]
    [InlineData(2, 0, 0, 2, true)]
    [InlineData(1, 0, 0, 0, false)]
    public async Task ProcessCampaignAsync_NoPendingRecipientsCompletesOnlyWhenCountersReachTotal(
        int totalCount,
        int sentCount,
        int simulatedCount,
        int skippedCount,
        bool shouldComplete)
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context,
            EmailCampaignStatusCodes.Running,
            DateTime.UtcNow.AddMinutes(-1),
            totalCount);
        campaign.SentCount = sentCount;
        campaign.SimulatedCount = simulatedCount;
        campaign.SkippedCount = skippedCount;
        await context.SaveChangesAsync();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        scheduler.Setup(item => item.ScheduleNext(It.IsAny<Guid>(), It.IsAny<TimeSpan>()));
        var sender = CreateSender();

        await this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(campaign.Id);

        await using var verification = fixture.CreateContext();
        var savedCampaign = await verification.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        Assert.Equal(shouldComplete, savedCampaign.StatusCode == EmailCampaignStatusCodes.Completed);
        Assert.Equal(shouldComplete, savedCampaign.CompletedAt.HasValue);
        sender.VerifyNoOtherCalls();
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessCampaignAsync_QueuesRecipientAndMarksScheduledCampaignRunning()
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Scheduled, DateTime.UtcNow.AddMinutes(-1), 1,
            EmailCampaignRecipientStatusCodes.Pending);
        var recipientId = campaign.Recipients.Single().Id;
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        var sender = CreateSender();
        sender.Setup(item => item.QueueAsync(
            "recipient-0@example.com", campaign.Subject, campaign.HtmlBody,
            $"campaign:{campaign.Id:N}:{recipientId:N}", recipientId, null))
            .Returns(Task.CompletedTask);

        await this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(campaign.Id);

        await using var verification = fixture.CreateContext();
        var savedCampaign = await verification.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        var savedRecipient = await verification.EmailCampaignRecipients.SingleAsync(item => item.Id == recipientId);
        Assert.Equal(EmailCampaignStatusCodes.Running, savedCampaign.StatusCode);
        Assert.NotNull(savedCampaign.StartedAt);
        Assert.Equal(EmailCampaignRecipientStatusCodes.Queued, savedRecipient.StatusCode);
        Assert.Equal(1, savedRecipient.AttemptCount);
        Assert.NotNull(savedRecipient.LastAttemptAt);
        sender.VerifyAll();
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessCampaignAsync_QueuesNextPendingRecipientAfterConfiguredInterval()
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Running, DateTime.UtcNow.AddMinutes(-1), 2,
            EmailCampaignRecipientStatusCodes.Pending, EmailCampaignRecipientStatusCodes.Pending);
        campaign.IntervalSeconds = 17;
        await context.SaveChangesAsync();
        var recipient = campaign.Recipients.OrderBy(item => item.CreatedAt).First();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        scheduler.Setup(item => item.ScheduleNext(It.IsAny<Guid>(), It.IsAny<TimeSpan>()));
        var sender = CreateSender();
        sender.Setup(item => item.QueueAsync(
            recipient.Email, campaign.Subject, campaign.HtmlBody,
            $"campaign:{campaign.Id:N}:{recipient.Id:N}", recipient.Id, null))
            .Returns(Task.CompletedTask);

        await this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(campaign.Id);

        sender.VerifyAll();
        scheduler.Verify(item => item.ScheduleNext(campaign.Id, TimeSpan.FromSeconds(17)), Times.Once);
    }

    [Fact]
    public async Task ProcessCampaignAsync_ArgumentExceptionMarksRecipientAndCompletesCampaign()
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Scheduled, DateTime.UtcNow.AddMinutes(-1), 1,
            EmailCampaignRecipientStatusCodes.Pending);
        var recipientId = campaign.Recipients.Single().Id;
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        var sender = CreateSender();
        sender.Setup(item => item.QueueAsync(
            "recipient-0@example.com", campaign.Subject, campaign.HtmlBody,
            $"campaign:{campaign.Id:N}:{recipientId:N}", recipientId, null))
            .ThrowsAsync(new ArgumentException("Некорректный email."));

        await this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(campaign.Id);

        await using var verification = fixture.CreateContext();
        var savedCampaign = await verification.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        var savedRecipient = await verification.EmailCampaignRecipients.SingleAsync(item => item.Id == recipientId);
        Assert.Equal(EmailCampaignStatusCodes.Completed, savedCampaign.StatusCode);
        Assert.Equal(1, savedCampaign.FailedCount);
        Assert.Equal("Некорректный email.", savedCampaign.LastError);
        Assert.NotNull(savedCampaign.CompletedAt);
        Assert.Equal(EmailCampaignRecipientStatusCodes.Failed, savedRecipient.StatusCode);
        Assert.Equal("Некорректный email.", savedRecipient.ErrorMessage);
        Assert.Equal(1, savedRecipient.AttemptCount);
        sender.VerifyAll();
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessCampaignAsync_UnexpectedSenderFailureRollsBackAndPropagates()
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Scheduled, DateTime.UtcNow.AddMinutes(-1), 1,
            EmailCampaignRecipientStatusCodes.Pending);
        var recipientId = campaign.Recipients.Single().Id;
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        var sender = CreateSender();
        sender.Setup(item => item.QueueAsync(
                "recipient-0@example.com", campaign.Subject, campaign.HtmlBody,
                $"campaign:{campaign.Id:N}:{recipientId:N}", recipientId, null))
            .ThrowsAsync(new InvalidOperationException("Временная ошибка отправки."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.CreateService(context, scheduler, sender.Object).ProcessCampaignAsync(campaign.Id));

        Assert.Equal("Временная ошибка отправки.", exception.Message);
        await using var verification = fixture.CreateContext();
        var savedCampaign = await verification.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        var savedRecipient = await verification.EmailCampaignRecipients.SingleAsync(item => item.Id == recipientId);
        Assert.Equal(EmailCampaignStatusCodes.Scheduled, savedCampaign.StatusCode);
        Assert.Equal(EmailCampaignRecipientStatusCodes.Pending, savedRecipient.StatusCode);
        Assert.Equal(0, savedRecipient.AttemptCount);
        sender.VerifyAll();
        scheduler.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(EmailCampaignStatusCodes.Paused)]
    [InlineData(EmailCampaignStatusCodes.Canceled)]
    public async Task SetStatusAsync_PausesOrCancelsCampaign(string status)
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Scheduled, DateTime.UtcNow, 0);
        var originalUpdatedAt = await context.EmailCampaigns
            .Where(item => item.Id == campaign.Id)
            .Select(item => item.UpdatedAt)
            .SingleAsync();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);

        await this.CreateService(context, scheduler).SetStatusAsync(campaign.Id, status);

        await using var verification = fixture.CreateContext();
        var savedCampaign = await verification.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        Assert.Equal(status, savedCampaign.StatusCode);
        Assert.True(savedCampaign.UpdatedAt >= originalUpdatedAt);
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetStatusAsync_RunningSchedulesImmediateProcessing()
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Paused, DateTime.UtcNow, 0);
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);
        scheduler.Setup(item => item.ScheduleNext(It.IsAny<Guid>(), It.IsAny<TimeSpan>()));

        await this.CreateService(context, scheduler).SetStatusAsync(campaign.Id, EmailCampaignStatusCodes.Running);

        scheduler.Verify(item => item.ScheduleNext(campaign.Id, TimeSpan.Zero), Times.Once);
    }

    [Fact]
    public async Task SetStatusAsync_CompletedCampaignRemainsUnchanged()
    {
        await using var context = fixture.CreateContext();
        var campaign = await this.AddCampaignAsync(
            context, EmailCampaignStatusCodes.Completed, DateTime.UtcNow, 0);
        var originalUpdatedAt = await context.EmailCampaigns
            .Where(item => item.Id == campaign.Id)
            .Select(item => item.UpdatedAt)
            .SingleAsync();
        var scheduler = new Mock<IEmailCampaignScheduler>(MockBehavior.Strict);

        await this.CreateService(context, scheduler).SetStatusAsync(campaign.Id, EmailCampaignStatusCodes.Canceled);

        await using var verification = fixture.CreateContext();
        var savedCampaign = await verification.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        Assert.Equal(EmailCampaignStatusCodes.Completed, savedCampaign.StatusCode);
        Assert.Equal(originalUpdatedAt, savedCampaign.UpdatedAt);
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SetStatusAsync_InvalidStatusThrowsBeforeDatabaseLookup()
    {
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new Mock<IEmailCampaignScheduler>(MockBehavior.Strict));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetStatusAsync(Guid.NewGuid(), "unknown"));

        Assert.Equal("Недопустимый статус рассылки.", exception.Message);
    }

    [Fact]
    public async Task SetStatusAsync_UnknownCampaignThrows()
    {
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new Mock<IEmailCampaignScheduler>(MockBehavior.Strict));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetStatusAsync(Guid.NewGuid(), EmailCampaignStatusCodes.Paused));

        Assert.Equal("Рассылка не найдена.", exception.Message);
    }

    [Fact]
    public async Task GetCampaignsAsync_ClampsPageValuesAndReturnsNewestFirst()
    {
        await using var context = fixture.CreateContext();
        var initialCount = await context.EmailCampaigns.CountAsync();
        var createdAt = DateTime.UtcNow.AddDays(1);
        var campaigns = Enumerable.Range(0, 6)
            .Select(index => new EmailCampaign
            {
                Subject = $"page-{index}",
                CreatedAt = createdAt.AddSeconds(index),
                UpdatedAt = createdAt.AddSeconds(index)
            })
            .ToList();
        context.EmailCampaigns.AddRange(campaigns);
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new Mock<IEmailCampaignScheduler>(MockBehavior.Strict));

        var firstPage = await service.GetCampaignsAsync(page: 0, pageSize: 1);
        var secondPage = await service.GetCampaignsAsync(page: 2, pageSize: 5);
        var largestPage = await service.GetCampaignsAsync(pageSize: 200);

        Assert.Equal(initialCount + campaigns.Count, firstPage.TotalCount);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(5, firstPage.PageSize);
        Assert.Equal(campaigns[^1].Id, firstPage.Items[0].Id);
        Assert.Equal(campaigns[0].Id, secondPage.Items[0].Id);
        Assert.Equal(50, largestPage.PageSize);
    }

    private EmailCampaignService CreateService(
        OpenCRMContext context,
        Mock<IEmailCampaignScheduler> scheduler,
        IEmailNotificationService? emailSender = null)
    {
        return new EmailCampaignService(
            context,
            emailSender ?? CreateSender().Object,
            scheduler.Object,
            NullLogger<EmailCampaignService>.Instance);
    }

    private async Task<EmailCampaign> AddCampaignAsync(
        OpenCRMContext context,
        string status,
        DateTime startAt,
        int totalCount,
        params string?[] recipientStatuses)
    {
        var campaign = new EmailCampaign
        {
            Subject = "Тема кампании",
            HtmlBody = "<p>Текст</p>",
            StatusCode = status,
            StartAt = startAt,
            IntervalSeconds = 7,
            TotalCount = totalCount,
            Recipients = recipientStatuses
                .Select((recipientStatus, index) => new EmailCampaignRecipient
                {
                    Email = $"recipient-{index}@example.com",
                    DisplayName = $"Получатель {index}",
                    StatusCode = recipientStatus ?? EmailCampaignRecipientStatusCodes.Pending,
                    CreatedAt = DateTime.UtcNow.AddSeconds(index)
                })
                .ToList()
        };
        context.EmailCampaigns.Add(campaign);
        await context.SaveChangesAsync();

        return campaign;
    }

    private static Mock<IEmailNotificationService> CreateSender()
    {
        return new Mock<IEmailNotificationService>(MockBehavior.Strict);
    }
}
