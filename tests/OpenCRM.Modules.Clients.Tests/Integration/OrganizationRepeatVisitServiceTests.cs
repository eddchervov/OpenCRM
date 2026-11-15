using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class OrganizationRepeatVisitServiceTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task CreatesOptionalRecommendationIdempotentlyAndCompletesOnlyContactTask()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = "Asia/Novosibirsk";
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationRepeatVisitService>();
        var options = await service.GetOptionsAsync(organization.OwnerUserId, organization.Id);
        Assert.True(options.CanCreate);
        Assert.Contains(options.Responsibles, item => item.Id == organization.OwnerUserId);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент с рекомендацией");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var source = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1));
        source.BuyerClientId = null;
        source.HandedOverAt = DateTime.UtcNow.AddHours(-2);
        source.ReadyForHandoverAt = source.HandedOverAt.Value.AddMinutes(-10);
        context.Maintenance.Add(source);
        await context.SaveChangesAsync();

        var suggestedDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk"))).AddDays(3);
        var optional = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Повторная полировка",
            SuggestedVisitDate = suggestedDate
        });
        var retry = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = optional.Id,
            SourceMaintenanceId = source.Id,
            ServiceName = "Другое название при повторе",
            SuggestedVisitDate = suggestedDate
        });

        Assert.Equal(optional.Id, retry.Id);
        Assert.Null(optional.ContactByUtc);
        Assert.True(optional.IsActive);
        Assert.Equal("Asia/Novosibirsk", optional.TimeZoneId);
        Assert.Equal(client.Id, optional.OrganizationClientId);
        Assert.Equal(1, await context.OrganizationAuditEvents.CountAsync(item => item.TargetId == optional.Id.ToString()));

        var bookedMaintenance = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Planned, DateTime.UtcNow.AddDays(2));
        context.Maintenance.Add(bookedMaintenance);
        await context.SaveChangesAsync();
        var persistedRecommendation = await context.OrganizationRepeatVisits.SingleAsync(item => item.Id == optional.Id);
        persistedRecommendation.BookedMaintenanceId = bookedMaintenance.Id;
        await context.SaveChangesAsync();
        var bookedRecommendation = await service.GetAsync(organization.OwnerUserId, organization.Id, optional.Id);

        Assert.False(bookedRecommendation!.CanEdit);

        var contactBy = DateTime.UtcNow.AddHours(1);
        var actionable = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Обновление покрытия",
            SuggestedVisitDate = suggestedDate,
            ResponsibleUserId = organization.OwnerUserId,
            ContactByUtc = contactBy
        });
        var completed = await service.CompleteActionAsync(organization.OwnerUserId, organization.Id, actionable.Id, actionable.Version);

        Assert.NotNull(completed.ContactCompletedAtUtc);
        Assert.True(completed.IsActive);
        Assert.Null(completed.BookedMaintenanceId);
        var actions = await service.GetPageAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitQueryDto
        {
            ActionsOnly = true,
            Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk"))).AddDays(1)
        });
        Assert.Empty(actions.Items);

        var persistedTask = await context.OrganizationRepeatVisits.SingleAsync(item => item.Id == actionable.Id);
        persistedTask.ContactByUtc = DateTime.UtcNow.AddDays(-1);
        persistedTask.ContactCompletedAtUtc = persistedTask.ContactByUtc.Value.AddMinutes(10);
        persistedTask.Version++;
        await context.SaveChangesAsync();
        await context.Entry(persistedTask).ReloadAsync();
        var savedContactBy = persistedTask.ContactByUtc;
        var savedCompletedAt = persistedTask.ContactCompletedAtUtc;
        var updatedSuggestedDate = suggestedDate.AddDays(1);
        var updated = await service.UpdateAsync(organization.OwnerUserId, organization.Id, actionable.Id, new OrganizationRepeatVisitSaveDto
        {
            ServiceName = "Обновлённое название",
            Reason = "Срок связи уже прошёл",
            SuggestedVisitDate = updatedSuggestedDate,
            ResponsibleUserId = organization.OwnerUserId,
            ContactByUtc = savedContactBy,
            Version = persistedTask.Version
        });

        Assert.Equal(savedContactBy, updated.ContactByUtc);
        Assert.Equal(savedCompletedAt, updated.ContactCompletedAtUtc);
        Assert.Equal(updatedSuggestedDate, updated.SuggestedVisitDate);

        var pastDeadline = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(organization.OwnerUserId, organization.Id, actionable.Id, new OrganizationRepeatVisitSaveDto
        {
            ServiceName = updated.ServiceName,
            SuggestedVisitDate = updatedSuggestedDate,
            ResponsibleUserId = organization.OwnerUserId,
            ContactByUtc = DateTime.UtcNow.AddHours(-1),
            Version = updated.Version
        }));

        Assert.Contains("выберите дату и время в будущем", pastDeadline.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UTC", pastDeadline.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContactTaskDoesNotRequireResponsibleButStillRequiresOrganizationTimeZone()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = "Asia/Novosibirsk";
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var service = services.GetRequiredService<IOrganizationRepeatVisitService>();
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var source = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1));
        source.HandedOverAt = DateTime.UtcNow.AddHours(-2);
        source.ReadyForHandoverAt = source.HandedOverAt.Value.AddMinutes(-10);
        context.Maintenance.Add(source);
        await context.SaveChangesAsync();
        var suggestedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);

        var unassignedTask = await service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Повторная услуга",
            SuggestedVisitDate = suggestedDate,
            ContactByUtc = DateTime.UtcNow.AddHours(1)
        });
        organization.ShiftTimeZoneId = null;
        await context.SaveChangesAsync();
        var missingTimeZone = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Повторная услуга",
            SuggestedVisitDate = suggestedDate,
            ResponsibleUserId = organization.OwnerUserId,
            ContactByUtc = DateTime.UtcNow.AddHours(1)
        }));

        Assert.Null(unassignedTask.ResponsibleUserId);
        Assert.Contains("часовой пояс", missingTimeZone.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssignedEmployeeCanManageOnlyTheirContactTaskAndReadOnlyWorkspaceBlocksWrites()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = "Asia/Novosibirsk";
        var employee = new OpenCRM.Domain.Identity.ApplicationUser
        {
            UserName = $"repeat-visit-{Guid.NewGuid():N}",
            Email = $"repeat-visit-{Guid.NewGuid():N}@example.test"
        };
        var secondEmployee = new OpenCRM.Domain.Identity.ApplicationUser
        {
            UserName = $"repeat-visit-{Guid.NewGuid():N}",
            Email = $"repeat-visit-{Guid.NewGuid():N}@example.test"
        };
        context.Users.Add(employee);
        context.Users.Add(secondEmployee);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = employee.Id,
            RoleCode = OrganizationRoleCodes.Employee
        });
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = secondEmployee.Id,
            RoleCode = OrganizationRoleCodes.Employee
        });
        await context.SaveChangesAsync();

        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var source = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Completed, DateTime.UtcNow.AddDays(-1));
        source.HandedOverAt = DateTime.UtcNow.AddHours(-2);
        source.ReadyForHandoverAt = source.HandedOverAt.Value.AddMinutes(-10);
        context.Maintenance.Add(source);
        await context.SaveChangesAsync();

        using var ownerServices = fixture.CreateServices(context);
        var ownerService = ownerServices.GetRequiredService<IOrganizationRepeatVisitService>();
        var suggestedVisitDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk"))).AddDays(4);
        var ownTask = await ownerService.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Повторный визит сотруднику",
            SuggestedVisitDate = suggestedVisitDate,
            ResponsibleUserId = employee.Id,
            ContactByUtc = DateTime.UtcNow.AddHours(1)
        });
        var otherTask = await ownerService.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Повторный визит владельцу",
            SuggestedVisitDate = suggestedVisitDate,
            ResponsibleUserId = organization.OwnerUserId,
            ContactByUtc = DateTime.UtcNow.AddHours(1)
        });
        var commonTask = await ownerService.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Общая задача связи",
            SuggestedVisitDate = suggestedVisitDate,
            ContactByUtc = DateTime.UtcNow.AddHours(1)
        });

        var deniedEdits = new HashSet<string>(StringComparer.Ordinal) { OrganizationPermissionCodes.ClientsEdit };
        var employeeSubscription = new Mock<IOrganizationSubscriptionService>();
        employeeSubscription.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        employeeSubscription.Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((string _, Guid requestedOrganizationId, string _) => requestedOrganizationId == organization.Id
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException("Нет доступа к организации.")));
        employeeSubscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((string _, Guid _, string permissionCode) => deniedEdits.Contains(permissionCode)
                ? Task.FromException(new InvalidOperationException("Недостаточно прав."))
                : Task.CompletedTask);
        using var employeeServices = fixture.CreateServices(context, deniedPermissionCodes: deniedEdits, subscriptionService: employeeSubscription.Object);
        var employeeService = employeeServices.GetRequiredService<IOrganizationRepeatVisitService>();
        var employeeView = await employeeService.GetAsync(employee.Id, organization.Id, ownTask.Id);
        var employeeActions = await employeeService.GetPageAsync(employee.Id, organization.Id, new OrganizationRepeatVisitQueryDto
        {
            ActionsOnly = true,
            Date = suggestedVisitDate.AddDays(-3)
        });
        var commonTaskView = await employeeService.GetAsync(employee.Id, organization.Id, commonTask.Id);
        var rescheduledCommonTask = await employeeService.RescheduleActionAsync(
            employee.Id, organization.Id, commonTask.Id, DateTime.UtcNow.AddHours(2), commonTask.Version);
        var completed = await employeeService.CompleteActionAsync(employee.Id, organization.Id, ownTask.Id, ownTask.Version);
        var cannotCompleteOther = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => employeeService.CompleteActionAsync(
            employee.Id, organization.Id, otherTask.Id, otherTask.Version));
        using var secondEmployeeServices = fixture.CreateServices(context, deniedPermissionCodes: deniedEdits, subscriptionService: employeeSubscription.Object);
        var secondEmployeeService = secondEmployeeServices.GetRequiredService<IOrganizationRepeatVisitService>();
        var secondEmployeeActions = await secondEmployeeService.GetPageAsync(secondEmployee.Id, organization.Id, new OrganizationRepeatVisitQueryDto
        {
            ActionsOnly = true,
            Date = suggestedVisitDate.AddDays(-3)
        });
        var completedCommonTask = await secondEmployeeService.CompleteActionAsync(
            secondEmployee.Id, organization.Id, commonTask.Id, rescheduledCommonTask.Version);

        var otherOrganization = await fixture.CreateOrganizationAsync(context);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => employeeService.GetPageAsync(
            employee.Id, otherOrganization.Id, new OrganizationRepeatVisitQueryDto()));

        Assert.True(employeeView!.CanManageContact);
        Assert.False(employeeView.CanEdit);
        Assert.True(commonTaskView!.CanManageContact);
        Assert.Contains(employeeActions.Items, item => item.Id == ownTask.Id);
        Assert.Contains(employeeActions.Items, item => item.Id == commonTask.Id);
        Assert.DoesNotContain(employeeActions.Items, item => item.Id == otherTask.Id);
        Assert.Contains(secondEmployeeActions.Items, item => item.Id == commonTask.Id);
        Assert.NotNull(completedCommonTask.ContactCompletedAtUtc);
        Assert.NotNull(completed.ContactCompletedAtUtc);
        Assert.Contains("назначенный", cannotCompleteOther.Message, StringComparison.OrdinalIgnoreCase);

        var readOnlySubscription = new Mock<IOrganizationSubscriptionService>();
        readOnlySubscription.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(true);
        readOnlySubscription.Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        readOnlySubscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Рабочее пространство доступно только для чтения."));
        using var readOnlyServices = fixture.CreateServices(context, subscriptionService: readOnlySubscription.Object);
        var readOnlyService = readOnlyServices.GetRequiredService<IOrganizationRepeatVisitService>();
        var readOnlyCommonTask = await ownerService.CreateAsync(organization.OwnerUserId, organization.Id, new OrganizationRepeatVisitSaveDto
        {
            Id = Guid.NewGuid(),
            SourceMaintenanceId = source.Id,
            ServiceName = "Общая задача в режиме чтения",
            SuggestedVisitDate = suggestedVisitDate,
            ContactByUtc = DateTime.UtcNow.AddHours(1)
        });
        var readOnlyCommonTaskView = await readOnlyService.GetAsync(employee.Id, organization.Id, readOnlyCommonTask.Id);

        Assert.False(readOnlyCommonTaskView!.CanManageContact);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => readOnlyService.CompleteActionAsync(
            employee.Id, organization.Id, otherTask.Id, otherTask.Version));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => readOnlyService.CompleteActionAsync(
            employee.Id, organization.Id, readOnlyCommonTask.Id, readOnlyCommonTask.Version));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => readOnlyService.RescheduleActionAsync(
            employee.Id, organization.Id, readOnlyCommonTask.Id, DateTime.UtcNow.AddHours(2), readOnlyCommonTask.Version));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => readOnlyService.ChangeStateAsync(
            organization.OwnerUserId, organization.Id, otherTask.Id, false, otherTask.Version));
    }
}
