using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationTimeZoneTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task ReadRequiresWorkspaceAccessAndMembershipButAllowsReadOnlyMembers()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "timezone-reader-owner@example.test" };
        var reader = new ApplicationUser { Email = "timezone-reader@example.test" };
        var outsider = new ApplicationUser { Email = "timezone-outsider@example.test" };
        var organization = new Organization
        {
            Name = "Часовой пояс для чтения",
            OwnerUser = owner,
            Members =
            [
                new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner },
                new OrganizationMember { User = reader, RoleCode = OrganizationRoleCodes.Viewer }
            ]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                reader.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceView))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                outsider.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceView))
            .Returns(Task.CompletedTask);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
                reader.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ReturnsAsync(true);
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                reader.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ThrowsAsync(new UnauthorizedAccessException());
        var service = new OrganizationTimeZoneService(
            context,
            subscriptions.Object,
            permissions.Object,
            new OrganizationAuditService(context),
            new OrganizationTransaction(context));

        var result = await service.GetAsync(reader.Id, organization.Id);

        Assert.Null(result.TimeZoneId);
        Assert.False(result.CanManage);
        Assert.True(result.CanChange);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(outsider.Id, organization.Id));
        subscriptions.Verify(value => value.EnsureWorkspaceCanReadAsync(
            reader.Id,
            organization.Id,
            OrganizationPermissionCodes.WorkspaceView), Times.Once);
        subscriptions.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            reader.Id,
            organization.Id,
            OrganizationPermissionCodes.WorkspaceProfileEdit), Times.Once);
    }

    [Fact]
    public async Task UpdatesOrganizationTimeZoneAndAuditsTheChange()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "timezone-owner@example.test" };
        var organization = new Organization
        {
            Name = "Часовой пояс организации",
            OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .Returns(Task.CompletedTask);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ReturnsAsync(true);
        var service = new OrganizationTimeZoneService(
            context,
            subscriptions.Object,
            permissions.Object,
            new OrganizationAuditService(context),
            new OrganizationTransaction(context));

        var result = await service.UpdateAsync(owner.Id, organization.Id, new UpdateOrganizationTimeZoneDto
        {
            TimeZoneId = "Asia/Novosibirsk",
            ExpectedTimeZoneId = null
        });

        Assert.Equal("Asia/Novosibirsk", result.TimeZoneId);
        var legacyService = new OrganizationShiftService(
            context, subscriptions.Object, permissions.Object, new OrganizationAuditService(context));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => legacyService.SetTimeZoneAsync(
            owner.Id, organization.Id, "Europe/Moscow", null));
        await legacyService.SetTimeZoneAsync(owner.Id, organization.Id, "Asia/Novosibirsk");
        Assert.True(result.CanManage);
        Assert.True(result.CanChange);
        subscriptions.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            owner.Id,
            organization.Id,
            OrganizationPermissionCodes.ShiftsManage), Times.Never);
        Assert.Equal("Asia/Novosibirsk", await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organization.Id)
            .Select(item => item.ShiftTimeZoneId)
            .SingleAsync());
        Assert.Contains(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync(), item =>
            item.OrganizationId == organization.Id && item.EventType == "organization.timezone.changed");
    }

    [Fact]
    public async Task TimedMaintenancePreventsChangingOrganizationTimeZoneEvenWhenArchivedOrDeleted()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "timezone-scheduled-owner@example.test" };
        var organization = new Organization
        {
            Name = "Организация с расписанием",
            OwnerUser = owner,
            ShiftTimeZoneId = "Asia/Novosibirsk",
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var vehicle = new Vehicle
        {
            User = owner,
            Organization = organization,
            Name = "Техника с расписанием",
            DateTimeCreation = DateTime.UtcNow,
            IsArchived = true,
            ArchiveDate = DateTime.UtcNow
        };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = owner,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            Date = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
            PlannedDate = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            DateTimeCreation = DateTime.UtcNow,
            IsDeleted = true
        };
        context.Organizations.Add(organization);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceView))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .Returns(Task.CompletedTask);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ReturnsAsync(true);
        var service = new OrganizationTimeZoneService(
            context,
            subscriptions.Object,
            permissions.Object,
            new OrganizationAuditService(context),
            new OrganizationTransaction(context));

        var settings = await service.GetAsync(owner.Id, organization.Id);

        Assert.False(settings.CanChange);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(owner.Id, organization.Id,
            new UpdateOrganizationTimeZoneDto
            {
                TimeZoneId = "Europe/Moscow",
                ExpectedTimeZoneId = "Asia/Novosibirsk"
            }));
        var legacyShiftService = new OrganizationShiftService(
            context,
            subscriptions.Object,
            permissions.Object,
            new OrganizationAuditService(context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => legacyShiftService.SetTimeZoneAsync(
            owner.Id,
            organization.Id,
            "Europe/Moscow"));
        var unchanged = await service.UpdateAsync(owner.Id, organization.Id, new UpdateOrganizationTimeZoneDto
        {
            TimeZoneId = "Asia/Novosibirsk",
            ExpectedTimeZoneId = "Asia/Novosibirsk"
        });

        Assert.Equal("Asia/Novosibirsk", unchanged.TimeZoneId);
        Assert.False(unchanged.CanChange);
    }

    [Fact]
    public async Task CanReplaceLegacyUtcAliasAndNormalizesUtcSelection()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "timezone-legacy-utc@example.test" };
        var organization = new Organization
        {
            Name = "Старый UTC часовой пояс",
            OwnerUser = owner,
            ShiftTimeZoneId = "UTC",
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .Returns(Task.CompletedTask);
        var service = new OrganizationTimeZoneService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>(),
            new OrganizationAuditService(context),
            new OrganizationTransaction(context));

        var changedZone = await service.UpdateAsync(owner.Id, organization.Id, new UpdateOrganizationTimeZoneDto
        {
            TimeZoneId = "Asia/Novosibirsk",
            ExpectedTimeZoneId = "UTC"
        });
        var normalizedUtc = await service.UpdateAsync(owner.Id, organization.Id, new UpdateOrganizationTimeZoneDto
        {
            TimeZoneId = "UTC",
            ExpectedTimeZoneId = "Asia/Novosibirsk"
        });

        Assert.Equal("Asia/Novosibirsk", changedZone.TimeZoneId);
        Assert.Equal("Etc/UTC", normalizedUtc.TimeZoneId);
    }

    [Fact]
    public async Task RejectsStaleExpectedTimeZoneWithoutOverwritingCurrentValue()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "timezone-conflict@example.test" };
        var organization = new Organization
        {
            Name = "Конфликт часового пояса",
            OwnerUser = owner,
            ShiftTimeZoneId = "Asia/Novosibirsk",
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .Returns(Task.CompletedTask);
        var service = new OrganizationTimeZoneService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>(),
            new OrganizationAuditService(context),
            new OrganizationTransaction(context));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.UpdateAsync(
            owner.Id,
            organization.Id,
            new UpdateOrganizationTimeZoneDto
            {
                TimeZoneId = "Europe/Moscow",
                ExpectedTimeZoneId = "Etc/UTC"
            }));

        Assert.Equal("Asia/Novosibirsk", await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organization.Id)
            .Select(item => item.ShiftTimeZoneId)
            .SingleAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task RequiresProfileEditPermissionAndRejectsUnknownTimeZoneWithoutChangingValue()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "timezone-permission@example.test" };
        var organization = new Organization
        {
            Name = "Проверка прав часового пояса",
            OwnerUser = owner,
            ShiftTimeZoneId = "Etc/UTC",
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ThrowsAsync(new UnauthorizedAccessException());
        var service = new OrganizationTimeZoneService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>(),
            new OrganizationAuditService(context),
            new OrganizationTransaction(context));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateAsync(
            owner.Id,
            organization.Id,
            new UpdateOrganizationTimeZoneDto
            {
                TimeZoneId = "Asia/Novosibirsk",
                ExpectedTimeZoneId = "Etc/UTC"
            }));
        subscriptions.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            owner.Id,
            organization.Id,
            OrganizationPermissionCodes.WorkspaceProfileEdit), Times.Once);

        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .Returns(Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            owner.Id,
            organization.Id,
            new UpdateOrganizationTimeZoneDto
            {
                TimeZoneId = "Mars/Olympus",
                ExpectedTimeZoneId = "Etc/UTC"
            }));

        Assert.Equal("Etc/UTC", await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organization.Id)
            .Select(item => item.ShiftTimeZoneId)
            .SingleAsync());
    }

    [Fact]
    public async Task LegacyShiftSettingsRouteDoesNotAllowShiftsManageOnly()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "timezone-legacy-shift@example.test" };
        var organization = new Organization
        {
            Name = "Совместимый маршрут часового пояса",
            OwnerUser = owner,
            ShiftTimeZoneId = "Etc/UTC",
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                owner.Id,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceProfileEdit))
            .ThrowsAsync(new UnauthorizedAccessException());
        var service = new OrganizationShiftService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>(),
            Mock.Of<IOrganizationAuditService>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetTimeZoneAsync(owner.Id, organization.Id, "Asia/Novosibirsk"));
        subscriptions.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            owner.Id,
            organization.Id,
            OrganizationPermissionCodes.WorkspaceProfileEdit), Times.Once);
        subscriptions.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            owner.Id,
            organization.Id,
            OrganizationPermissionCodes.ShiftsManage), Times.Never);
    }
}
