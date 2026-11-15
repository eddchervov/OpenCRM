using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Planning.Contracts.Dtos;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerReferenceValidationTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task CreateRejectsVehicleClientResponsibleDefectAndMaintenanceFromAnotherWorkspace()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var foreignVehicle = await PlanningTestData.CreateVehicleAsync(context, foreignOrganization, foreignOrganization.OwnerUser);
        var foreignClient = await PlanningTestData.CreateClientAsync(context, foreignOrganization);
        var foreignDefect = await PlanningTestData.CreateDefectAsync(context, foreignOrganization, foreignVehicle, foreignOrganization.OwnerUser);
        var foreignMaintenance = await PlanningTestData.CreateMaintenanceAsync(context, foreignVehicle, foreignOrganization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        var invalidReferences = new (Action<CreateOrganizationSchedulerTaskDto> SetReference, string Message)[]
        {
            (dto => dto.VehicleId = foreignVehicle.Id, "Выбранная техника не найдена в рабочем пространстве."),
            (dto => dto.OrganizationClientId = foreignClient.Id, "Выбранный клиент не найден в рабочем пространстве."),
            (dto => dto.ResponsibleUserId = foreignOrganization.OwnerUserId, "Выбранный ответственный не найден в рабочем пространстве."),
            (dto => dto.LinkedDefectId = foreignDefect.Id, "Выбранная неисправность не найдена."),
            (dto => dto.LinkedMaintenanceId = foreignMaintenance.Id, "Выбранное обслуживание не найдено.")
        };

        foreach (var (setReference, message) in invalidReferences)
        {
            var dto = new CreateOrganizationSchedulerTaskDto { Title = "Проверка ссылки" };
            setReference(dto);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => commands.CreateTaskAsync(
                organization.OwnerUserId, organization.Id, dto));

            Assert.Equal(message, exception.Message);
        }

        Assert.Empty(await context.OrganizationSchedulerTasks.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().Where(value => value.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task CreateAcceptsOwnedVehicleClientDefectAndMaintenanceReferences()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var client = await PlanningTestData.CreateClientAsync(context, organization);
        var defect = await PlanningTestData.CreateDefectAsync(context, organization, vehicle, organization.OwnerUser);
        var maintenance = await PlanningTestData.CreateMaintenanceAsync(context, vehicle, organization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();

        var taskId = await commands.CreateTaskAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationSchedulerTaskDto
        {
            Title = "Связанная задача",
            VehicleId = vehicle.Id,
            OrganizationClientId = client.Id,
            ResponsibleUserId = organization.OwnerUserId,
            LinkedDefectId = defect.Id,
            LinkedMaintenanceId = maintenance.Id
        });

        var task = await context.OrganizationSchedulerTasks.AsNoTracking().SingleAsync(value => value.Id == taskId);
        Assert.Equal(vehicle.Id, task.VehicleId);
        Assert.Equal(client.Id, task.OrganizationClientId);
        Assert.Equal(organization.OwnerUserId, task.ResponsibleUserId);
        Assert.Equal(defect.Id, task.LinkedDefectId);
        Assert.Equal(maintenance.Id, task.LinkedMaintenanceId);
    }
}
