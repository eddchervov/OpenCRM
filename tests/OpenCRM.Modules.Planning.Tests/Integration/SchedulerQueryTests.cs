using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Planning.Contracts;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerQueryTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task DueRemindersAreScopedOrderedLimitedAndVisibleToCreatorOrAssignee()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var owner = organization.OwnerUser;
        var employee = await CreateEmployeeAsync(context, organization);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, owner);
        vehicle.Name = "Машина владельца";
        var client = await PlanningTestData.CreateClientAsync(context, organization);
        await context.SaveChangesAsync();

        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var firstVisible = PlanningTestData.CreateTask(organization, owner, "Создана владельцем", reminderAt: now.AddMinutes(-20));
        firstVisible.ResponsibleUserId = employee.Id;
        firstVisible.VehicleId = vehicle.Id;
        firstVisible.OrganizationClientId = client.Id;
        var secondVisible = PlanningTestData.CreateTask(organization, employee, "Назначена владельцу", reminderAt: now.AddMinutes(-10));
        secondVisible.ResponsibleUserId = owner.Id;
        var hiddenFromOwner = PlanningTestData.CreateTask(organization, employee, "Чужое напоминание", reminderAt: now.AddMinutes(-5));
        hiddenFromOwner.ResponsibleUserId = employee.Id;
        var alreadySent = PlanningTestData.CreateTask(organization, owner, "Уже отправлено", reminderAt: now.AddMinutes(-3));
        alreadySent.ReminderSentAt = now.AddMinutes(-2);
        var future = PlanningTestData.CreateTask(organization, owner, "Еще не наступило", reminderAt: now.AddMinutes(2));
        var closed = PlanningTestData.CreateTask(organization, owner, "Закрытая задача", reminderAt: now.AddMinutes(-1));
        closed.StatusCode = OrganizationSchedulerTaskStatusCodes.Closed;
        var deleted = PlanningTestData.CreateTask(organization, owner, "Удаленная задача", reminderAt: now.AddMinutes(-1));
        deleted.IsDeleted = true;
        var otherOrganization = await fixture.CreateOrganizationAsync(context);
        var foreign = PlanningTestData.CreateTask(otherOrganization, otherOrganization.OwnerUser, "Другая организация", reminderAt: now.AddMinutes(-30));
        context.OrganizationSchedulerTasks.AddRange(firstVisible, secondVisible, hiddenFromOwner, alreadySent, future, closed, deleted, foreign);
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ISchedulerQueryService>();
        var results = await query.GetDueRemindersAsync(owner.Id, organization.Id, now, take: 20);

        Assert.Equal(new[] { firstVisible.Id, secondVisible.Id }, results.Select(value => value.TaskId));
        Assert.All(results, value => Assert.Equal(organization.Id, value.OrganizationId));
        Assert.Equal("Создана владельцем", results[0].Title);
        Assert.Equal("Назначена владельцу", results[1].Title);
        Assert.Contains("Тест Автомобиль", results[0].VehicleDisplayName);
        Assert.Contains("Машина владельца", results[0].VehicleDisplayName);
        Assert.Equal(client.Name, results[0].ClientName);
    }

    [Fact]
    public async Task DueReminderTakeIsClampedToAtLeastOne()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var now = DateTime.UtcNow;
        context.OrganizationSchedulerTasks.AddRange(
            PlanningTestData.CreateTask(organization, organization.OwnerUser, "Первое", reminderAt: now.AddMinutes(-2)),
            PlanningTestData.CreateTask(organization, organization.OwnerUser, "Второе", reminderAt: now.AddMinutes(-1)));
        await context.SaveChangesAsync();

        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ISchedulerQueryService>();

        var results = await query.GetDueRemindersAsync(organization.OwnerUserId, organization.Id, now, take: 0);

        Assert.Single(results);
        Assert.Equal("Первое", results[0].Title);
    }

    private static async Task<ApplicationUser> CreateEmployeeAsync(OpenCRM.Infrastructure.Data.OpenCRMContext context, Organization organization)
    {
        var employee = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("D"),
            Email = $"planning-employee-{Guid.NewGuid():N}@example.test"
        };
        context.Users.Add(employee);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = employee.Id,
            RoleCode = OrganizationRoleCodes.Employee
        });
        await context.SaveChangesAsync();

        return employee;
    }
}
