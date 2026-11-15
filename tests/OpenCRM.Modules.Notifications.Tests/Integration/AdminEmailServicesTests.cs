using System.Text.Json;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class AdminEmailServicesTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetComposeDataAsync_ReturnsActiveTypesAndFilteredUsersInNewestOrder()
    {
        await using var context = fixture.CreateContext();
        var activeCode = OrganizationTypeCodes.Detailing;
        var inactiveCode = OrganizationTypeCodes.Fleet;
        var activeType = await context.OrganizationTypeDefinitions.SingleAsync(x => x.Code == activeCode);
        var inactiveType = await context.OrganizationTypeDefinitions.SingleAsync(x => x.Code == inactiveCode);
        var originalActiveState = activeType.IsActive;
        var originalInactiveState = inactiveType.IsActive;

        try
        {
            activeType.IsActive = true;
            inactiveType.IsActive = false;
            var matchingUser = this.CreateUser("matching", email: $"compose-{Guid.NewGuid():N}@example.com");
            matchingUser.UserName ="Тестовый получатель";
            matchingUser.PhoneNumber = "+70000001234";
            matchingUser.DatetimeCreation = DateTime.UtcNow;
            var olderUser = this.CreateUser("older", email: $"compose-old-{Guid.NewGuid():N}@example.com");
            olderUser.DatetimeCreation = DateTime.UtcNow.AddDays(-1);
            var deletedUser = this.CreateUser("deleted", email: $"compose-deleted-{Guid.NewGuid():N}@example.com");
            deletedUser.IsDeleted = true;
            context.Users.AddRange(matchingUser, olderUser, deletedUser);
            context.Users.Add(new ApplicationUser { UserName = $"no-email-{Guid.NewGuid():N}" });
            await context.SaveChangesAsync();
            var service = this.CreateCompositionService(context);

            var result = await service.GetComposeDataAsync("Тестовый получатель");

            Assert.Contains(result.OrganizationTypes, item => item.Code == activeCode);
            Assert.DoesNotContain(result.OrganizationTypes, item => item.Code == inactiveCode);
            var user = Assert.Single(result.Users);
            Assert.Equal(matchingUser.Id, user.Id);
            Assert.Equal(matchingUser.Email, user.Email);
            Assert.Equal(matchingUser.PhoneNumber, user.Phone);
            Assert.Equal(matchingUser.Email, user.DisplayName);
        }
        finally
        {
            activeType.IsActive = originalActiveState;
            inactiveType.IsActive = originalInactiveState;
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task GetComposeDataAsync_LimitsUsersToThirtyNewest()
    {
        await using var context = fixture.CreateContext();
        var users = Enumerable.Range(0, 31)
            .Select(index => this.CreateUser(
                $"compose-page-{index}",
                email: $"compose-page-{Guid.NewGuid():N}@example.com",
                createdAt: DateTime.UtcNow.AddSeconds(index)))
            .ToList();
        context.Users.AddRange(users);
        await context.SaveChangesAsync();
        var service = this.CreateCompositionService(context);

        var result = await service.GetComposeDataAsync(null);

        Assert.Equal(30, result.Users.Count);
        Assert.DoesNotContain(result.Users, item => item.Id == users[0].Id);
        Assert.Contains(result.Users, item => item.Id == users[^1].Id);
    }

    [Fact]
    public async Task PreviewRecipientsAsync_AllModeAppliesFiltersAndCapsPreviewItems()
    {
        await using var context = fixture.CreateContext();
        var initiallyEligibleCount = await context.Users.CountAsync(user =>
            !user.IsDeleted && user.Email != null && user.Email != "" &&
            user.EmailConfirmed && user.AdvertisingConsentAccepted);
        var eligibleUsers = Enumerable.Range(0, 25)
            .Select(index => this.CreateUser(
                $"preview-all-{index}",
                email: $"preview-{Guid.NewGuid():N}@example.com",
                emailConfirmed: true,
                advertisingConsentAccepted: true))
            .ToList();
        var unconfirmedUser = this.CreateUser(
            "preview-unconfirmed", email: $"preview-unconfirmed-{Guid.NewGuid():N}@example.com");
        var declinedUser = this.CreateUser(
            "preview-declined", email: $"preview-declined-{Guid.NewGuid():N}@example.com", emailConfirmed: true);
        context.Users.AddRange(eligibleUsers);
        context.Users.AddRange(unconfirmedUser, declinedUser);
        await context.SaveChangesAsync();
        var service = this.CreateCompositionService(context);

        var result = await service.PreviewRecipientsAsync(new EmailCampaignRecipientsRequest
        {
            TargetMode = EmailCampaignTargetModeCodes.All,
            OnlyConfirmedEmail = true,
            OnlyAdvertisingConsent = true
        });

        Assert.True(result.Succeeded);
        Assert.Equal(initiallyEligibleCount + 25, result.Value!.TotalCount);
        Assert.Equal(20, result.Value.Items.Count);
        Assert.All(result.Value.Items, item =>
        {
            Assert.True(item.EmailConfirmed);
            Assert.True(item.AdvertisingConsentAccepted);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(EmailCampaignTargetModeCodes.User)]
    [InlineData(EmailCampaignTargetModeCodes.OrganizationType)]
    [InlineData("unknown")]
    public async Task PreviewRecipientsAsync_MissingTargetOrUnsupportedModeReturnsEmpty(string? targetMode)
    {
        await using var context = fixture.CreateContext();
        var service = this.CreateCompositionService(context);

        var result = await service.PreviewRecipientsAsync(new EmailCampaignRecipientsRequest
        {
            TargetMode = targetMode!
        });

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.TotalCount);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task PreviewRecipientsAsync_ExternalEmailUsesNormalizedRegisteredUserWhenPresent()
    {
        await using var context = fixture.CreateContext();
        var user = this.CreateUser("external", email: $"External-{Guid.NewGuid():N}@example.com");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = this.CreateCompositionService(context);

        var result = await service.PreviewRecipientsAsync(new EmailCampaignRecipientsRequest
        {
            TargetMode = " EMAIL ",
            Email = $"  {user.Email!.ToUpperInvariant()}  ",
            OnlyConfirmedEmail = false
        });

        var recipient = Assert.Single(result.Value!.Items);
        Assert.Equal(user.Id, recipient.UserId);
        Assert.Equal(user.Email!.ToUpperInvariant(), recipient.Email);
    }

    [Theory]
    [InlineData("not-an-email", false, false)]
    [InlineData("person@example.com", true, false)]
    [InlineData("person@example.com", false, true)]
    public async Task PreviewRecipientsAsync_RejectsInvalidExternalRecipientOptions(
        string email,
        bool onlyConfirmedEmail,
        bool onlyAdvertisingConsent)
    {
        await using var context = fixture.CreateContext();
        var service = this.CreateCompositionService(context);

        var result = await service.PreviewRecipientsAsync(new EmailCampaignRecipientsRequest
        {
            TargetMode = EmailCampaignTargetModeCodes.Email,
            Email = email,
            OnlyConfirmedEmail = onlyConfirmedEmail,
            OnlyAdvertisingConsent = onlyAdvertisingConsent
        });

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task PreviewRecipientsAsync_OrganizationTypeIncludesOwnersAndMembersOnce()
    {
        await using var context = fixture.CreateContext();
        var organizationType = $"compose-sto-{Guid.NewGuid():N}";
        var owner = this.CreateUser("org-owner", email: $"owner-{Guid.NewGuid():N}@example.com");
        var member = this.CreateUser("org-member", email: $"member-{Guid.NewGuid():N}@example.com");
        context.Users.AddRange(owner, member);
        var organization = new Organization
        {
            Name = "Организация уведомлений",
            OwnerUserId = owner.Id,
            PrimaryScenarioCode = organizationType
        };
        context.Organizations.Add(organization);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = owner.Id
        });
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = member.Id
        });
        await context.SaveChangesAsync();
        var service = this.CreateCompositionService(context);

        var result = await service.PreviewRecipientsAsync(new EmailCampaignRecipientsRequest
        {
            TargetMode = EmailCampaignTargetModeCodes.OrganizationType,
            OrganizationTypeCode = $" {organizationType} ",
            OnlyConfirmedEmail = false
        });

        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Equal(
            new[] { owner.Id, member.Id }.OrderBy(id => id),
            result.Value.Items.Select(item => item.UserId).OrderBy(id => id));
    }

    [Fact]
    public async Task ScheduleCampaignAsync_ValidRequestCreatesCampaignAndMapsResult()
    {
        await using var context = fixture.CreateContext();
        var user = this.CreateUser(
            "schedule",
            email: $"schedule-{Guid.NewGuid():N}@example.com",
            emailConfirmed: true,
            advertisingConsentAccepted: true);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var campaignId = Guid.NewGuid();
        var firstSendAt = DateTime.UtcNow;
        var lastSendAt = firstSendAt.AddSeconds(20);
        var campaigns = new Mock<IEmailCampaignService>(MockBehavior.Strict);
        campaigns.Setup(item => item.CreateCampaignAsync(It.Is<EmailCampaignCreateDto>(request =>
                request.Subject == "Тема" && request.HtmlBody == "<p>Текст</p>" &&
                request.TargetMode == EmailCampaignTargetModeCodes.User && request.TargetUserId == user.Id &&
                request.OnlyConfirmedEmail && request.OnlyAdvertisingConsent && request.IntervalSeconds == 20 &&
                request.StartDelayMinutes == 5 && request.Recipients.Count == 1 &&
                request.Recipients[0].Email == user.Email), "admin-user"))
            .ReturnsAsync(new EmailCampaignCreateResultDto
            {
                CampaignId = campaignId,
                ScheduledCount = 1,
                IntervalSeconds = 20,
                FirstSendAt = firstSendAt,
                LastSendAt = lastSendAt
            });
        var service = new AdminEmailCompositionService(context, campaigns.Object);

        var result = await service.ScheduleCampaignAsync(new EmailCampaignScheduleRequest
        {
            TargetMode = EmailCampaignTargetModeCodes.User,
            UserId = user.Id,
            OnlyConfirmedEmail = true,
            OnlyAdvertisingConsent = true,
            Subject = "Тема",
            HtmlBody = "<p>Текст</p>",
            IntervalSeconds = 20,
            StartDelayMinutes = 5
        }, "admin-user");

        Assert.True(result.Succeeded);
        Assert.Equal(campaignId, result.Value!.CampaignId);
        Assert.Equal(1, result.Value.ScheduledCount);
        Assert.Equal(20, result.Value.IntervalSeconds);
        Assert.Equal(firstSendAt, result.Value.FirstSendAt);
        Assert.Equal(lastSendAt, result.Value.LastSendAt);
        campaigns.VerifyAll();
    }

    [Theory]
    [InlineData("", "body", 10)]
    [InlineData("long-subject-placeholder", "body", 10)]
    [InlineData("subject", " ", 10)]
    [InlineData("subject", "body", 4)]
    public async Task ScheduleCampaignAsync_InvalidCampaignFieldsReturnFailure(
        string subject,
        string htmlBody,
        int intervalSeconds)
    {
        await using var context = fixture.CreateContext();
        if (subject == "long-subject-placeholder")
        {
            subject = new string('x', 301);
        }

        var campaigns = new Mock<IEmailCampaignService>(MockBehavior.Strict);
        var service = new AdminEmailCompositionService(context, campaigns.Object);

        var result = await service.ScheduleCampaignAsync(new EmailCampaignScheduleRequest
        {
            TargetMode = EmailCampaignTargetModeCodes.All,
            OnlyConfirmedEmail = false,
            Subject = subject,
            HtmlBody = htmlBody,
            IntervalSeconds = intervalSeconds
        }, null);

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        campaigns.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ScheduleCampaignAsync_EmptyRecipientQueryReturnsFailure()
    {
        await using var context = fixture.CreateContext();
        var campaigns = new Mock<IEmailCampaignService>(MockBehavior.Strict);
        var service = new AdminEmailCompositionService(context, campaigns.Object);

        var result = await service.ScheduleCampaignAsync(new EmailCampaignScheduleRequest
        {
            TargetMode = EmailCampaignTargetModeCodes.User,
            Subject = "Тема",
            HtmlBody = "Текст",
            IntervalSeconds = 10
        }, null);

        Assert.False(result.Succeeded);
        Assert.Equal("Не найдено получателей с email по выбранным условиям.", result.Error);
        campaigns.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendTestEmailAsync_EmptyAddressReturnsFailureWithoutSending()
    {
        await using var context = fixture.CreateContext();
        var sender = new Mock<IEmailSender>(MockBehavior.Strict);
        var service = new AdminEmailTestOperationsService(context, sender.Object);

        var result = await service.SendTestEmailAsync(new EmailTestSendRequest { Email = " " });

        Assert.False(result.Succeeded);
        Assert.Equal("Укажите email для тестовой отправки.", result.Error);
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendTestEmailAsync_UsesDefaultsAndTrimsRecipient()
    {
        await using var context = fixture.CreateContext();
        var sender = new Mock<IEmailSender>(MockBehavior.Strict);
        sender.Setup(item => item.SendEmailAsync(
                "person@example.com",
                "Тестовое письмо " + Helpers.ProjectBrand.DefaultName,
                It.Is<string>(body => body.Contains("из админки") && body.Contains("журнале email"))))
            .Returns(Task.CompletedTask);
        var service = new AdminEmailTestOperationsService(context, sender.Object);

        var result = await service.SendTestEmailAsync(new EmailTestSendRequest { Email = " person@example.com " });

        Assert.True(result.Succeeded);
        Assert.Contains("поставлено в очередь", result.Value);
        sender.VerifyAll();
    }

    [Fact]
    public async Task SendTestEmailAsync_TrimsCustomValuesAndReturnsSendError()
    {
        await using var context = fixture.CreateContext();
        var sender = new Mock<IEmailSender>(MockBehavior.Strict);
        sender.Setup(item => item.SendEmailAsync("person@example.com", "Тема", "Текст"))
            .ThrowsAsync(new InvalidOperationException("SMTP недоступен."));
        var service = new AdminEmailTestOperationsService(context, sender.Object);

        var result = await service.SendTestEmailAsync(new EmailTestSendRequest
        {
            Email = " person@example.com ",
            Subject = " Тема ",
            HtmlBody = " Текст "
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Не удалось отправить тестовое письмо: SMTP недоступен.", result.Error);
        sender.VerifyAll();
    }

    [Fact]
    public async Task CreateWebhookTestAsync_WithoutMessagesReturnsFailure()
    {
        await using var context = fixture.CreateContext();
        var service = new AdminEmailTestOperationsService(context, Mock.Of<IEmailSender>());

        var result = await service.CreateWebhookTestAsync(new EmailWebhookTestRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("Нет писем, к которым можно привязать тестовый вебхук.", result.Error);
    }

    [Theory]
    [InlineData(EmailMessageStatusCodes.Opened, 1, 0)]
    [InlineData(EmailMessageStatusCodes.Clicked, 0, 1)]
    [InlineData(EmailMessageStatusCodes.Delivered, 0, 0)]
    [InlineData("invalid", 0, 0)]
    [InlineData(" ", 0, 0)]
    [InlineData(null, 0, 0)]
    public async Task CreateWebhookTestAsync_NormalizesStatusAndUpdatesMessage(
        string? requestedStatus,
        int expectedOpenCount,
        int expectedClickCount)
    {
        await using var context = fixture.CreateContext();
        var message = new EmailMessage
        {
            ToEmail = $"webhook-{Guid.NewGuid():N}@example.com",
            Subject = "Письмо",
            StatusCode = EmailMessageStatusCodes.Queued,
            HtmlBody = "<p>HTML</p>",
            TextBody = "Текст",
            CreatedAt = DateTime.UtcNow
        };
        context.EmailMessages.Add(message);
        await context.SaveChangesAsync();
        var service = new AdminEmailTestOperationsService(context, Mock.Of<IEmailSender>());

        var result = await service.CreateWebhookTestAsync(new EmailWebhookTestRequest
        {
            EmailMessageId = message.Id,
            Status = requestedStatus
        });

        Assert.True(result.Succeeded);
        var expectedStatus = requestedStatus is EmailMessageStatusCodes.Opened
            or EmailMessageStatusCodes.Clicked
            or EmailMessageStatusCodes.Delivered
            ? requestedStatus
            : EmailMessageStatusCodes.Delivered;
        Assert.Equal(expectedStatus, result.Value!.Status);
        Assert.Equal(message.ToEmail, result.Value.Email);
        Assert.Equal("manual-test", result.Value.Provider);
        Assert.False(result.Value.SignatureVerified);
        using var payload = JsonDocument.Parse(result.Value.RawPayload);
        Assert.Equal("admin_manual_test", payload.RootElement.GetProperty("source").GetString());

        await using var verification = fixture.CreateContext();
        var updated = await verification.EmailMessages.SingleAsync(item => item.Id == message.Id);
        var messageEvent = await verification.EmailMessageEvents.SingleAsync(item => item.Id == result.Value.Id);
        Assert.Equal(expectedStatus, updated.StatusCode);
        Assert.Equal(expectedOpenCount, updated.OpenCount);
        Assert.Equal(expectedClickCount, updated.ClickCount);
        Assert.Equal("manual-" + message.Id, updated.ProviderMessageId);
        Assert.Equal(result.Value.Id, messageEvent.Id);
        Assert.False(messageEvent.SignatureVerified);
    }

    [Fact]
    public async Task CreateWebhookTestAsync_MissingRequestedMessageFallsBackToNewestAndKeepsProviderId()
    {
        await using var context = fixture.CreateContext();
        var olderMessage = new EmailMessage
        {
            ToEmail = $"older-{Guid.NewGuid():N}@example.com",
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        };
        var newestMessage = new EmailMessage
        {
            ToEmail = $"newest-{Guid.NewGuid():N}@example.com",
            ProviderMessageId = "provider-message-42",
            CreatedAt = DateTime.UtcNow.AddYears(10)
        };
        context.EmailMessages.AddRange(olderMessage, newestMessage);
        await context.SaveChangesAsync();
        var service = new AdminEmailTestOperationsService(context, Mock.Of<IEmailSender>());

        var result = await service.CreateWebhookTestAsync(new EmailWebhookTestRequest
        {
            EmailMessageId = int.MaxValue,
            Status = EmailMessageStatusCodes.Delivered
        });

        Assert.True(result.Succeeded);
        Assert.Equal(newestMessage.Id, result.Value!.EmailMessageId);
        Assert.Equal("provider-message-42", result.Value.ProviderMessageId);
        await using var verification = fixture.CreateContext();
        var saved = await verification.EmailMessages.SingleAsync(item => item.Id == newestMessage.Id);
        Assert.Equal("provider-message-42", saved.ProviderMessageId);
    }

    [Fact]
    public async Task CreateWebhookTestAsync_WithoutRequestedIdUsesNewestMessage()
    {
        await using var context = fixture.CreateContext();
        var message = new EmailMessage
        {
            ToEmail = $"fallback-{Guid.NewGuid():N}@example.com",
            CreatedAt = DateTime.UtcNow.AddYears(20)
        };
        context.EmailMessages.Add(message);
        await context.SaveChangesAsync();
        var service = new AdminEmailTestOperationsService(context, Mock.Of<IEmailSender>());

        var result = await service.CreateWebhookTestAsync(new EmailWebhookTestRequest());

        Assert.True(result.Succeeded);
        Assert.Equal(message.Id, result.Value!.EmailMessageId);
    }

    [Fact]
    public async Task AdminEmailJournal_ReturnsFilteredPagedMessagesAndDetails()
    {
        await using var context = fixture.CreateContext();
        var user = this.CreateUser("journal", email: $"journal-{Guid.NewGuid():N}@example.com");
        context.Users.Add(user);
        var prefix = $"journal-{Guid.NewGuid():N}";
        var messageStatus = $"status-{Guid.NewGuid():N}";
        var messages = Enumerable.Range(0, 12)
            .Select(index => new EmailMessage
            {
                UserId = user.Id,
                ToEmail = $"{prefix}-{index}@example.com",
                Subject = $"subject-{prefix}-{index}",
                StatusCode = messageStatus,
                ProviderMessageId = $"provider-{prefix}-{index}",
                HtmlBody = $"<p>{index}</p>",
                TextBody = index.ToString(),
                CreatedAt = DateTime.UtcNow.AddSeconds(index)
            })
            .ToList();
        context.EmailMessages.AddRange(messages);
        await context.SaveChangesAsync();
        var eventMessage = messages[^1];
        context.EmailMessageEvents.Add(new EmailMessageEvent
        {
            EmailMessageId = eventMessage.Id,
            EventName = "opened",
            Status = "opened",
            ReceivedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var page = await journal.GetPageAsync(
            search: $"  {prefix.ToUpperInvariant()}  ", status: messageStatus, page: 0, pageSize: 1);
        var largestPage = await journal.GetPageAsync(
            search: prefix, status: messageStatus, pageSize: 1_000);
        var details = await journal.GetByIdAsync(eventMessage.Id, CancellationToken.None);
        var missingDetails = await journal.GetByIdAsync(int.MaxValue, CancellationToken.None);

        Assert.Equal(12, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(100, largestPage.PageSize);
        Assert.Equal(eventMessage.Id, page.Items[0].Id);
        Assert.Equal(user.Email, page.Items[0].UserDisplayName);
        Assert.Equal(1, page.Items[0].EventCount);
        Assert.Equal(eventMessage.Id, details!.Id);
        Assert.Equal(user.Email, details.UserDisplayName);
        Assert.Equal(eventMessage.HtmlBody, details.HtmlBody);
        Assert.Equal(eventMessage.TextBody, details.TextBody);
        Assert.Equal(1, details.EventCount);
        Assert.Null(missingDetails);
    }

    [Theory]
    [InlineData("NoUser")]
    [InlineData("Email")]
    [InlineData("UserName")]
    [InlineData("PhoneNumber")]
    [InlineData("UserId")]
    public async Task AdminEmailJournal_GetByIdUsesAvailableUserDisplayName(string displayNameSource)
    {
        await using var context = fixture.CreateContext();
        var userId = $"journal-display-{Guid.NewGuid():N}";
        ApplicationUser? user = displayNameSource == "NoUser"
            ? null
            : new ApplicationUser { Id = userId };
        var expectedDisplayName = displayNameSource switch
        {
            "Email" => $"{userId}@example.com",
            "UserName" => $"name-{userId}",
            "PhoneNumber" => "+70000000000",
            "UserId" => userId,
            _ => null
        };

        if (user != null)
        {
            switch (displayNameSource)
            {
                case "Email":
                    user.Email = expectedDisplayName;
                    break;
                case "UserName":
                    user.UserName = expectedDisplayName;
                    break;
                case "PhoneNumber":
                    user.PhoneNumber = expectedDisplayName;
                    break;
            }
        }

        var message = new EmailMessage
        {
            User = user,
            ToEmail = $"recipient-{Guid.NewGuid():N}@example.com",
            Subject = "Журнал email"
        };
        context.EmailMessages.Add(message);
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var result = await journal.GetByIdAsync(message.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expectedDisplayName, result.UserDisplayName);
    }

    [Fact]
    public async Task AdminEmailJournal_ReturnsEventsInReverseOrderAndEmptyForUnknownMessage()
    {
        await using var context = fixture.CreateContext();
        var message = new EmailMessage
        {
            ToEmail = $"events-{Guid.NewGuid():N}@example.com",
            CreatedAt = DateTime.UtcNow
        };
        context.EmailMessages.Add(message);
        await context.SaveChangesAsync();
        var earlier = DateTime.UtcNow.AddMinutes(-2);
        var later = DateTime.UtcNow.AddMinutes(-1);
        context.EmailMessageEvents.AddRange(
            new EmailMessageEvent
            {
                EmailMessageId = message.Id,
                EventName = "delivered",
                EventAt = later,
                ReceivedAt = earlier,
                RawPayload = "earlier"
            },
            new EmailMessageEvent
            {
                EmailMessageId = message.Id,
                EventName = "opened",
                EventAt = later,
                ReceivedAt = later,
                RawPayload = "later"
            });
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var events = await journal.GetEventsAsync(message.Id, CancellationToken.None);
        var emptyEvents = await journal.GetEventsAsync(int.MaxValue, CancellationToken.None);

        Assert.Equal(new[] { "opened", "delivered" }, events.Items.Select(item => item.EventName));
        Assert.Equal("later", events.Items[0].RawPayload);
        Assert.Empty(emptyEvents.Items);
    }

    [Fact]
    public async Task AdminEmailJournal_FiltersWebhookEventsAndClampsPagination()
    {
        await using var context = fixture.CreateContext();
        var marker = $"webhook-{Guid.NewGuid():N}";
        var events = Enumerable.Range(0, 12)
            .Select(index => new EmailMessageEvent
            {
                Provider = "provider-test",
                EventName = $"event-{marker}-{index}",
                Status = $"status-{marker}",
                Email = $"{marker}-{index}@example.com",
                Url = $"https://example.test/{marker}/{index}",
                RawPayload = $"payload-{index}",
                ReceivedAt = DateTime.UtcNow.AddSeconds(index)
            })
            .ToList();
        context.EmailMessageEvents.AddRange(events);
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var page = await journal.GetWebhookEventsAsync(
            search: marker.ToUpperInvariant(), status: $"status-{marker}", page: 0, pageSize: 1);
        var largestPage = await journal.GetWebhookEventsAsync(
            search: marker, status: $"status-{marker}", pageSize: 1_000);
        var secondPage = await journal.GetWebhookEventsAsync(
            search: marker, status: $"status-{marker}", page: 2, pageSize: 10);

        Assert.Equal(12, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(100, largestPage.PageSize);
        Assert.Equal(events[^1].Id, page.Items[0].Id);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(events[1].Id, secondPage.Items[0].Id);
        Assert.Equal(events[0].Id, secondPage.Items[1].Id);
    }

    [Theory]
    [InlineData("ToEmail")]
    [InlineData("Subject")]
    [InlineData("ProviderMessageId")]
    [InlineData("LastEventCode")]
    [InlineData("UserEmail")]
    [InlineData("UserName")]
    [InlineData("PhoneNumber")]
    [InlineData("OriginalEmail")]
    public async Task AdminEmailJournal_SearchMatchesEachMessageAndUserField(string matchingField)
    {
        await using var context = fixture.CreateContext();
        var marker = $"search-{Guid.NewGuid():N}";
        var user = this.CreateUser("journal-search", email: $"user-{Guid.NewGuid():N}@example.com");
        user.OriginalEmail = "other-original@example.com";
        user.UserName ="other-user-name";
        user.PhoneNumber = "+70000000000";
        var message = new EmailMessage
        {
            ToEmail = "other-recipient@example.com",
            Subject = "other-subject",
            StatusCode = "queued",
            ProviderMessageId = "other-provider-id",
            LastEventCode = "other-event"
        };

        switch (matchingField)
        {
            case "ToEmail":
                message.ToEmail = $"{marker}@example.com";
                break;
            case "Subject":
                message.Subject = marker;
                break;
            case "ProviderMessageId":
                message.ProviderMessageId = marker;
                break;
            case "LastEventCode":
                message.LastEventCode = marker;
                break;
            case "UserEmail":
                user.Email = $"{marker}@example.com";
                user.NormalizedEmail = user.Email!.ToUpperInvariant();
                break;
            case "UserName":
                user.UserName = marker;
                break;
            case "PhoneNumber":
                user.PhoneNumber = marker;
                break;
            case "OriginalEmail":
                user.OriginalEmail = marker;
                break;
        }

        message.User = user;
        context.EmailMessages.Add(message);
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var result = await journal.GetPageAsync(search: marker);

        var item = Assert.Single(result.Items);
        Assert.Equal(message.Id, item.Id);
    }

    [Fact]
    public async Task AdminEmailJournal_SearchWithoutMatchesHandlesMissingUserFields()
    {
        await using var context = fixture.CreateContext();
        var marker = $"missing-search-{Guid.NewGuid():N}";
        var user = new ApplicationUser { Id = $"journal-search-{Guid.NewGuid():N}" };
        var linkedMessage = new EmailMessage
        {
            User = user,
            ToEmail = "recipient@example.com",
            Subject = "subject",
            ProviderMessageId = "provider-id",
            LastEventCode = "delivered"
        };
        var unlinkedMessage = new EmailMessage
        {
            ToEmail = "another-recipient@example.com",
            Subject = "another-subject",
            ProviderMessageId = "another-provider-id",
            LastEventCode = "opened"
        };
        context.EmailMessages.AddRange(linkedMessage, unlinkedMessage);
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var result = await journal.GetPageAsync(search: marker);
        var allMessages = await journal.GetPageAsync();

        Assert.Empty(result.Items);
        Assert.Contains(allMessages.Items, item => item.Id == unlinkedMessage.Id && item.UserDisplayName == null);
        Assert.Contains(allMessages.Items, item => item.Id == linkedMessage.Id && item.UserDisplayName == user.Id);
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("ProviderMessageId")]
    [InlineData("EventName")]
    [InlineData("Status")]
    [InlineData("Url")]
    public async Task AdminEmailJournal_SearchMatchesEachWebhookField(string matchingField)
    {
        await using var context = fixture.CreateContext();
        var marker = $"webhook-search-{Guid.NewGuid():N}";
        var messageEvent = new EmailMessageEvent
        {
            Email = "other@example.com",
            ProviderMessageId = "other-provider-id",
            EventName = "other-event",
            Status = "other-status",
            Url = "https://example.com/other"
        };

        switch (matchingField)
        {
            case "Email":
                messageEvent.Email = $"{marker}@example.com";
                break;
            case "ProviderMessageId":
                messageEvent.ProviderMessageId = marker;
                break;
            case "EventName":
                messageEvent.EventName = marker;
                break;
            case "Status":
                messageEvent.Status = marker;
                break;
            case "Url":
                messageEvent.Url = $"https://example.com/{marker}";
                break;
        }

        context.EmailMessageEvents.Add(messageEvent);
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var result = await journal.GetWebhookEventsAsync(search: marker);

        Assert.Equal(messageEvent.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task AdminEmailJournal_SearchWithoutWebhookMatchesHandlesMissingFields()
    {
        await using var context = fixture.CreateContext();
        var messageEvent = new EmailMessageEvent
        {
            EventName = "unrelated-event",
            RawPayload = "{}"
        };
        context.EmailMessageEvents.Add(messageEvent);
        await context.SaveChangesAsync();
        var journal = new AdminEmailJournalService(context);

        var result = await journal.GetWebhookEventsAsync(search: $"missing-{Guid.NewGuid():N}");

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task AdminEmailJournal_DashboardAggregatesMessagesAndWebhookEvents()
    {
        await using var context = fixture.CreateContext();
        var status = $"dash-{Guid.NewGuid():N}";
        var message = new EmailMessage
        {
            ToEmail = $"dashboard-{Guid.NewGuid():N}@example.com",
            StatusCode = status,
            CreatedAt = DateTime.UtcNow
        };
        context.EmailMessages.Add(message);
        await context.SaveChangesAsync();
        var newestAt = DateTime.UtcNow;
        context.EmailMessageEvents.AddRange(
            new EmailMessageEvent
            {
                EmailMessageId = message.Id,
                EventName = "custom-dashboard-event",
                Status = status,
                ReceivedAt = newestAt,
                EventAt = newestAt
            },
            new EmailMessageEvent
            {
                EmailMessageId = message.Id,
                EventName = "event-without-status",
                Status = null,
                ReceivedAt = newestAt.AddSeconds(-1)
            });
        await context.SaveChangesAsync();
        var totalMessages = await context.EmailMessages.CountAsync();
        var totalEvents = await context.EmailMessageEvents.CountAsync();
        var journal = new AdminEmailJournalService(context);

        var dashboard = await journal.GetDashboardAsync(CancellationToken.None);

        Assert.Equal(totalMessages, dashboard.TotalMessages);
        Assert.Equal(totalEvents, dashboard.TotalWebhookEvents);
        var expectedLastWebhookAt = await context.EmailMessageEvents.MaxAsync(item => item.ReceivedAt);
        Assert.Equal(expectedLastWebhookAt, dashboard.LastWebhookAt);
        Assert.Contains(dashboard.MessageStatusCounts, item => item.Code == status && item.Count == 1);
        Assert.Contains(dashboard.EventStatusCounts, item => item.Code == status && item.Count == 1);
        Assert.True(dashboard.Since <= DateTime.UtcNow.AddDays(-14));
    }

    private AdminEmailCompositionService CreateCompositionService(
        OpenCRMContext context,
        IEmailCampaignService? campaigns = null)
    {
        return new AdminEmailCompositionService(
            context,
            campaigns ?? Mock.Of<IEmailCampaignService>());
    }

    private ApplicationUser CreateUser(
        string prefix,
        string? email = null,
        bool emailConfirmed = false,
        bool advertisingConsentAccepted = false,
        DateTime? createdAt = null)
    {
        email ??= $"{prefix}-{Guid.NewGuid():N}@example.com";
        return new ApplicationUser
        {
            Id = $"{prefix}-{Guid.NewGuid():N}",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = emailConfirmed,
            AdvertisingConsentAccepted = advertisingConsentAccepted,
            DatetimeCreation = createdAt ?? DateTime.UtcNow
        };
    }
}
