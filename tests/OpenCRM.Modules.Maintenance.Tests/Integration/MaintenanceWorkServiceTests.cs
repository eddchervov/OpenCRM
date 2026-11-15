using Helpers;
using OpenCRM.Testing;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Work;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceWorkServiceTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task AssignedQueryUsesOrganizationLocalDateForCompletedWorkRetention()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 10, 24, 20, 30, 0, DateTimeKind.Utc);
        var owner = new ApplicationUser();
        var worker = new ApplicationUser();
        var member = new OrganizationMember { User = worker, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = owner,
            Name = "Локальная дата выполненных работ",
            ShiftTimeZoneId = "Asia/Novosibirsk",
            Members =
            [
                new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner },
                member
            ]
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = member,
            Name = "Мастер",
            IsActive = true
        };
        var older = CreateScheduledMaintenance(
            worker,
            organization,
            employee,
            "Срок вышел",
            new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc),
            TimeSpan.FromHours(9));
        older.Status = MaintenanceStatus.Completed;
        older.CompletedDate = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        var retained = CreateScheduledMaintenance(
            worker,
            organization,
            employee,
            "В сроке",
            new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc),
            TimeSpan.FromHours(9));
        retained.Status = MaintenanceStatus.Completed;
        retained.CompletedDate = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.AddRange(older, retained);
        context.MaintenanceWorkOperations.AddRange(
            CreateOperation(older, employee, MaintenanceWorkStatus.Completed),
            CreateOperation(retained, employee, MaintenanceWorkStatus.Completed));
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                worker.Id,
                organization.Id,
                OrganizationPermissionCodes.MaintenanceWorkView))
            .Returns(Task.CompletedTask);
        var query = new MaintenanceWorkQueryService(
            context,
            subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(now));

        var assigned = await query.GetAssignedAsync(worker.Id, organization.Id);

        var item = Assert.Single(assigned);
        Assert.Equal("В сроке", item.Name);
    }

    [Fact]
    public async Task EmployeeAccessRejectsMissingMembershipAndInactiveEmployeeLinks()
    {
        await using var context = fixture.CreateContext();
        var missingMemberUser = new ApplicationUser();
        var missingMemberOrganization = new Organization
        {
            OwnerUser = missingMemberUser,
            Name = "Участник не зарегистрирован"
        };
        var inactiveUser = new ApplicationUser();
        var inactiveMember = new OrganizationMember
        {
            User = inactiveUser,
            RoleCode = OrganizationRoleCodes.Performer
        };
        var inactiveOrganization = new Organization
        {
            OwnerUser = inactiveUser,
            Name = "Сотрудник отключён",
            Members = [inactiveMember]
        };
        var inactiveEmployee = new OrganizationEmployee
        {
            Organization = inactiveOrganization,
            Member = inactiveMember,
            Name = "Отключённый мастер",
            IsActive = false
        };
        context.OrganizationEmployees.Add(inactiveEmployee);
        context.Organizations.Add(missingMemberOrganization);
        await context.SaveChangesAsync();

        var access = new MaintenanceWorkEmployeeAccess(context);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.GetEmployeeAsync(
            missingMemberUser.Id, missingMemberOrganization.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => access.GetEmployeeAsync(
            inactiveUser.Id, inactiveOrganization.Id));
    }

    [Fact]
    public async Task OnlyOnePerformerCanClaimAvailableWork()
    {
        await using var context = fixture.CreateContext();
        var firstUser = new ApplicationUser();
        var secondUser = new ApplicationUser();
        var firstMember = new OrganizationMember { User = firstUser, RoleCode = OrganizationRoleCodes.Performer };
        var secondMember = new OrganizationMember { User = secondUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            Name = "Свободная работа", OwnerUser = firstUser,
            AllowWorkSelfAssignment = true, Members = [firstMember, secondMember]
        };
        var firstEmployee = new OrganizationEmployee { Organization = organization, Member = firstMember, Name = "Первый" };
        var secondEmployee = new OrganizationEmployee { Organization = organization, Member = secondMember, Name = "Второй" };
        var detailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = firstUser,
            Vehicle = new Vehicle { User = firstUser, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Свободная работа" }]
        };
        context.OrganizationEmployees.AddRange(firstEmployee, secondEmployee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        var (query, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);
        Assert.Single(await query.GetAvailableAsync(firstUser.Id, organization.Id));
        var claimed = await execution.ClaimAsync(firstUser.Id, organization.Id, maintenance.Id, detailId);
        Assert.Equal(detailId, claimed.DetailId);
        Assert.Empty(await query.GetAvailableAsync(secondUser.Id, organization.Id));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ClaimAsync(
            secondUser.Id, organization.Id, maintenance.Id, detailId));
        Assert.Single(await query.GetAssignedAsync(firstUser.Id, organization.Id));
        Assert.Empty(await query.GetAssignedAsync(secondUser.Id, organization.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => query.GetProgressAsync(
            firstUser.Id, Guid.NewGuid(), maintenance.Id));
    }

    [Fact]
    public async Task ClaimRequiresEnabledSelfAssignmentAndAnUnassignedMaintenance()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Назначение доступной работы",
            Members = [member]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var detailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Полировка" }]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var (query, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);

        Assert.Empty(await query.GetAvailableAsync(user.Id, organization.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ClaimAsync(
            user.Id, organization.Id, maintenance.Id, detailId));

        organization.AllowWorkSelfAssignment = true;
        maintenance.EmployeeId = employee.Id;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ClaimAsync(
            user.Id, organization.Id, maintenance.Id, detailId));

        maintenance.EmployeeId = null;
        maintenance.Details.Single().EmployeeId = employee.Id;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ClaimAsync(
            user.Id, organization.Id, maintenance.Id, detailId));
    }

    [Fact]
    public async Task AvailableWorkQueryRejectsAnInactiveOrganizationAfterResolvingItsEmployee()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Неактивная организация",
            IsActive = false,
            Members = [member]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var query = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(DateTime.UtcNow));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => query.GetAvailableAsync(user.Id, organization.Id));
    }

    [Fact]
    public async Task ClaimRejectsTerminalOrdersMaterialsAssignedDetailsAndAlreadyTrackedDetails()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Недоступные работы для назначения",
            AllowWorkSelfAssignment = true,
            Members = [member]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var completedDetailId = Guid.NewGuid();
        var cancelledDetailId = Guid.NewGuid();
        var materialDetailId = Guid.NewGuid();
        var assignedDetailId = Guid.NewGuid();
        var trackedDetailId = Guid.NewGuid();
        var completed = CreateWorkOrder(user, organization, employee, completedDetailId, MaintenanceStatus.Completed);
        var cancelled = CreateWorkOrder(user, organization, employee, cancelledDetailId, MaintenanceStatus.Cancelled);
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail
                {
                    DetailId = materialDetailId,
                    Name = "Материал",
                    MaterialName = "Паста",
                    MaterialQuantity = 1
                },
                new MaintenanceDetail { DetailId = assignedDetailId, Name = "Уже назначена", EmployeeId = employee.Id },
                new MaintenanceDetail { DetailId = trackedDetailId, Name = "Уже отслеживается" }
            ]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.AddRange(completed, cancelled, maintenance);
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = trackedDetailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Assigned
        });
        await context.SaveChangesAsync();
        var (_, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ClaimAsync(
            user.Id, organization.Id, completed.Id, completedDetailId));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ClaimAsync(
            user.Id, organization.Id, cancelled.Id, cancelledDetailId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => execution.ClaimAsync(
            user.Id, organization.Id, maintenance.Id, materialDetailId));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ClaimAsync(
            user.Id, organization.Id, maintenance.Id, assignedDetailId));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ClaimAsync(
            user.Id, organization.Id, maintenance.Id, trackedDetailId));
    }

    [Fact]
    public async Task AvailableQueryOmitsAssignedTrackedAndMaterialRowsAndSortsEligibleRows()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Фильтрация доступных работ",
            AllowWorkSelfAssignment = true,
            Members = [member]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var availableZetaId = Guid.NewGuid();
        var availableAlphaId = Guid.NewGuid();
        var unscheduledId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();
        var materialId = Guid.NewGuid();
        var trackedId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            PlannedDate = DateTime.UtcNow.Date,
            PlannedStartTime = TimeSpan.FromHours(9),
            Details =
            [
                new MaintenanceDetail { DetailId = availableZetaId, Name = "Бета" },
                new MaintenanceDetail { DetailId = availableAlphaId, Name = "Альфа" },
                new MaintenanceDetail { DetailId = assignedId, Name = "Назначенная", EmployeeId = employee.Id },
                new MaintenanceDetail { DetailId = materialId, Name = "Материал", MaterialName = "Материал", MaterialQuantity = 1 },
                new MaintenanceDetail { DetailId = trackedId, Name = "Уже отслеживается" }
            ]
        };
        context.OrganizationEmployees.Add(employee);
        var unscheduled = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { DetailId = unscheduledId, Name = "Без расписания" }]
        };
        context.Maintenance.AddRange(maintenance, unscheduled);
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = trackedId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Assigned
        });
        await context.SaveChangesAsync();
        var query = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(DateTime.UtcNow));

        var available = await query.GetAvailableAsync(user.Id, organization.Id);

        Assert.Collection(available,
            item =>
            {
                Assert.Equal(availableAlphaId, item.DetailId);
                Assert.Equal("Альфа", item.Name);
            },
            item =>
            {
                Assert.Equal(availableZetaId, item.DetailId);
                Assert.Equal("Бета", item.Name);
            },
            item =>
            {
                Assert.Equal(unscheduledId, item.DetailId);
                Assert.Equal("Без расписания", item.Name);
            });
    }

    [Fact]
    public async Task AssignedQueryIncludesOnlyCompletedWorkWithinTheRecentWindow()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "Окно завершённых работ", Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var recentDetailId = Guid.NewGuid();
        var oldDetailId = Guid.NewGuid();
        var unfinishedDetailId = Guid.NewGuid();
        var recent = CreateCompletedMaintenance(user, organization, employee, recentDetailId, now.AddDays(-1));
        var old = CreateCompletedMaintenance(user, organization, employee, oldDetailId, now.AddDays(-3));
        var unfinished = CreateCompletedMaintenance(user, organization, employee, unfinishedDetailId, now);
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.AddRange(recent, old, unfinished);
        context.MaintenanceWorkOperations.AddRange(
            new MaintenanceWorkOperation
            {
                Id = recentDetailId,
                Maintenance = recent,
                OrganizationId = organization.Id,
                EmployeeId = employee.Id,
                Status = MaintenanceWorkStatus.Completed
            },
            new MaintenanceWorkOperation
            {
                Id = unfinishedDetailId,
                Maintenance = unfinished,
                OrganizationId = organization.Id,
                EmployeeId = employee.Id,
                Status = MaintenanceWorkStatus.Assigned
            });
        await context.SaveChangesAsync();
        var query = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(now));

        var assigned = await query.GetAssignedAsync(user.Id, organization.Id);

        var result = Assert.Single(assigned);
        Assert.Equal(recentDetailId, result.DetailId);
        Assert.Equal(MaintenanceWorkStatus.Completed, result.Status);
    }

    [Fact]
    public async Task AssignedQueryUsesOrderAssignmentAndSkipsUnfinishedCompletedMaterialAndForeignRows()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Назначение на уровне заказа",
            Members = [member, otherMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер заказа" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой мастер" };
        var plannedDetailId = Guid.NewGuid();
        var materialDetailId = Guid.NewGuid();
        var foreignDetailId = Guid.NewGuid();
        var unfinishedCompletedDetailId = Guid.NewGuid();
        var planned = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Status = MaintenanceStatus.Planned,
            Date = now.Date,
            PlannedDate = now.Date,
            Details =
            [
                new MaintenanceDetail { DetailId = plannedDetailId, Name = "Работа заказа" },
                new MaintenanceDetail
                {
                    DetailId = materialDetailId,
                    Name = "Материал",
                    MaterialName = "Паста",
                    MaterialQuantity = 1
                },
                new MaintenanceDetail
                {
                    DetailId = foreignDetailId,
                    Name = "Работа другого мастера",
                    EmployeeId = otherEmployee.Id
                }
            ]
        };
        var completedWithoutOperation = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Status = MaintenanceStatus.Completed,
            Date = now.Date,
            CompletedDate = now,
            Details = [new MaintenanceDetail { DetailId = unfinishedCompletedDetailId, Name = "Не завершённая работа" }]
        };
        context.OrganizationEmployees.AddRange(employee, otherEmployee);
        context.Maintenance.AddRange(planned, completedWithoutOperation);
        await context.SaveChangesAsync();
        var query = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(now));

        var assigned = await query.GetAssignedAsync(user.Id, organization.Id);

        var item = Assert.Single(assigned);
        Assert.Equal(plannedDetailId, item.DetailId);
        Assert.Equal("Работа заказа", item.Name);
    }

    [Fact]
    public async Task AssignedQueryPrioritizesActiveWorkThenOrdersByScheduleAndName()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "Порядок работ", Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var early = CreateScheduledMaintenance(user, organization, employee, "Ранняя", now.Date, TimeSpan.FromHours(8));
        var laterDate = CreateScheduledMaintenance(user, organization, employee, "Поздняя дата", now.Date.AddDays(1), TimeSpan.FromHours(7));
        var beta = CreateScheduledMaintenance(user, organization, employee, "Бета", now.Date, TimeSpan.FromHours(9));
        var alpha = CreateScheduledMaintenance(user, organization, employee, "Альфа", now.Date, TimeSpan.FromHours(9));
        var unscheduled = CreateScheduledMaintenance(user, organization, employee, "Без расписания", now.Date, TimeSpan.FromHours(9));
        unscheduled.PlannedDate = null;
        unscheduled.PlannedStartTime = null;
        var withoutStartTime = CreateScheduledMaintenance(user, organization, employee, "Без времени", now.Date, TimeSpan.FromHours(9));
        withoutStartTime.PlannedStartTime = null;
        var assigned = CreateScheduledMaintenance(user, organization, employee, "Назначена", now.Date, TimeSpan.FromHours(6));
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.AddRange(early, laterDate, beta, alpha, unscheduled, withoutStartTime, assigned);
        context.MaintenanceWorkOperations.AddRange(
            CreateOperation(early, employee, MaintenanceWorkStatus.InProgress),
            CreateOperation(laterDate, employee, MaintenanceWorkStatus.Paused),
            CreateOperation(beta, employee, MaintenanceWorkStatus.InProgress),
            CreateOperation(alpha, employee, MaintenanceWorkStatus.Paused),
            CreateOperation(unscheduled, employee, MaintenanceWorkStatus.Assigned),
            CreateOperation(withoutStartTime, employee, MaintenanceWorkStatus.Assigned),
            CreateOperation(assigned, employee, MaintenanceWorkStatus.Assigned));
        await context.SaveChangesAsync();
        var query = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(now));

        var assignedItems = await query.GetAssignedAsync(user.Id, organization.Id);

        Assert.Collection(assignedItems,
            item => Assert.Equal("Ранняя", item.Name),
            item => Assert.Equal("Альфа", item.Name),
            item => Assert.Equal("Бета", item.Name),
            item => Assert.Equal("Поздняя дата", item.Name),
            item => Assert.Equal("Назначена", item.Name),
            item => Assert.Equal("Без времени", item.Name),
            item => Assert.Equal("Без расписания", item.Name));
    }

    [Fact]
    public async Task ProgressAndReportQueriesUseMaintenanceLevelEmployeeAssignment()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Назначение на заказ",
            Members = [member, otherMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой мастер" };
        var detailId = Guid.NewGuid();
        var materialId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Performer = "Исполнитель заказа",
            Status = MaintenanceStatus.InProgress,
            Date = now.Date,
            Details =
            [
                new MaintenanceDetail { DetailId = detailId, Name = "Полировка" },
                new MaintenanceDetail { DetailId = materialId, Name = "Материал", MaterialName = "Паста", MaterialQuantity = 1 }
            ]
        };
        context.OrganizationEmployees.AddRange(employee, otherEmployee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var (query, _, _, _) = CreateWorkServices(context, now);

        var progress = Assert.Single(await query.GetProgressAsync(user.Id, organization.Id, maintenance.Id));
        var reports = await query.GetReportsAsync(user.Id, organization.Id, maintenance.Id, detailId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => query.GetReportsAsync(
            otherUser.Id, organization.Id, maintenance.Id, detailId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => query.GetReportsAsync(
            user.Id, organization.Id, maintenance.Id, materialId));

        Assert.Equal(detailId, progress.DetailId);
        Assert.Equal("Исполнитель заказа", progress.EmployeeName);
        Assert.Equal(MaintenanceWorkStatus.Assigned, (MaintenanceWorkStatus)progress.Status);
        Assert.Equal(0u, progress.Version);
        Assert.Equal(TimeSpan.Zero, progress.ActiveTime);
        Assert.Null(progress.PauseReason);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task ReportsUsePlaceholderNameWhenHistoricalEmployeeIsMissing()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { OwnerUser = user, Name = "Исторический исполнитель" };
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа" }]
        };
        var report = new MaintenanceWorkReport
        {
            CommandId = Guid.NewGuid(),
            OrganizationId = organization.Id,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            DetailId = maintenance.Details.Single().DetailId,
            EmployeeId = Guid.NewGuid(),
            MemberId = Guid.NewGuid(),
            ActorUserId = user.Id,
            Kind = "comment",
            Text = "Историческое замечание",
            CreatedAt = DateTime.UtcNow
        };
        context.Maintenance.Add(maintenance);
        context.MaintenanceWorkReports.Add(report);
        await context.SaveChangesAsync();
        var query = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(DateTime.UtcNow));

        var result = Assert.Single(await query.GetReportsAsync(user.Id, organization.Id, maintenance.Id));

        Assert.Equal("Исполнитель", result.EmployeeName);
    }

    [Fact]
    public async Task ProductionReportNetsMaterialReturnsAndHidesCostsWithoutFinancePermission()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser();
        var organization = new Organization { OwnerUser = user, Name = "Материалы производства" };
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = now.Date,
            DiscountAmount = 100,
            Details = [new MaintenanceDetail { Name = "Работа", Price = 1000 }]
        };
        var stock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Полировальная паста",
            Unit = "шт"
        };
        var returnedStock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Полностью возвращённый материал",
            Unit = "л"
        };
        var sortedStock = new OrganizationMaterialStock
        {
            Organization = organization,
            Name = "Тряпка",
            Unit = "шт"
        };
        context.Maintenance.Add(maintenance);
        context.OrganizationMaterialMovements.AddRange(
            CreateMaintenanceMovement(organization, maintenance, stock, -2, 50),
            CreateMaintenanceMovement(organization, maintenance, stock, 1, 25),
            CreateMaintenanceMovement(organization, maintenance, returnedStock, -1, 20),
            CreateMaintenanceMovement(organization, maintenance, returnedStock, 1, 20),
            CreateMaintenanceMovement(organization, maintenance, sortedStock, -1, 0));
        await context.SaveChangesAsync();
        var query = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(),
            CreateClock(now));

        var hiddenFinance = await query.GetProductionReportAsync(user.Id, organization.Id, maintenance.Id);

        Assert.Collection(hiddenFinance.Materials,
            material =>
            {
                Assert.Equal("Полировальная паста", material.Name);
                Assert.Equal(1m, material.Quantity);
                Assert.Null(material.Cost);
            },
            material =>
            {
                Assert.Equal("Тряпка", material.Name);
                Assert.Equal(1m, material.Quantity);
                Assert.Null(material.Cost);
            });
        Assert.Null(hiddenFinance.MaterialCostAmount);
        Assert.Null(hiddenFinance.GrossMarginAmount);

        var financePermissions = new Mock<IOrganizationPermissionService>();
        financePermissions.Setup(service => service.HasPermissionAsync(
                user.Id, organization.Id, OrganizationPermissionCodes.FinanceView))
            .ReturnsAsync(true);
        var financeQuery = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            financePermissions.Object,
            CreateClock(now));
        var visibleFinance = await financeQuery.GetProductionReportAsync(user.Id, organization.Id, maintenance.Id);

        Assert.Equal(25m, visibleFinance.MaterialCostAmount);
        Assert.Collection(visibleFinance.Materials,
            material => Assert.Equal(25m, material.Cost),
            material => Assert.Equal(0m, material.Cost));
        Assert.Equal(900m, visibleFinance.RevenueAmount);

        maintenance.Status = MaintenanceStatus.Cancelled;
        await context.SaveChangesAsync();
        Assert.Equal(0m, (await financeQuery.GetProductionReportAsync(user.Id, organization.Id, maintenance.Id)).RevenueAmount);

        maintenance.Status = MaintenanceStatus.InProgress;
        maintenance.DiscountAmount = 1001m;
        await context.SaveChangesAsync();
        Assert.Equal(0m, (await financeQuery.GetProductionReportAsync(user.Id, organization.Id, maintenance.Id)).RevenueAmount);
    }

    [Fact]
    public async Task AddReportValidatesPayloadAndRejectsAWorkAssignedToAnotherEmployee()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "Доступ к отчётам", Members = [member, otherMember] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Назначенный" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой" };
        var detailId = Guid.NewGuid();
        var orderAssignedDetailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail { DetailId = detailId, Name = "Полировка", EmployeeId = employee.Id },
                new MaintenanceDetail { DetailId = orderAssignedDetailId, Name = "Работа по назначению заказа" }
            ]
        };
        context.OrganizationEmployees.AddRange(employee, otherEmployee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var subscriptions = Mock.Of<IOrganizationSubscriptionService>();
        var reports = new MaintenanceWorkReportService(
            context, subscriptions, new MaintenancePlanAccess(subscriptions), CreateClock(DateTime.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.Empty, Kind = "comment", Text = "Текст" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "comment", Text = " " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "unknown", Text = "Текст" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = Guid.NewGuid(), Kind = "comment", Text = new string('т', 2001)
            }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.AddReportAsync(
            otherUser.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "comment", Text = "Замечание" }));
        await reports.AddReportAsync(user.Id, organization.Id, maintenance.Id, orderAssignedDetailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = Guid.NewGuid(),
                Kind = "comment",
                Text = "Работа назначена на заказ"
            });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, Guid.NewGuid(),
            new CreateMaintenanceWorkReportDto
            {
                CommandId = Guid.NewGuid(),
                Kind = "comment",
                Text = "Несуществующая работа"
            }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reports.AddReportAsync(
            otherUser.Id, organization.Id, maintenance.Id, orderAssignedDetailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = Guid.NewGuid(),
                Kind = "comment",
                Text = "Работа назначена другому исполнителю"
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = null!, Text = null! }));

        maintenance.Status = MaintenanceStatus.Completed;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "comment", Text = "Позднее замечание" }));
        maintenance.Status = MaintenanceStatus.Cancelled;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "comment", Text = "Замечание после отмены" }));
    }

    [Fact]
    public async Task AddReportCommandIdempotencyIsBoundToOrganizationMaintenanceDetailActorKindAndText()
    {
        await using var context = fixture.CreateContext();
        var firstUser = new ApplicationUser();
        var secondUser = new ApplicationUser();
        var otherOrganizationUser = new ApplicationUser();
        var firstMember = new OrganizationMember { User = firstUser, RoleCode = OrganizationRoleCodes.Performer };
        var secondMember = new OrganizationMember { User = secondUser, RoleCode = OrganizationRoleCodes.Performer };
        var otherOrganizationMember = new OrganizationMember
        {
            User = otherOrganizationUser,
            RoleCode = OrganizationRoleCodes.Performer
        };
        var organization = new Organization
        {
            OwnerUser = firstUser,
            Name = "Идемпотентность отчётов",
            Members = [firstMember, secondMember]
        };
        var otherOrganization = new Organization
        {
            OwnerUser = otherOrganizationUser,
            Name = "Другая организация отчётов",
            Members = [otherOrganizationMember]
        };
        var firstEmployee = new OrganizationEmployee
        {
            Organization = organization,
            Member = firstMember,
            Name = "Первый исполнитель"
        };
        var secondEmployee = new OrganizationEmployee
        {
            Organization = organization,
            Member = secondMember,
            Name = "Второй исполнитель"
        };
        var otherOrganizationEmployee = new OrganizationEmployee
        {
            Organization = otherOrganization,
            Member = otherOrganizationMember,
            Name = "Исполнитель другой организации"
        };
        var firstDetailId = Guid.NewGuid();
        var secondDetailId = Guid.NewGuid();
        var otherMaintenanceDetailId = Guid.NewGuid();
        var otherOrganizationDetailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = firstUser,
            Vehicle = new Vehicle { User = firstUser, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail
                {
                    DetailId = firstDetailId,
                    Name = "Первая работа",
                    EmployeeId = firstEmployee.Id
                },
                new MaintenanceDetail
                {
                    DetailId = secondDetailId,
                    Name = "Вторая работа",
                    EmployeeId = firstEmployee.Id
                }
            ]
        };
        var otherMaintenance = new MaintenanceEntity
        {
            User = firstUser,
            Vehicle = new Vehicle { User = firstUser, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail
                {
                    DetailId = otherMaintenanceDetailId,
                    Name = "Работа другого обслуживания",
                    EmployeeId = firstEmployee.Id
                }
            ]
        };
        var otherOrganizationMaintenance = new MaintenanceEntity
        {
            User = otherOrganizationUser,
            Vehicle = new Vehicle { User = otherOrganizationUser, Organization = otherOrganization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail
                {
                    DetailId = otherOrganizationDetailId,
                    Name = "Работа другой организации",
                    EmployeeId = otherOrganizationEmployee.Id
                }
            ]
        };
        context.OrganizationEmployees.AddRange(firstEmployee, secondEmployee, otherOrganizationEmployee);
        context.Maintenance.AddRange(maintenance, otherMaintenance, otherOrganizationMaintenance);
        await context.SaveChangesAsync();
        var subscriptions = Mock.Of<IOrganizationSubscriptionService>();
        var reports = new MaintenanceWorkReportService(
            context, subscriptions, new MaintenancePlanAccess(subscriptions), CreateClock(DateTime.UtcNow));
        var commandId = Guid.NewGuid();
        var command = new CreateMaintenanceWorkReportDto
        {
            CommandId = commandId,
            Kind = "comment",
            Text = "Проверить результат"
        };

        await reports.AddReportAsync(firstUser.Id, organization.Id, maintenance.Id, firstDetailId, command);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            firstUser.Id, organization.Id, otherMaintenance.Id, otherMaintenanceDetailId, command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            firstUser.Id, organization.Id, maintenance.Id, secondDetailId, command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            otherOrganizationUser.Id, otherOrganization.Id, otherOrganizationMaintenance.Id,
            otherOrganizationDetailId, command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            secondUser.Id, organization.Id, maintenance.Id, firstDetailId, command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            firstUser.Id, organization.Id, maintenance.Id, firstDetailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = commandId,
                Kind = "defect",
                Text = command.Text
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            firstUser.Id, organization.Id, maintenance.Id, firstDetailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = commandId,
                Kind = command.Kind,
                Text = "Другой текст"
            }));

        Assert.Single(await context.MaintenanceWorkReports.Where(item => item.CommandId == commandId).ToListAsync());
    }

    [Fact]
    public async Task ExecuteValidatesIdentifiersTransitionsPauseReasonAndCommandIdempotency()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "Переходы работы", Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var detailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Полировка", EmployeeId = employee.Id }]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var (_, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { Action = "start" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, Guid.Empty,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "start" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "unknown", Version = 0 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = null!, Version = 0 }));

        var startCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(),
            Action = "start",
            Version = 0
        };
        var started = await execution.ExecuteAsync(user.Id, organization.Id, maintenance.Id, detailId, startCommand);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = startCommand.CommandId,
                Action = "unknown",
                Version = startCommand.Version
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "pause",
                Version = started.Version
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "pause",
                Reason = new string('п', 1001),
                Version = started.Version
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = startCommand.CommandId,
                Action = "pause",
                Reason = "Пауза",
                Version = startCommand.Version
            }));
    }

    [Fact]
    public async Task ExecuteCommandIdempotencyIsBoundToOperationActorAndPayload()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Повтор команд выполнения",
            Members = [member, otherMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Первый мастер" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Второй мастер" };
        var firstDetailId = Guid.NewGuid();
        var secondDetailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail { DetailId = firstDetailId, Name = "Первая работа", EmployeeId = employee.Id },
                new MaintenanceDetail { DetailId = secondDetailId, Name = "Вторая работа", EmployeeId = employee.Id }
            ]
        };
        context.OrganizationEmployees.AddRange(employee, otherEmployee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var (_, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);
        var startCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(),
            Action = "start",
            Version = 0
        };

        var started = await execution.ExecuteAsync(user.Id, organization.Id, maintenance.Id, firstDetailId, startCommand);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, secondDetailId, startCommand));

        maintenance.Details.Single(detail => detail.DetailId == firstDetailId).EmployeeId = otherEmployee.Id;
        maintenance.Details.Single(detail => detail.DetailId == firstDetailId).EmployeeName = otherEmployee.Name;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            otherUser.Id, organization.Id, maintenance.Id, firstDetailId, startCommand));

        maintenance.Details.Single(detail => detail.DetailId == firstDetailId).EmployeeId = employee.Id;
        maintenance.Details.Single(detail => detail.DetailId == firstDetailId).EmployeeName = employee.Name;
        await context.SaveChangesAsync();
        var pauseCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(),
            Action = "pause",
            Reason = "Ожидание",
            Version = started.Version
        };
        var paused = await execution.ExecuteAsync(user.Id, organization.Id, maintenance.Id, firstDetailId, pauseCommand);
        var resumeCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(),
            Action = "resume",
            Version = paused.Version
        };
        var resumed = await execution.ExecuteAsync(user.Id, organization.Id, maintenance.Id, firstDetailId, resumeCommand);

        Assert.Equal(resumed.Version, (await execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId, resumeCommand)).Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = resumeCommand.CommandId,
                Action = "start",
                Version = resumeCommand.Version
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = resumeCommand.CommandId,
                Action = resumeCommand.Action,
                Version = resumeCommand.Version + 1
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = resumeCommand.CommandId,
                Action = null!,
                Version = resumeCommand.Version
            }));
        foreach (var action in new[] { "sweep", "pasta", "resort", "compiler" })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
                user.Id, organization.Id, maintenance.Id, firstDetailId,
                new MaintenanceWorkCommandDto
                {
                    CommandId = resumeCommand.CommandId,
                    Action = action,
                    Version = resumeCommand.Version
                }));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = resumeCommand.CommandId,
                Action = resumeCommand.Action,
                Reason = "Другая причина",
                Version = resumeCommand.Version
            }));
    }

    [Fact]
    public async Task ExecuteRejectsOrdersUnavailableForWorkAndOperationsOwnedByAnotherOrder()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "Защита выполнения", Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var now = DateTime.UtcNow;
        var completedDetailId = Guid.NewGuid();
        var cancelledDetailId = Guid.NewGuid();
        var readyDetailId = Guid.NewGuid();
        var handedOverDetailId = Guid.NewGuid();
        var sharedDetailId = Guid.NewGuid();
        var completed = CreateWorkOrder(user, organization, employee, completedDetailId, MaintenanceStatus.Completed);
        var cancelled = CreateWorkOrder(user, organization, employee, cancelledDetailId, MaintenanceStatus.Cancelled);
        var ready = CreateWorkOrder(user, organization, employee, readyDetailId, MaintenanceStatus.Completed);
        ready.ReadyForHandoverAt = now;
        var handedOver = CreateWorkOrder(user, organization, employee, handedOverDetailId, MaintenanceStatus.Completed);
        handedOver.ReadyForHandoverAt = now;
        handedOver.HandedOverAt = now;
        var operationOwner = CreateWorkOrder(user, organization, employee, sharedDetailId, MaintenanceStatus.InProgress);
        var operationTarget = CreateWorkOrder(user, organization, employee, sharedDetailId, MaintenanceStatus.InProgress);
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.AddRange(completed, cancelled, ready, handedOver, operationOwner, operationTarget);
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = sharedDetailId,
            Maintenance = operationOwner,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Assigned
        });
        await context.SaveChangesAsync();
        var (_, execution, _, _) = CreateWorkServices(context, now);

        foreach (var (maintenance, detailId) in new[]
        {
            (completed, completedDetailId),
            (cancelled, cancelledDetailId),
            (ready, readyDetailId),
            (handedOver, handedOverDetailId)
        })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
                user.Id, organization.Id, maintenance.Id, detailId,
                new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "start", Version = 0 }));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, operationTarget.Id, sharedDetailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "start", Version = 0 }));
    }

    [Fact]
    public async Task ExecuteUsesOrderAssignmentAndRejectsPausingWithoutAnOpenInterval()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Работа назначена на заказ",
            Members = [member, otherMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой мастер" };
        var detailId = Guid.NewGuid();
        var otherDetailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            EmployeeId = employee.Id,
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail { DetailId = detailId, Name = "Работа без интервала" },
                new MaintenanceDetail { DetailId = otherDetailId, Name = "Работа по назначению заказа" }
            ]
        };
        context.OrganizationEmployees.AddRange(employee, otherEmployee);
        context.Maintenance.Add(maintenance);
        var operation = new MaintenanceWorkOperation
        {
            Id = detailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        };
        context.MaintenanceWorkOperations.Add(operation);
        await context.SaveChangesAsync();
        var (_, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution.ExecuteAsync(
            otherUser.Id, organization.Id, maintenance.Id, otherDetailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "start",
                Version = 0
            }));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "pause",
                Reason = "Ожидание",
                Version = operation.Version
            }));

        Assert.Equal("Открытый интервал работы не найден.", exception.Message);
    }

    [Fact]
    public async Task WorkCommandsAndQueriesReturnNotFoundForMissingOrderOrWork()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "Отсутствующая работа", Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        context.OrganizationEmployees.Add(employee);
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = "Полировка" }]
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var (query, execution, reports, time) = CreateWorkServices(context, DateTime.UtcNow);
        var missingMaintenanceId = Guid.NewGuid();
        var detailId = Guid.NewGuid();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => query.GetProgressAsync(
            user.Id, organization.Id, missingMaintenanceId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => query.GetProductionReportAsync(
            user.Id, organization.Id, missingMaintenanceId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => query.GetReportsAsync(
            user.Id, organization.Id, missingMaintenanceId, detailId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => execution.ClaimAsync(
            user.Id, organization.Id, missingMaintenanceId, detailId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, missingMaintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "start" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, missingMaintenanceId, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Reason = "Возврат" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reports.AddReportAsync(
            user.Id, organization.Id, missingMaintenanceId, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "comment", Text = "Замечание" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reports.DecideReportAsync(
            user.Id, organization.Id, missingMaintenanceId, Guid.NewGuid(),
            new DecideMaintenanceWorkReportDto { Decision = "rejected", Reason = "Причина" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => time.AdjustTimeAsync(
            user.Id, organization.Id, missingMaintenanceId, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 1, Reason = "Поправка" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reports.DecideReportAsync(
            user.Id, organization.Id, maintenance.Id, Guid.NewGuid(),
            new DecideMaintenanceWorkReportDto { Decision = "rejected", Reason = "Нет запроса" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => execution.ExecuteAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "start" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Reason = "Нет работы" }));
    }

    [Fact]
    public async Task ReturnForReworkCommandIdempotencyIsBoundToEveryCommandIdentityField()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var otherUser = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Повтор возврата работы",
            Members = [member, otherMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var firstDetailId = Guid.NewGuid();
        var secondDetailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details =
            [
                new MaintenanceDetail { DetailId = firstDetailId, Name = "Первая работа", EmployeeId = employee.Id },
                new MaintenanceDetail { DetailId = secondDetailId, Name = "Вторая работа", EmployeeId = employee.Id }
            ]
        };
        var firstOperation = new MaintenanceWorkOperation
        {
            Id = firstDetailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Assigned
        };
        var secondOperation = new MaintenanceWorkOperation
        {
            Id = secondDetailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Assigned
        };
        var commandId = Guid.NewGuid();
        var workEvent = new MaintenanceWorkEvent
        {
            Operation = firstOperation,
            CommandId = commandId,
            OrganizationId = organization.Id,
            MaintenanceId = maintenance.Id,
            EmployeeId = employee.Id,
            MemberId = member.Id,
            ActorUserId = user.Id,
            PreviousStatus = MaintenanceWorkStatus.Completed,
            Status = MaintenanceWorkStatus.Assigned,
            CreatedAt = DateTime.UtcNow,
            Reason = "Нужно повторить этап",
            PreviousVersion = 4,
            ResultVersion = 5
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        context.MaintenanceWorkOperations.AddRange(firstOperation, secondOperation);
        context.MaintenanceWorkEvents.Add(workEvent);
        await context.SaveChangesAsync();
        var (_, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);
        var command = new MaintenanceWorkCommandDto
        {
            CommandId = commandId,
            Action = "return",
            Reason = "Нужно повторить этап",
            Version = 4
        };

        await execution.ReturnForReworkAsync(user.Id, organization.Id, maintenance.Id, firstDetailId, command);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, secondDetailId, command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            otherUser.Id, organization.Id, maintenance.Id, firstDetailId, command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = commandId,
                Action = command.Action,
                Reason = command.Reason,
                Version = command.Version + 1
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = commandId,
                Action = command.Action,
                Reason = "Другая причина",
                Version = command.Version
            }));

        workEvent.PreviousStatus = MaintenanceWorkStatus.Assigned;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId, command));
        workEvent.PreviousStatus = MaintenanceWorkStatus.Completed;
        workEvent.Status = MaintenanceWorkStatus.Completed;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, firstDetailId, command));
    }

    [Fact]
    public async Task ReturnForReworkRejectsStaleVersionBeforeChangingCompletedWork()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var member = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization { OwnerUser = user, Name = "Устаревший возврат", Members = [member] };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var detailId = Guid.NewGuid();
        var completedAt = DateTime.UtcNow.AddMinutes(-1);
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Завершённая работа", EmployeeId = employee.Id }]
        };
        var operation = new MaintenanceWorkOperation
        {
            Id = detailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Completed,
            CompletedAt = completedAt,
            Version = 5
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        context.MaintenanceWorkOperations.Add(operation);
        await context.SaveChangesAsync();
        var savedVersion = operation.Version;
        var (_, execution, _, _) = CreateWorkServices(context, DateTime.UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "return",
                Reason = "Повторить работу",
                Version = savedVersion - 1
            }));

        Assert.Equal(MaintenanceWorkStatus.Completed, operation.Status);
        Assert.Equal(savedVersion, operation.Version);
        Assert.Equal(completedAt, operation.CompletedAt);
        Assert.Empty(await context.MaintenanceWorkEvents.Where(item => item.OperationId == detailId).ToListAsync());

        operation.Status = MaintenanceWorkStatus.InProgress;
        await context.SaveChangesAsync();
        var inProgressVersion = operation.Version;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ReturnForReworkAsync(
            user.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "return",
                Reason = "Работа ещё выполняется",
                Version = inProgressVersion
            }));

        Assert.Equal(MaintenanceWorkStatus.InProgress, operation.Status);
        Assert.Equal(inProgressVersion, operation.Version);
        Assert.Empty(await context.MaintenanceWorkEvents.Where(item => item.OperationId == detailId).ToListAsync());
    }

    [Fact]
    public async Task CommandsKeepHistoryAndRejectStaleVersionAndForeignAssignment()
    {
        await using var context = fixture.CreateContext();
        var now = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc);
        var worker = new ApplicationUser();
        var otherWorker = new ApplicationUser();
        var member = new OrganizationMember { User = worker, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherWorker, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            Name = "Проверка операций",
            OwnerUser = worker,
            Members = [member, otherMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой мастер" };
        var vehicle = new Vehicle { User = worker, Organization = organization };
        var detailId = Guid.NewGuid();
        var maintenance = new MaintenanceEntity
        {
            User = worker,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            Date = now.Date,
            PlannedDate = now.Date,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Полировка", EmployeeId = employee.Id }]
        };
        context.OrganizationEmployees.AddRange(employee, otherEmployee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        var (query, execution, reports, time) = CreateWorkServices(context, now);
        var startCommand = new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "start", Version = 0 };
        var started = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, startCommand);
        Assert.Equal(MaintenanceWorkStatus.InProgress, started.Status);
        Assert.Equal(MaintenanceStatus.InProgress, (await context.Maintenance.SingleAsync(item => item.Id == maintenance.Id)).Status);

        var pauseCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(), Action = "pause", Reason = "Ожидаем материал", Version = started.Version
        };
        var paused = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, pauseCommand);
        Assert.Equal(MaintenanceWorkStatus.Paused, paused.Status);
        var repeatedPause = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, pauseCommand);
        Assert.Equal(paused.Version, repeatedPause.Version);

        var timeCommand = new AdjustMaintenanceWorkTimeDto
        {
            CommandId = Guid.NewGuid(), Minutes = 15, Reason = "Уточнение времени"
        };
        var adjustment = await time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId, timeCommand);
        var repeatedAdjustment = await time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId, timeCommand);
        Assert.Equal(adjustment.Id, repeatedAdjustment.Id);
        Assert.Equal(15, adjustment.TimeAdjustmentMinutes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 0, Reason = "Нулевое изменение" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.Empty, Minutes = 1, Reason = "Без идентификатора" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 10081, Reason = "Слишком много" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = -10081, Reason = "Слишком мало" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 1, Reason = "   " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 1, Reason = null! }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 1, Reason = new string('п', 1001) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto
            {
                CommandId = timeCommand.CommandId, Minutes = 10, Reason = timeCommand.Reason
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new AdjustMaintenanceWorkTimeDto
            {
                CommandId = Guid.NewGuid(), Minutes = -16, Reason = "Слишком большое уменьшение"
            }));

        var progress = Assert.Single(await query.GetProgressAsync(worker.Id, organization.Id, maintenance.Id));
        Assert.Equal(TimeSpan.FromMinutes(15), progress.ActiveTime);
        Assert.Equal("Ожидаем материал", progress.PauseReason);

        var repeatedStart = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, startCommand);
        Assert.Equal(MaintenanceWorkStatus.InProgress, repeatedStart.Status);
        Assert.Equal(started.Version, repeatedStart.Version);
        Assert.Equal(2, await context.MaintenanceWorkEvents.CountAsync(item => item.OperationId == detailId));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ExecuteAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "resume", Version = started.Version }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution.ExecuteAsync(
            otherWorker.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "resume", Version = paused.Version }));
        Assert.Single(await query.GetAssignedAsync(worker.Id, organization.Id));
        Assert.Empty(await query.GetAssignedAsync(otherWorker.Id, organization.Id));

        var resumeCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(), Action = "resume", Version = paused.Version
        };
        var resumed = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, resumeCommand);
        var repeatedResume = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, resumeCommand);
        Assert.Equal(resumed.Version, repeatedResume.Version);
        var completeCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(), Action = "complete", Version = resumed.Version
        };
        var completed = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, completeCommand);
        var repeatedCompletion = await execution.ExecuteAsync(worker.Id, organization.Id, maintenance.Id, detailId, completeCommand);
        Assert.Equal(completed.Version, repeatedCompletion.Version);
        var reworkCommand = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(), Action = "return", Reason = "Нужна повторная полировка", Version = completed.Version
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto { CommandId = Guid.NewGuid(), Action = "return", Version = completed.Version }));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.ReturnForReworkAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "return",
                Reason = "Устаревшая команда",
                Version = completed.Version - 1
            }));
        await execution.ReturnForReworkAsync(worker.Id, organization.Id, maintenance.Id, detailId, reworkCommand);
        await execution.ReturnForReworkAsync(worker.Id, organization.Id, maintenance.Id, detailId, reworkCommand);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = reworkCommand.CommandId,
                Action = "return",
                Reason = "Иная причина",
                Version = reworkCommand.Version
            }));
        Assert.Equal(MaintenanceWorkStatus.Assigned,
            (await context.MaintenanceWorkOperations.SingleAsync(item => item.Id == detailId)).Status);
        Assert.Equal(5, await context.MaintenanceWorkEvents.CountAsync(item => item.OperationId == detailId));
        Assert.False(await context.MaintenanceWorkIntervals.AnyAsync(item => item.OperationId == detailId && item.EndedAt == null));
        var reworkItem = Assert.Single(await query.GetAssignedAsync(worker.Id, organization.Id));
        Assert.Equal("Нужна повторная полировка", reworkItem.ReworkReason);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => time.AdjustTimeAsync(
            worker.Id, organization.Id, maintenance.Id, Guid.NewGuid(),
            new AdjustMaintenanceWorkTimeDto { CommandId = Guid.NewGuid(), Minutes = 1, Reason = "Нет начатой работы" }));

        var extraCommand = new CreateMaintenanceWorkReportDto
        {
            CommandId = Guid.NewGuid(), Kind = "extra", Text = "Повторная защита покрытия"
        };
        var request = await reports.AddReportAsync(worker.Id, organization.Id, maintenance.Id, detailId, extraCommand);
        var repeatedRequest = await reports.AddReportAsync(worker.Id, organization.Id, maintenance.Id, detailId, extraCommand);
        Assert.Equal(request.Id, repeatedRequest.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.AddReportAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = extraCommand.CommandId, Kind = "extra", Text = "Другой текст"
            }));
        Assert.Single(await context.MaintenanceWorkReports.Where(item => item.Kind == "extra" && item.MaintenanceId == maintenance.Id).ToListAsync());

        var vatError = await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, request.Id,
            new DecideMaintenanceWorkReportDto { Decision = "approved", Price = 4200 }));

        Assert.Contains("Укажите ставку НДС", vatError.Message);
        Assert.Null((await context.MaintenanceWorkReports.SingleAsync(report => report.Id == request.Id)).Decision);

        var approved = await reports.DecideReportAsync(worker.Id, organization.Id, maintenance.Id, request.Id,
            new DecideMaintenanceWorkReportDto { Decision = "approved", Price = 4200, VatRateCode = MaintenanceVatRates.WithoutVat });
        Assert.Equal("approved", approved.Decision);
        var reportList = await query.GetReportsAsync(worker.Id, organization.Id, maintenance.Id, detailId);
        Assert.Contains(reportList, report => report.Id == request.Id && report.EmployeeName == employee.Name);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => query.GetReportsAsync(
            otherWorker.Id, organization.Id, maintenance.Id, detailId));

        var rejectedRequest = await reports.AddReportAsync(worker.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = Guid.NewGuid(), Kind = "extra", Text = "Дополнительная проверка"
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = "rejected" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = "approved", Price = -1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = "approved", Price = 1.005m, VatRateCode = MaintenanceVatRates.WithoutVat }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = "unsupported" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = null! }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = "approved", Price = 1_000_000_001 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = "rejected", Reason = new string('п', 1001) }));
        var rejected = await reports.DecideReportAsync(worker.Id, organization.Id, maintenance.Id, rejectedRequest.Id,
            new DecideMaintenanceWorkReportDto { Decision = "rejected", Reason = "Не согласовано" });
        Assert.Equal("rejected", rejected.Decision);
        Assert.Equal("Не согласовано", rejected.DecisionReason);
        Assert.Null((await context.MaintenanceWorkReports.SingleAsync(report => report.Id == rejected.Id)).ApprovedDetailId);

        var inactiveEmployeeRequest = await reports.AddReportAsync(worker.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = Guid.NewGuid(), Kind = "extra", Text = "Исполнитель отключён"
            });
        employee.IsActive = false;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, inactiveEmployeeRequest.Id,
            new DecideMaintenanceWorkReportDto
            {
                Decision = "approved", Price = 500, VatRateCode = MaintenanceVatRates.WithoutVat
            }));
        employee.IsActive = true;
        await context.SaveChangesAsync();

        Assert.Equal(3, await context.MaintenanceWorkReports.CountAsync(report => report.Kind == "extra"
            && report.MaintenanceId == maintenance.Id));
        var allReports = await query.GetReportsAsync(worker.Id, organization.Id, maintenance.Id);
        Assert.Contains(allReports, report => report.Id == request.Id);
        Assert.Contains(allReports, report => report.Id == rejectedRequest.Id);
        var productionReport = await query.GetProductionReportAsync(worker.Id, organization.Id, maintenance.Id);
        Assert.Equal(2, productionReport.TotalOperations);
        Assert.Equal(0, productionReport.CompletedOperations);
        Assert.Null(productionReport.RevenueAmount);

        var financePermissions = new Mock<IOrganizationPermissionService>();
        financePermissions.Setup(service => service.HasPermissionAsync(
                worker.Id, organization.Id, OrganizationPermissionCodes.FinanceView))
            .ReturnsAsync(true);
        var financeClock = new Mock<ISystemClock>();
        financeClock.SetupGet(clock => clock.UtcNow).Returns(now);
        var financeReportService = new MaintenanceWorkQueryService(
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            financePermissions.Object,
            financeClock.Object);
        Assert.Equal(4200, (await financeReportService.GetProductionReportAsync(
            worker.Id, organization.Id, maintenance.Id)).RevenueAmount);
        var savedMaintenance = await context.Maintenance.AsNoTracking().SingleAsync(item => item.Id == maintenance.Id);
        Assert.Contains(savedMaintenance.Details, item => item.Name == extraCommand.Text && item.Price == 4200
            && item.EmployeeId == employee.Id && item.VatRateCode == MaintenanceVatRates.WithoutVat);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, request.Id,
            new DecideMaintenanceWorkReportDto { Decision = "approved", Price = 4200, VatRateCode = MaintenanceVatRates.WithoutVat }));

        var pendingReport = await reports.AddReportAsync(worker.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto
            {
                CommandId = Guid.NewGuid(), Kind = "extra", Text = "Дополнительная работа перед выдачей"
            });
        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.ReadyForHandoverAt = now;
        maintenance.HandedOverAt = now;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
            worker.Id, organization.Id, maintenance.Id, pendingReport.Id,
            new DecideMaintenanceWorkReportDto { Decision = "rejected", Reason = "Слишком поздно" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.ReturnForReworkAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(), Action = "return", Reason = "После передачи", Version = completed.Version
            }));
    }

    [Fact]
    public async Task AdminCanCompleteAssignedWorkForAnotherEmployeeOnCompletedMaintenanceAndAuditTheReason()
    {
        await using var context = fixture.CreateContext();
        var admin = new ApplicationUser { UserName ="manager@example.test" };
        var worker = new ApplicationUser { UserName ="worker@example.test" };
        var adminMember = new OrganizationMember { User = admin, RoleCode = OrganizationRoleCodes.Owner };
        var workerMember = new OrganizationMember { User = worker, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = admin,
            Name = "Административное завершение",
            Members = [adminMember, workerMember]
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = workerMember,
            Name = "Назначенный мастер"
        };
        var detailId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var maintenance = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.Completed,
            CompletedDate = now,
            Details = [new MaintenanceDetail
            {
                DetailId = detailId,
                Name = "Полировка",
                EmployeeId = employee.Id,
                EmployeeName = employee.Name
            }]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                admin.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .Returns(Task.CompletedTask);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(service => service.UtcNow).Returns(now);
        var execution = new MaintenanceWorkExecutionService(context, subscription.Object, clock.Object);
        var command = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(),
            Action = "complete-for-employee",
            Version = 0,
            Reason = "Сотрудник отсутствует, работу проверил администратор"
        };

        var completed = await execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, maintenance.Id, detailId, command);
        Assert.Equal(MaintenanceWorkStatus.Completed, completed.Status);
        Assert.Equal(TimeSpan.Zero, completed.ActiveTime);
        Assert.Null(completed.StartedAt);

        var operation = await context.MaintenanceWorkOperations.SingleAsync(item => item.Id == detailId);
        Assert.Equal(employee.Id, operation.EmployeeId);
        Assert.Null(operation.StartedAt);
        Assert.Equal(now, operation.CompletedAt);
        var workEvent = await context.MaintenanceWorkEvents.SingleAsync(item => item.CommandId == command.CommandId);
        Assert.Equal(admin.Id, workEvent.ActorUserId);
        Assert.Equal(adminMember.Id, workEvent.MemberId);
        Assert.Equal(employee.Id, workEvent.EmployeeId);
        Assert.Equal(command.Reason, workEvent.Reason);

        var repeated = await execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, maintenance.Id, detailId, command);
        Assert.Equal(completed.Version, repeated.Version);
        Assert.Single(await context.MaintenanceWorkEvents.Where(item => item.CommandId == command.CommandId).ToListAsync());

        var (query, _, _, _) = CreateWorkServices(context, now);
        var progress = Assert.Single(await query.GetProgressAsync(admin.Id, organization.Id, maintenance.Id));
        Assert.True(progress.HasAssignedEmployee);
        Assert.Equal("manager@example.test", progress.CompletedBy);
        Assert.Equal(command.Reason, progress.CompletionReason);
        await new OpenCRM.Modules.Maintenance.MaintenanceWorkGuard(context)
            .EnsureTrackedWorkCompletedAsync(maintenance);
        subscription.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            admin.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit), Times.Exactly(2));
    }

    [Fact]
    public async Task AdminCompletionRequiresMaintenanceEditAndClosesOnlyTheRealActiveInterval()
    {
        await using var context = fixture.CreateContext();
        var admin = new ApplicationUser { UserName ="manager@example.test" };
        var worker = new ApplicationUser { UserName ="worker@example.test" };
        var adminMember = new OrganizationMember { User = admin, RoleCode = OrganizationRoleCodes.Owner };
        var workerMember = new OrganizationMember { User = worker, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = admin,
            Name = "Завершение активной работы",
            Members = [adminMember, workerMember]
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = workerMember,
            Name = "Мастер"
        };
        var detailId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var startedAt = now.AddMinutes(-12);
        var maintenance = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Details = [new MaintenanceDetail
            {
                DetailId = detailId,
                Name = "Полировка",
                EmployeeId = employee.Id,
                EmployeeName = employee.Name
            }]
        };
        var operation = new MaintenanceWorkOperation
        {
            Id = detailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress,
            StartedAt = startedAt
        };
        var interval = new MaintenanceWorkInterval
        {
            Operation = operation,
            EmployeeId = employee.Id,
            StartedAt = startedAt
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        context.MaintenanceWorkOperations.Add(operation);
        context.MaintenanceWorkIntervals.Add(interval);
        await context.SaveChangesAsync();

        var deniedSubscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        deniedSubscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                admin.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .ThrowsAsync(new UnauthorizedAccessException());
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(service => service.UtcNow).Returns(now);
        var deniedExecution = new MaintenanceWorkExecutionService(context, deniedSubscription.Object, clock.Object);
        var command = new MaintenanceWorkCommandDto
        {
            CommandId = Guid.NewGuid(),
            Action = "complete-for-employee",
            Version = operation.Version,
            Reason = "Проверено"
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => deniedExecution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, maintenance.Id, detailId, command));

        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                admin.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .Returns(Task.CompletedTask);
        var execution = new MaintenanceWorkExecutionService(context, subscription.Object, clock.Object);
        var staleCommand = new MaintenanceWorkCommandDto
        {
            CommandId = command.CommandId,
            Action = command.Action,
            Version = operation.Version + 1,
            Reason = command.Reason
        };
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, maintenance.Id, detailId, staleCommand));

        var completed = await execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, maintenance.Id, detailId, command);
        Assert.Equal(TimeSpan.FromMinutes(12), completed.ActiveTime);
        var savedInterval = await context.MaintenanceWorkIntervals.SingleAsync(item => item.OperationId == detailId);
        Assert.Equal(now, savedInterval.EndedAt);
        Assert.Equal(employee.Id, (await context.MaintenanceWorkOperations.SingleAsync(item => item.Id == detailId)).EmployeeId);
    }

    [Fact]
    public async Task AdminCompletionRejectsInvalidParentStatesMissingReasonUnassignedWorkAndForeignMaintenance()
    {
        await using var context = fixture.CreateContext();
        var admin = new ApplicationUser { UserName ="manager@example.test" };
        var worker = new ApplicationUser { UserName ="worker@example.test" };
        var adminMember = new OrganizationMember { User = admin, RoleCode = OrganizationRoleCodes.Owner };
        var workerMember = new OrganizationMember { User = worker, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = admin,
            Name = "Ограничения административного завершения",
            Members = [adminMember, workerMember]
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = workerMember,
            Name = "Мастер"
        };
        var detailId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var planned = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Работа", EmployeeId = employee.Id }]
        };
        var cancelled = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.Cancelled,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Работа", EmployeeId = employee.Id }]
        };
        var handedOver = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.Completed,
            CompletedDate = now,
            ReadyForHandoverAt = now,
            HandedOverAt = now,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Работа", EmployeeId = employee.Id }]
        };
        var withoutAssignee = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Работа" }]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.AddRange(planned, cancelled, handedOver, withoutAssignee);
        await context.SaveChangesAsync();
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(service => service.UtcNow).Returns(now);
        var execution = new MaintenanceWorkExecutionService(
            context, Mock.Of<IOrganizationSubscriptionService>(), clock.Object);
        MaintenanceWorkCommandDto Command(string? reason) => new()
        {
            CommandId = Guid.NewGuid(),
            Action = "complete-for-employee",
            Reason = reason
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, planned.Id, detailId, Command("Проверено")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, cancelled.Id, detailId, Command("Проверено")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, handedOver.Id, detailId, Command("Проверено")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, withoutAssignee.Id, detailId, Command("Проверено")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, withoutAssignee.Id, detailId, Command("  ")));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => execution.CompleteForEmployeeAsync(
            admin.Id, Guid.NewGuid(), withoutAssignee.Id, detailId, Command("Проверено")));
    }

    [Fact]
    public async Task AdminCompletionLeavesClosedIntervalsOfPausedWorkUntouched()
    {
        await using var context = fixture.CreateContext();
        var admin = new ApplicationUser { UserName ="manager@example.test" };
        var worker = new ApplicationUser { UserName ="worker@example.test" };
        var adminMember = new OrganizationMember { User = admin, RoleCode = OrganizationRoleCodes.Owner };
        var workerMember = new OrganizationMember { User = worker, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            OwnerUser = admin,
            Name = "Пауза перед административным завершением",
            Members = [adminMember, workerMember]
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = workerMember,
            Name = "Мастер"
        };
        var detailId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var startedAt = now.AddMinutes(-20);
        var pausedAt = now.AddMinutes(-8);
        var maintenance = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Details = [new MaintenanceDetail
            {
                DetailId = detailId,
                Name = "Работа",
                EmployeeId = employee.Id,
                EmployeeName = employee.Name
            }]
        };
        var operation = new MaintenanceWorkOperation
        {
            Id = detailId,
            Maintenance = maintenance,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.Paused,
            StartedAt = startedAt
        };
        var interval = new MaintenanceWorkInterval
        {
            Operation = operation,
            EmployeeId = employee.Id,
            StartedAt = startedAt,
            EndedAt = pausedAt
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        context.MaintenanceWorkOperations.Add(operation);
        context.MaintenanceWorkIntervals.Add(interval);
        await context.SaveChangesAsync();

        var clock = new Mock<ISystemClock>();
        clock.SetupGet(service => service.UtcNow).Returns(now);
        var execution = new MaintenanceWorkExecutionService(
            context, Mock.Of<IOrganizationSubscriptionService>(), clock.Object);
        var completed = await execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "complete-for-employee",
                Version = operation.Version,
                Reason = "Работу проверил администратор"
            });

        Assert.Equal(TimeSpan.FromMinutes(12), completed.ActiveTime);
        Assert.Equal(pausedAt, (await context.MaintenanceWorkIntervals.SingleAsync(item => item.OperationId == detailId)).EndedAt);
    }

    [Fact]
    public async Task SelfAdminCompletionIsShownUntilANewerEmployeeCompletionReplacesItsAudit()
    {
        await using var context = fixture.CreateContext();
        var admin = new ApplicationUser { UserName ="manager-performer@example.test" };
        var member = new OrganizationMember { User = admin, RoleCode = OrganizationRoleCodes.Owner };
        var organization = new Organization
        {
            OwnerUser = admin,
            Name = "Повторное выполнение работы",
            Members = [member]
        };
        var employee = new OrganizationEmployee
        {
            Organization = organization,
            Member = member,
            Name = "Администратор и мастер"
        };
        var detailId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var maintenance = new MaintenanceEntity
        {
            User = admin,
            Vehicle = new Vehicle { User = admin, Organization = organization },
            Status = MaintenanceStatus.Completed,
            CompletedDate = now,
            Details = [new MaintenanceDetail
            {
                DetailId = detailId,
                Name = "Работа",
                EmployeeId = employee.Id,
                EmployeeName = employee.Name
            }]
        };
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                admin.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                admin.Id, organization.Id, OrganizationPermissionCodes.MaintenanceWorkExecute))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                admin.Id, organization.Id, OrganizationPermissionCodes.MaintenanceView))
            .Returns(Task.CompletedTask);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(service => service.UtcNow).Returns(now);
        var execution = new MaintenanceWorkExecutionService(context, subscription.Object, clock.Object);
        var adminResult = await execution.CompleteForEmployeeAsync(
            admin.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "complete-for-employee",
                Version = 0,
                Reason = "Администратор проверил собственную работу"
            });
        var query = new MaintenanceWorkQueryService(
            context, subscription.Object, Mock.Of<IOrganizationPermissionService>(), clock.Object);
        var initialProgress = Assert.Single(await query.GetProgressAsync(admin.Id, organization.Id, maintenance.Id));
        Assert.Equal("manager-performer@example.test", initialProgress.CompletedBy);
        Assert.Equal("Администратор проверил собственную работу", initialProgress.CompletionReason);

        maintenance.Status = MaintenanceStatus.InProgress;
        await context.SaveChangesAsync();
        await execution.ReturnForReworkAsync(
            admin.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "return",
                Version = adminResult.Version,
                Reason = "Нужно исправить"
            });
        var operation = await context.MaintenanceWorkOperations.SingleAsync(item => item.Id == detailId);
        var started = await execution.ExecuteAsync(
            admin.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "start",
                Version = operation.Version
            });
        await execution.ExecuteAsync(
            admin.Id, organization.Id, maintenance.Id, detailId,
            new MaintenanceWorkCommandDto
            {
                CommandId = Guid.NewGuid(),
                Action = "complete",
                Version = started.Version
            });

        var finalProgress = Assert.Single(await query.GetProgressAsync(admin.Id, organization.Id, maintenance.Id));
        Assert.Equal((int)MaintenanceWorkStatus.Completed, finalProgress.Status);
        Assert.Null(finalProgress.CompletedBy);
        Assert.Null(finalProgress.CompletionReason);
    }

    [Theory]
    [InlineData(false, "approved")]
    [InlineData(false, "rejected")]
    [InlineData(true, "approved")]
    public async Task PendingExtraWorkApprovalRequiresCurrentTeamAccess(bool teamAvailable, string decision)
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var member = new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner };
        var organization = new Organization
        {
            OwnerUser = owner,
            Name = "Согласование после изменения тарифа",
            Members = [member]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "QA мастер" };
        var detailId = Guid.NewGuid();
        var maintenance = CreateWorkOrder(owner, organization, employee, detailId, MaintenanceStatus.InProgress);
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var hasTeam = true;
        var subscriptions = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                owner.Id, organization.Id, OrganizationPermissionCodes.MaintenanceWorkExecute))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                owner.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.HasFeatureAsync(owner.Id, organization.Id, FeatureCodes.MembersModule))
            .ReturnsAsync(() => hasTeam);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
        var reports = new MaintenanceWorkReportService(
            context, subscriptions.Object, new MaintenancePlanAccess(subscriptions.Object), clock.Object);
        var pending = await reports.AddReportAsync(owner.Id, organization.Id, maintenance.Id, detailId,
            new CreateMaintenanceWorkReportDto { CommandId = Guid.NewGuid(), Kind = "extra", Text = "Дополнительная полировка QA" });
        hasTeam = teamAvailable;
        var command = new DecideMaintenanceWorkReportDto
        {
            Decision = decision,
            Price = 500,
            VatRateCode = MaintenanceVatRates.WithoutVat,
            Reason = "Решение QA"
        };

        if (!teamAvailable && decision == "approved")
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reports.DecideReportAsync(
                owner.Id, organization.Id, maintenance.Id, pending.Id, command));
            Assert.Contains("текущем тарифе", error.Message);
        }
        else
        {
            await reports.DecideReportAsync(owner.Id, organization.Id, maintenance.Id, pending.Id, command);
        }

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.Include(value => value.Details).SingleAsync(value => value.Id == maintenance.Id);
        var savedReport = await context.MaintenanceWorkReports.SingleAsync(value => value.Id == pending.Id);
        Assert.Equal(employee.Id, saved.Details.Single(value => value.DetailId == detailId).EmployeeId);
        if (decision == "approved" && teamAvailable)
        {
            var added = Assert.Single(saved.Details, value => value.DetailId != detailId);
            Assert.Equal(employee.Id, added.EmployeeId);
            Assert.Equal(500, added.Price);
            Assert.Equal(added.DetailId, savedReport.ApprovedDetailId);
            Assert.Equal("approved", savedReport.Decision);
        }
        else
        {
            Assert.Single(saved.Details);
            Assert.Null(savedReport.ApprovedDetailId);
            Assert.Equal(decision == "rejected" ? "rejected" : null, savedReport.Decision);
        }
    }

    private static (
        MaintenanceWorkQueryService Query,
        MaintenanceWorkExecutionService Execution,
        MaintenanceWorkReportService Reports,
        MaintenanceWorkTimeService Time) CreateWorkServices(
        OpenCRMContext context, DateTime now)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(value => value.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.MembersModule))
            .ReturnsAsync(true);
        var subscriptionService = subscriptions.Object;

        return (
            new MaintenanceWorkQueryService(
                context, subscriptionService, Mock.Of<IOrganizationPermissionService>(), clock.Object),
            new MaintenanceWorkExecutionService(context, subscriptionService, clock.Object),
            new MaintenanceWorkReportService(
                context, subscriptionService, new MaintenancePlanAccess(subscriptionService), clock.Object),
            new MaintenanceWorkTimeService(context, subscriptionService, clock.Object));
    }

    private static MaintenanceEntity CreateCompletedMaintenance(
        ApplicationUser user,
        Organization organization,
        OrganizationEmployee employee,
        Guid detailId,
        DateTime completedAt) => new()
    {
        User = user,
        Vehicle = new Vehicle { User = user, Organization = organization },
        Status = MaintenanceStatus.Completed,
        Date = completedAt.Date,
        CompletedDate = completedAt,
        Details = [new MaintenanceDetail
        {
            DetailId = detailId,
            Name = "Работа",
            EmployeeId = employee.Id
        }]
    };

    private static MaintenanceEntity CreateScheduledMaintenance(
        ApplicationUser user,
        Organization organization,
        OrganizationEmployee employee,
        string name,
        DateTime plannedDate,
        TimeSpan plannedStartTime)
    {
        var detailId = Guid.NewGuid();
        return new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.InProgress,
            Date = plannedDate,
            PlannedDate = plannedDate,
            PlannedStartTime = plannedStartTime,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = name, EmployeeId = employee.Id }]
        };
    }

    private static MaintenanceEntity CreateWorkOrder(
        ApplicationUser user,
        Organization organization,
        OrganizationEmployee employee,
        Guid detailId,
        MaintenanceStatus status) => new()
    {
        User = user,
        Vehicle = new Vehicle { User = user, Organization = organization },
        Status = status,
        Date = DateTime.UtcNow,
        Details = [new MaintenanceDetail { DetailId = detailId, Name = "Полировка", EmployeeId = employee.Id }]
    };

    private static MaintenanceWorkOperation CreateOperation(
        MaintenanceEntity maintenance,
        OrganizationEmployee employee,
        MaintenanceWorkStatus status) => new()
    {
        Id = maintenance.Details.Single().DetailId,
        Maintenance = maintenance,
        OrganizationId = employee.Organization.Id,
        EmployeeId = employee.Id,
        Status = status
    };

    private static OrganizationMaterialMovement CreateMaintenanceMovement(
        Organization organization,
        MaintenanceEntity maintenance,
        OrganizationMaterialStock stock,
        decimal quantity,
        decimal totalCost) => new()
    {
        Organization = organization,
        Stock = stock,
        Maintenance = maintenance,
        TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue,
        Quantity = quantity,
        TotalCost = totalCost,
        Unit = stock.Unit
    };

    private static ISystemClock CreateClock(DateTime now)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);

        return clock.Object;
    }
}
