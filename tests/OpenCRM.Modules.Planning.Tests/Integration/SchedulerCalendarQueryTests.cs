using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Planning.Contracts;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerCalendarQueryTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task CalendarFiltersByDateVehicleClientResponsibleTypeAndPriority()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        vehicle.Name = "Машина клиента";
        var client = await PlanningTestData.CreateClientAsync(context, organization);
        var employee = new OpenCRM.Domain.Identity.ApplicationUser
        {
            Id = Guid.NewGuid().ToString("D"),
            Email = $"planning-calendar-{Guid.NewGuid():N}@example.test"
        };
        context.Users.Add(employee);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = employee.Id,
            RoleCode = OrganizationRoleCodes.Employee
        });
        await context.SaveChangesAsync();
        var selectedStart = new DateTime(2026, 10, 11, 23, 30, 0, DateTimeKind.Utc);
        var selected = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Подготовить автомобиль", selectedStart);
        selected.EndAt = selectedStart.AddHours(2);
        selected.VehicleId = vehicle.Id;
        selected.OrganizationClientId = client.Id;
        selected.ResponsibleUserId = employee.Id;
        selected.TypeCode = OrganizationSchedulerTaskTypeCodes.Call;
        selected.PriorityCode = OrganizationSchedulerTaskPriorityCodes.High;
        selected.Comment = "  Подтвердить детали  ";
        var wrongResponsible = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Другой ответственный", selectedStart);
        wrongResponsible.VehicleId = vehicle.Id;
        wrongResponsible.OrganizationClientId = client.Id;
        wrongResponsible.ResponsibleUserId = organization.OwnerUserId;
        wrongResponsible.TypeCode = OrganizationSchedulerTaskTypeCodes.Call;
        wrongResponsible.PriorityCode = OrganizationSchedulerTaskPriorityCodes.High;
        var wrongVehicle = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Другая техника", selectedStart);
        wrongVehicle.OrganizationClientId = client.Id;
        wrongVehicle.ResponsibleUserId = employee.Id;
        wrongVehicle.TypeCode = OrganizationSchedulerTaskTypeCodes.Call;
        wrongVehicle.PriorityCode = OrganizationSchedulerTaskPriorityCodes.High;
        var outsideRange = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Вне периода", selectedStart.AddDays(2));
        outsideRange.VehicleId = vehicle.Id;
        outsideRange.OrganizationClientId = client.Id;
        outsideRange.ResponsibleUserId = employee.Id;
        outsideRange.TypeCode = OrganizationSchedulerTaskTypeCodes.Call;
        outsideRange.PriorityCode = OrganizationSchedulerTaskPriorityCodes.High;
        var deleted = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Удалена", selectedStart);
        deleted.VehicleId = vehicle.Id;
        deleted.OrganizationClientId = client.Id;
        deleted.ResponsibleUserId = employee.Id;
        deleted.TypeCode = OrganizationSchedulerTaskTypeCodes.Call;
        deleted.PriorityCode = OrganizationSchedulerTaskPriorityCodes.High;
        deleted.IsDeleted = true;
        context.OrganizationSchedulerTasks.AddRange(selected, wrongResponsible, wrongVehicle, outsideRange, deleted);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ISchedulerQueryService>();

        var results = await queries.GetManualTaskCalendarAsync(organization.OwnerUserId, organization.Id,
            new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), vehicle.Id, client.Id,
            $" {employee.Id} ", " CALL ", " HIGH ");

        var item = Assert.Single(results);
        Assert.Equal(selected.Id, item.Id);
        Assert.Equal("task", item.ItemKind);
        Assert.Equal(selectedStart.Date, item.Date);
        Assert.Equal("23:30", item.PlannedStartTime);
        Assert.Equal("01:30", item.PlannedEndTime);
        Assert.Contains("Подтвердить детали", item.Comment);
        Assert.Contains("Машина клиента", item.Subtitle);
        Assert.Contains(client.Name, item.Subtitle);
        Assert.Equal($"#scheduler?task={selected.Id}", item.DetailUrl);
    }

    [Fact]
    public async Task CalendarSupportsUnassignedFilterAndDefaultsMissingEndToOneHour()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var start = new DateTime(2026, 10, 12, 10, 15, 0, DateTimeKind.Utc);
        var task = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Без ответственного", start);
        context.OrganizationSchedulerTasks.Add(task);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ISchedulerQueryService>();

        var results = await queries.GetManualTaskCalendarAsync(organization.OwnerUserId, organization.Id,
            start.Date, start.Date, null, null, "__none", null, null);

        var item = Assert.Single(results);
        Assert.Equal("10:15", item.PlannedStartTime);
        Assert.Equal("11:15", item.PlannedEndTime);
        Assert.Null(item.Subtitle);
        Assert.Equal(string.Empty, item.Comment);
    }
}
