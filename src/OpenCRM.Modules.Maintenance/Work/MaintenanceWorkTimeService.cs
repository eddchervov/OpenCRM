using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Work;

/// <summary> Сервис корректировки рабочего времени по работам. </summary>
internal sealed class MaintenanceWorkTimeService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    ISystemClock systemClock) : IMaintenanceWorkTimeService
{
    /// <summary> Зависимость для выполнения операций в транзакции. </summary>
    private readonly MaintenanceTransaction _transaction = new(context);

    /// <summary> Возвращает корректировку времени в минутах для строки работы. </summary>
    internal static Task<int> GetAdjustmentMinutesAsync(OpenCRMContext context, Guid detailId) =>
        context.MaintenanceWorkReports.AsNoTracking()
            .Where(report => report.DetailId == detailId && report.Kind == "time")
            .SumAsync(report => report.TimeAdjustmentMinutes ?? 0);

    /// <summary> Корректирует рабочее время по строке работы. </summary>
    public async Task<MaintenanceWorkReportDto> AdjustTimeAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId, AdjustMaintenanceWorkTimeDto command)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        var reason = command.Reason?.Trim();
        if (command.CommandId == Guid.Empty || command.Minutes == 0
            || command.Minutes is < -10080 or > 10080 || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
        {
            throw new InvalidOperationException("Укажите изменение времени и причину не длиннее 1000 символов.");
        }

        return await this._transaction.ExecuteAsync(
            () => this.AdjustTimeCoreAsync(userId, organizationId, maintenanceId, detailId, command, reason!),
            () => context.MaintenanceWorkReports.AsNoTracking().AnyAsync(item => item.CommandId == command.CommandId));
    }

    /// <summary> Внутренняя реализация корректировки рабочего времени. </summary>
    private async Task<MaintenanceWorkReportDto> AdjustTimeCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        AdjustMaintenanceWorkTimeDto command,
        string reason
    )
    {
        var maintenance = await context.Maintenance
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        if (maintenance.IsDeleted)
        {
            throw new KeyNotFoundException("Обслуживание не найдено.");
        }

        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);
        var operation = await context.MaintenanceWorkOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == detailId && item.MaintenanceId == maintenanceId)
            ?? throw new KeyNotFoundException("Начатая работа не найдена.");
        var previous = await context.MaintenanceWorkReports.AsNoTracking()
            .SingleOrDefaultAsync(report => report.CommandId == command.CommandId);
        var employeeName = await context.OrganizationEmployees.AsNoTracking()
            .Where(employee => employee.Id == operation.EmployeeId)
            .Select(employee => employee.Name)
            .SingleAsync();
        if (previous != null)
        {
            if (previous.OrganizationId != organizationId || previous.MaintenanceId != maintenanceId
                || previous.DetailId != detailId || previous.ActorUserId != userId
                || previous.Kind != "time" || previous.TimeAdjustmentMinutes != command.Minutes
                || previous.Text != reason)
            {
                throw new InvalidOperationException("Идентификатор запроса уже использован для другого действия.");
            }

            return MaintenanceWorkMapping.MapReport(previous, employeeName);
        }

        var intervals = await context.MaintenanceWorkIntervals.AsNoTracking()
            .Where(interval => interval.OperationId == detailId)
            .ToListAsync();
        var now = systemClock.UtcNow;
        var actualTime = intervals.Aggregate(TimeSpan.Zero,
            (total, interval) => total + ((interval.EndedAt ?? now) - interval.StartedAt));
        var priorAdjustment = await MaintenanceWorkTimeService.GetAdjustmentMinutesAsync(context, detailId);
        if (actualTime + TimeSpan.FromMinutes(priorAdjustment + command.Minutes) < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Итоговое время работы не может быть отрицательным.");
        }

        var memberId = await context.OrganizationMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId && member.UserId == userId)
            .Select(member => member.Id)
            .SingleAsync();
        var report = new MaintenanceWorkReport
        {
            CommandId = command.CommandId,
            OrganizationId = organizationId,
            MaintenanceId = maintenanceId,
            DetailId = detailId,
            EmployeeId = operation.EmployeeId,
            MemberId = memberId,
            ActorUserId = userId,
            Kind = "time",
            Text = reason,
            TimeAdjustmentMinutes = command.Minutes,
            CreatedAt = now
        };
        context.MaintenanceWorkReports.Add(report);
        await context.SaveChangesAsync();

        return MaintenanceWorkMapping.MapReport(report, employeeName);
    }
}
