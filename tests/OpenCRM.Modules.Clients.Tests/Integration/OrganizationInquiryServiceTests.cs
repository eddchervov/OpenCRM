using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class OrganizationInquiryServiceTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task KeepsSeparateInquiriesForSamePhoneAndWritesVersionedHistory()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var save = new OrganizationInquirySaveDto
        {
            ContactName = "Анна",
            ContactPhone = "+7 900 111-22-33",
            InterestText = "Полировка кузова",
            NextActionText = "Перезвонить",
            NextActionAt = DateTime.UtcNow.AddDays(1),
            ResponsibleUserId = organization.OwnerUserId
        };

        var first = await service.CreateAsync(organization.OwnerUserId, organization.Id, save);
        var second = await service.CreateAsync(organization.OwnerUserId, organization.Id, save with
        {
            ContactName = "Анна, повторно",
            InterestText = "Химчистка салона"
        });
        var updated = await service.UpdateAsync(organization.OwnerUserId, organization.Id, first.Id, save with
        {
            Version = first.Version,
            InterestText = "Полировка и защита кузова",
            NextActionText = "Согласовать объём",
            ResponsibleUserId = null
        });
        var moved = await service.ChangeStageAsync(organization.OwnerUserId, organization.Id, first.Id, OrganizationInquiryStageCodes.Proposal, updated.Version);

        var conflict = await Assert.ThrowsAsync<InquiryConflictException>(() => service.CloseAsync(
            organization.OwnerUserId, organization.Id, first.Id, "Не подходит", updated.Version));
        var closed = await service.CloseAsync(organization.OwnerUserId, organization.Id, first.Id, "Не подходит", moved.Version);
        var history = await service.GetHistoryAsync(organization.OwnerUserId, organization.Id, first.Id);
        var page = await service.GetPageAsync(organization.OwnerUserId, organization.Id, new OrganizationInquiryQueryDto
        {
            SearchQuery = "Полировка",
            SourceCode = null,
            StageCode = "all"
        });

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.ContactPhone, second.ContactPhone);
        Assert.Equal(OrganizationInquiryStageCodes.Closed, closed.StageCode);
        Assert.Equal("Не подходит", closed.CloseReason);
        Assert.Equal("Обращение изменилось. Обновите данные и повторите действие.", conflict.Message);
        Assert.Contains(history.Items, item => item.EventType == "created");
        Assert.Contains(history.Items, item => item.EventType == "responsible");
        Assert.Contains(history.Items, item => item.EventType == "action");
        Assert.Contains(history.Items, item => item.EventType == "stage_changed");
        Assert.Contains(history.Items, item => item.EventType == "closed");
        Assert.Equal(1, page.TotalCount);
        var otherOrganization = await fixture.CreateOrganizationAsync(context, "Другое рабочее пространство");
        await EnableInquiryScenarioAsync(context, otherOrganization);
        Assert.Null(await service.GetAsync(organization.OwnerUserId, otherOrganization.Id, first.Id));
        Assert.Equal(2, await context.OrganizationInquiries.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task BookingLinkRequiresTransactionAndMarksBookedExactlyOnce()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        using var services = fixture.CreateServices(context);
        var inquiryService = services.GetRequiredService<IOrganizationInquiryService>();
        var bookingLink = services.GetRequiredService<IOrganizationInquiryBookingLinkService>();
        var inquiry = await inquiryService.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Клиент",
            InterestText = "Нанесение покрытия"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => bookingLink.LockAndGetAsync(organization.OwnerUserId, organization.Id, inquiry.Id));
        OrganizationInquiryBookingLinkDto? state = null;
        Guid maintenanceId = Guid.Empty;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            state = await bookingLink.LockAndGetAsync(organization.OwnerUserId, organization.Id, inquiry.Id);
            Assert.NotNull(state);
            Assert.Equal(OrganizationInquiryStageCodes.New, state.StageCode);
            Assert.Equal(inquiry.Version, state.Version);

            var vehicle = await fixture.AddVehicleAsync(context, organization);
            var maintenance = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, OpenCRM.Domain.Enums.MaintenanceStatus.Planned, DateTime.UtcNow);
            context.Maintenance.Add(maintenance);
            await context.SaveChangesAsync();
            maintenanceId = maintenance.Id;
            await bookingLink.MarkBookedAsync(organization.OwnerUserId, organization.Id, inquiry.Id, state.Version, maintenance.Id);
            await transaction.CommitAsync();
        });
        var booked = await inquiryService.GetAsync(organization.OwnerUserId, organization.Id, inquiry.Id);

        Assert.Equal(OrganizationInquiryStageCodes.Booked, booked!.StageCode);
        Assert.Equal(maintenanceId, booked.MaintenanceId);
        Assert.Null(booked.NextActionAt);
    }

    [Fact]
    public async Task NormalizesEmptyActionAndRejectsVehicleFromAnotherClient()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент 1");
        var otherClient = ClientsDatabaseFixture.CreateClient(organization, "Клиент 2");
        context.OrganizationClients.AddRange(client, otherClient);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);

        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Клиент",
            InterestText = "Услуга",
            NextActionText = "   ",
            OrganizationClientId = client.Id,
            VehicleId = vehicle.Id
        });

        Assert.Null(inquiry.NextActionText);
        Assert.Null(inquiry.NextActionAt);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            inquiry.Id,
            new OrganizationInquirySaveDto
            {
                ContactName = "Клиент",
                InterestText = "Услуга",
                OrganizationClientId = otherClient.Id,
                VehicleId = vehicle.Id,
                Version = inquiry.Version
            }));
    }

    [Fact]
    public async Task DefaultsToOpenInquiriesAndIncludesActionsOverdueToday()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var open = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Открытый контакт",
            InterestText = "Услуга",
            NextActionText = "Перезвонить",
            NextActionAt = DateTime.UtcNow.AddMinutes(-5)
        });
        var closed = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Закрытый контакт",
            InterestText = "Услуга"
        });
        await service.CloseAsync(organization.OwnerUserId, organization.Id, closed.Id, "Отказ", closed.Version);

        var defaultPage = await service.GetPageAsync(organization.OwnerUserId, organization.Id, new OrganizationInquiryQueryDto());
        var overduePage = await service.GetPageAsync(organization.OwnerUserId, organization.Id, new OrganizationInquiryQueryDto
        {
            ActionDue = "overdue"
        });

        Assert.Contains(defaultPage.Items, item => item.Id == open.Id);
        Assert.DoesNotContain(defaultPage.Items, item => item.Id == closed.Id);
        Assert.Single(overduePage.Items);
        Assert.Equal(open.Id, overduePage.Items[0].Id);
    }

    [Fact]
    public async Task ActionRemindersAreLimitedToTwentyOpenActionsAssignedToCurrentUserAndDueWithinFifteenMinutes()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        var otherUser = new ApplicationUser
        {
            UserName = $"client-test-{Guid.NewGuid():N}",
            Email = $"client-test-{Guid.NewGuid():N}@example.test"
        };
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = otherUser.Id,
            RoleCode = OrganizationRoleCodes.Employee
        });
        var now = DateTime.UtcNow;
        var ownReminders = Enumerable.Range(0, 22).Select(index => new OrganizationInquiry
        {
            OrganizationId = organization.Id,
            ContactName = $"Свой клиент {index}",
            Interest = "Услуга",
            StageCode = OrganizationInquiryStageCodes.New,
            ResponsibleUserId = organization.OwnerUserId,
            NextActionText = "Позвонить",
            NextActionAt = now.AddMinutes(-2).AddSeconds(index),
            CreatedByUserId = organization.OwnerUserId
        }).ToArray();
        context.OrganizationInquiries.AddRange(ownReminders);
        context.OrganizationInquiries.AddRange(
            new OrganizationInquiry
            {
                OrganizationId = organization.Id,
                ContactName = "Чужое действие",
                Interest = "Услуга",
                StageCode = OrganizationInquiryStageCodes.New,
                ResponsibleUserId = otherUser.Id,
                NextActionText = "Позвонить",
                NextActionAt = now.AddMinutes(-5),
                CreatedByUserId = organization.OwnerUserId
            },
            new OrganizationInquiry
            {
                OrganizationId = organization.Id,
                ContactName = "Закрытое действие",
                Interest = "Услуга",
                StageCode = OrganizationInquiryStageCodes.Closed,
                ResponsibleUserId = organization.OwnerUserId,
                NextActionText = "Позвонить",
                NextActionAt = now.AddMinutes(-6),
                CreatedByUserId = organization.OwnerUserId
            },
            new OrganizationInquiry
            {
                OrganizationId = organization.Id,
                ContactName = "Будущее действие",
                Interest = "Услуга",
                StageCode = OrganizationInquiryStageCodes.New,
                ResponsibleUserId = organization.OwnerUserId,
                NextActionText = "Позвонить",
                NextActionAt = now.AddMinutes(16),
                CreatedByUserId = organization.OwnerUserId
            });
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var reminders = await service.GetActionRemindersAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal(20, reminders.Count);
        Assert.All(reminders, item => Assert.Equal(organization.OwnerUserId, item.ResponsibleUserId));
        Assert.All(reminders, item => Assert.Contains(ownReminders.Take(20), reminder => reminder.Id == item.Id));
        Assert.Equal(reminders.OrderBy(item => item.NextActionAt).Select(item => item.Id), reminders.Select(item => item.Id));
    }

    [Fact]
    public async Task ReschedulesActionWithVersionedHistoryAndRejectsStaleOrCompletedActions()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Клиент",
            InterestText = "Согласовать состав работ",
            NextActionText = "Перезвонить по стоимости",
            NextActionAt = DateTime.UtcNow.AddMinutes(5)
        });
        var nextActionAt = DateTime.UtcNow.AddDays(1);

        var updated = await service.RescheduleActionAsync(
            organization.OwnerUserId,
            organization.Id,
            inquiry.Id,
            nextActionAt,
            inquiry.Version);
        var history = await service.GetHistoryAsync(organization.OwnerUserId, organization.Id, inquiry.Id);

        Assert.Equal("Перезвонить по стоимости", updated.NextActionText);
        Assert.Equal(nextActionAt.Ticks / 10 * 10, updated.NextActionAt!.Value.Ticks);
        Assert.Equal(inquiry.Version + 1, updated.Version);
        Assert.Contains(history.Items, item => item.EventType == "action_rescheduled");
        await Assert.ThrowsAsync<InquiryConflictException>(() => service.RescheduleActionAsync(
            organization.OwnerUserId,
            organization.Id,
            inquiry.Id,
            nextActionAt.AddDays(1),
            inquiry.Version));

        var completed = await service.CompleteActionAsync(organization.OwnerUserId, organization.Id, inquiry.Id, updated.Version);
        await Assert.ThrowsAsync<InquiryConflictException>(() => service.RescheduleActionAsync(
            organization.OwnerUserId,
            organization.Id,
            inquiry.Id,
            nextActionAt.AddDays(2),
            completed.Version));
    }

    [Fact]
    public async Task UsesRequestedTimeZoneForLocalDayWhenOrganizationTimeZoneIsMissing()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        organization.ShiftTimeZoneId = null;
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Утренний звонок",
            InterestText = "Полировка",
            NextActionText = "Перезвонить",
            NextActionAt = new DateTime(2026, 10, 23, 20, 0, 0, DateTimeKind.Utc)
        });
        var query = new OrganizationInquiryQueryDto
        {
            ActionDue = "today",
            Date = new DateOnly(2026, 10, 24),
            TimeZoneId = "Asia/Novosibirsk"
        };

        var localPage = await service.GetPageAsync(organization.OwnerUserId, organization.Id, query);
        var utcPage = await service.GetPageAsync(organization.OwnerUserId, organization.Id, query with { TimeZoneId = "UTC" });
        var board = await service.GetBoardAsync(organization.OwnerUserId, organization.Id, query);

        Assert.Null(inquiry.TimeZoneId);
        Assert.Equal(inquiry.Id, Assert.Single(localPage.Items).Id);
        Assert.Empty(utcPage.Items);
        Assert.Equal(1, board.Columns.Sum(column => column.Count));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPageAsync(
            organization.OwnerUserId, organization.Id, query with { TimeZoneId = "Invalid/Zone" }));

        organization.ShiftTimeZoneId = "Europe/Moscow";
        await context.SaveChangesAsync();
        var organizationPage = await service.GetPageAsync(organization.OwnerUserId, organization.Id, query);

        Assert.Equal("Europe/Moscow", organizationPage.TimeZoneId);
        Assert.Empty(organizationPage.Items);
    }

    [Fact]
    public async Task StoresStageCommentInTheSameVersionedHistoryEvent()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Анна",
            InterestText = "Полировка кузова"
        });

        var changed = await service.ChangeStageAsync(organization.OwnerUserId, organization.Id, inquiry.Id,
            OrganizationInquiryStageCodes.Proposal, inquiry.Version, "  Стоимость согласована, предложение отправлено  ");
        var history = await service.GetHistoryAsync(organization.OwnerUserId, organization.Id, inquiry.Id);
        var transition = Assert.Single(history.Items, item => item.EventType == "stage_changed");

        Assert.Equal(inquiry.Version + 1, changed.Version);
        Assert.Equal(OrganizationInquiryStageCodes.Proposal, changed.StageCode);
        Assert.Equal(OrganizationInquiryStageCodes.New, transition.PreviousValue);
        Assert.Equal(OrganizationInquiryStageCodes.Proposal, transition.NewValue);
        Assert.Equal("Стоимость согласована, предложение отправлено", transition.Description);
        Assert.Equal(organization.OwnerUserId, transition.ActorUserId);
        Assert.DoesNotContain(history.Items, item => item.EventType == "note");
        await Assert.ThrowsAsync<InquiryConflictException>(() => service.ChangeStageAsync(
            organization.OwnerUserId, organization.Id, inquiry.Id, OrganizationInquiryStageCodes.Ready, inquiry.Version, "Устаревшая версия"));
        var afterConflict = await service.GetHistoryAsync(organization.OwnerUserId, organization.Id, inquiry.Id);

        Assert.Equal(history.TotalCount, afterConflict.TotalCount);
        var withoutComment = await service.ChangeStageAsync(organization.OwnerUserId, organization.Id, inquiry.Id,
            OrganizationInquiryStageCodes.Ready, changed.Version);
        var finalHistory = await service.GetHistoryAsync(organization.OwnerUserId, organization.Id, inquiry.Id);

        Assert.Equal(changed.Version + 1, withoutComment.Version);
        Assert.Null(Assert.Single(finalHistory.Items, item => item.NewValue == OrganizationInquiryStageCodes.Ready).Description);
    }

    [Fact]
    public async Task RejectsInvalidStageCommentAndTransitionWithoutWritingHistory()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        await EnableInquiryScenarioAsync(context, organization);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Анна",
            InterestText = "Полировка кузова"
        });

        await Assert.ThrowsAsync<ArgumentException>(() => service.ChangeStageAsync(organization.OwnerUserId, organization.Id,
            inquiry.Id, OrganizationInquiryStageCodes.Proposal, inquiry.Version, new string('а', 1001)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ChangeStageAsync(organization.OwnerUserId, organization.Id,
            inquiry.Id, OrganizationInquiryStageCodes.Closed, inquiry.Version, "Комментарий не заменяет причину закрытия"));
        await Assert.ThrowsAsync<InquiryConflictException>(() => service.ChangeStageAsync(organization.OwnerUserId, organization.Id,
            inquiry.Id, OrganizationInquiryStageCodes.New, inquiry.Version, "Без перехода"));
        var current = await service.GetAsync(organization.OwnerUserId, organization.Id, inquiry.Id);
        var history = await service.GetHistoryAsync(organization.OwnerUserId, organization.Id, inquiry.Id);

        Assert.Equal(inquiry.Version, current!.Version);
        Assert.Equal(OrganizationInquiryStageCodes.New, current.StageCode);
        Assert.Single(history.Items);
    }

    [Fact]
    public async Task RequiresInquiryReadPermissionBeforeCreateOrUpdate()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Контакт",
            InterestText = "Услуга"
        });
        var permissionCodes = new HashSet<string>
        {
            OrganizationPermissionCodes.InquiriesView
        };
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(item => item.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        subscription.Setup(item => item.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<string, Guid, string>((_, _, permissionCode) => permissionCodes.Contains(permissionCode)
                ? Task.FromException(new UnauthorizedAccessException("Нет права просмотра обращений."))
                : Task.CompletedTask);
        subscription.Setup(item => item.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns<string, Guid, string>((_, _, permissionCode) => permissionCodes.Contains(permissionCode)
                ? Task.FromException(new UnauthorizedAccessException("Нет права записи."))
                : Task.CompletedTask);
        using var restrictedServices = fixture.CreateServices(context, subscriptionService: subscription.Object);
        var restrictedService = restrictedServices.GetRequiredService<IOrganizationInquiryService>();
        var inquiryCount = await context.OrganizationInquiries.CountAsync();
        var eventCount = await context.OrganizationInquiryEvents.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restrictedService.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new OrganizationInquirySaveDto
            {
                ContactName = "Новый контакт",
                InterestText = "Услуга"
            }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restrictedService.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            inquiry.Id,
            new OrganizationInquirySaveDto
            {
                ContactName = "Изменённый контакт",
                InterestText = "Услуга",
                Version = inquiry.Version
            }));

        Assert.Equal(inquiryCount, await context.OrganizationInquiries.CountAsync());
        Assert.Equal(eventCount, await context.OrganizationInquiryEvents.CountAsync());
        Assert.Equal("Контакт", await context.OrganizationInquiries.AsNoTracking()
            .Where(item => item.Id == inquiry.Id).Select(item => item.ContactName).SingleAsync());
        subscription.Verify(item => item.EnsureWorkspaceCanWriteAsync(
            organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.InquiriesManage), Times.Never);
    }

    [Fact]
    public async Task ContactEditPreservesSoftDeletedClientAndVehicleLinks()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Архивный клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Контакт",
            InterestText = "Услуга",
            OrganizationClientId = client.Id,
            VehicleId = vehicle.Id
        });
        client.Delete(DateTime.UtcNow);
        vehicle.IsDeleted = true;
        vehicle.DeletionDate = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var updated = await service.UpdateAsync(organization.OwnerUserId, organization.Id, inquiry.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Контакт обновлён",
            InterestText = "Услуга",
            OrganizationClientId = client.Id,
            VehicleId = vehicle.Id,
            Version = inquiry.Version
        });

        Assert.Equal("Контакт обновлён", updated.ContactName);
        Assert.Equal(client.Id, updated.OrganizationClientId);
        Assert.Equal(vehicle.Id, updated.VehicleId);
    }

    [Fact]
    public async Task PreservesHiddenLinksAndRejectsLinkChangesWithoutViewPermissions()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Связанный клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        using var fullAccessServices = fixture.CreateServices(context);
        var fullAccessService = fullAccessServices.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await fullAccessService.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Контакт",
            InterestText = "Услуга",
            OrganizationClientId = client.Id,
            VehicleId = vehicle.Id,
            ResponsibleUserId = organization.OwnerUserId
        });

        var deniedPermissions = new HashSet<string>
        {
            OrganizationPermissionCodes.ClientsView,
            OrganizationPermissionCodes.VehiclesView,
            OrganizationPermissionCodes.MembersView
        };
        using var restrictedServices = fixture.CreateServices(context, deniedPermissionCodes: deniedPermissions);
        var restrictedService = restrictedServices.GetRequiredService<IOrganizationInquiryService>();
        var hiddenCard = await restrictedService.GetAsync(organization.OwnerUserId, organization.Id, inquiry.Id);

        Assert.Null(hiddenCard!.OrganizationClientId);
        Assert.Null(hiddenCard.VehicleId);
        var updated = await restrictedService.UpdateAsync(organization.OwnerUserId, organization.Id, inquiry.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Контакт обновлён",
            InterestText = "Услуга",
            Version = hiddenCard.Version
        });

        var stored = await context.OrganizationInquiries.AsNoTracking().SingleAsync(item => item.Id == inquiry.Id);
        Assert.Equal(client.Id, stored.OrganizationClientId);
        Assert.Equal(vehicle.Id, stored.VehicleId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restrictedService.UpdateAsync(
            organization.OwnerUserId,
            organization.Id,
            inquiry.Id,
            new OrganizationInquirySaveDto
            {
                ContactName = "Контакт",
                InterestText = "Услуга",
                OrganizationClientId = client.Id,
                Version = updated.Version
            }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restrictedService.CreateAsync(
            organization.OwnerUserId,
            organization.Id,
            new OrganizationInquirySaveDto
            {
                ContactName = "Другой контакт",
                InterestText = "Услуга",
                VehicleId = vehicle.Id
            }));

        await fullAccessService.UpdateAsync(organization.OwnerUserId, organization.Id, inquiry.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Контакт обновлён",
            InterestText = "Услуга",
            Version = updated.Version
        });
        var restrictedHistory = await restrictedService.GetHistoryAsync(organization.OwnerUserId, organization.Id, inquiry.Id);
        Assert.Contains(restrictedHistory.Items, item => item.EventType == "client");
        Assert.Contains(restrictedHistory.Items, item => item.EventType == "vehicle");
        Assert.Contains(restrictedHistory.Items, item => item.EventType == "responsible");
        Assert.All(restrictedHistory.Items.Where(item => item.EventType is "client" or "vehicle" or "responsible"), item =>
        {
            Assert.Null(item.PreviousValue);
            Assert.Null(item.NewValue);
        });
    }

    [Fact]
    public async Task ReadOnlyWorkspaceAllowsReadingAndRejectsEveryMutation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationInquiryService>();
        var inquiry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto
        {
            ContactName = "Контакт",
            InterestText = "Полировка",
            NextActionText = "Перезвонить",
            NextActionAt = DateTime.UtcNow.AddHours(1)
        });

        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(item => item.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        subscription.Setup(item => item.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(item => item.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Рабочее пространство доступно только для просмотра."));
        using var readOnlyServices = fixture.CreateServices(context, subscriptionService: subscription.Object);
        var readOnlyService = readOnlyServices.GetRequiredService<IOrganizationInquiryService>();
        var bookingLinks = readOnlyServices.GetRequiredService<IOrganizationInquiryBookingLinkService>();
        var eventCount = await context.OrganizationInquiryEvents.CountAsync();
        var inquiryCount = await context.OrganizationInquiries.CountAsync();

        Assert.NotNull(await readOnlyService.GetAsync(organization.OwnerUserId, organization.Id, inquiry.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readOnlyService.CreateAsync(
            organization.OwnerUserId, organization.Id, new OrganizationInquirySaveDto { ContactName = "Другой", InterestText = "Мойка" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readOnlyService.UpdateAsync(
            organization.OwnerUserId, organization.Id, inquiry.Id, new OrganizationInquirySaveDto
            {
                ContactName = "Изменённый",
                InterestText = "Мойка",
                Version = inquiry.Version
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readOnlyService.ChangeStageAsync(
            organization.OwnerUserId, organization.Id, inquiry.Id, OrganizationInquiryStageCodes.Ready, inquiry.Version));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readOnlyService.CloseAsync(
            organization.OwnerUserId, organization.Id, inquiry.Id, "Отказ", inquiry.Version));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readOnlyService.AddNoteAsync(
            organization.OwnerUserId, organization.Id, inquiry.Id, "Заметка", inquiry.Version));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readOnlyService.CompleteActionAsync(
            organization.OwnerUserId, organization.Id, inquiry.Id, inquiry.Version));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bookingLinks.LockAndGetAsync(
            organization.OwnerUserId, organization.Id, inquiry.Id));

        Assert.Equal(inquiryCount, await context.OrganizationInquiries.CountAsync());
        Assert.Equal(eventCount, await context.OrganizationInquiryEvents.CountAsync());
        Assert.Equal(inquiry.Version, await context.OrganizationInquiries.Where(item => item.Id == inquiry.Id)
            .Select(item => item.Version).SingleAsync());
    }

    private static async Task EnableInquiryScenarioAsync(OpenCRMContext context, Organization organization)
    {
        organization.PrimaryScenarioCode = OrganizationTypeCodes.Detailing;
        await context.SaveChangesAsync();
    }
}
