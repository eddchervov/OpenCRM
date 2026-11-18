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

/// <summary> Сервис отчётов о выполнении работ обслуживания. </summary>
internal sealed class MaintenanceWorkReportService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    MaintenancePlanAccess maintenancePlanAccess,
    ISystemClock systemClock) : IMaintenanceWorkReportService
{
    /// <summary> Зависимость для получения сотрудника организации. </summary>
    private readonly MaintenanceWorkEmployeeAccess _employeeAccess = new(context);

    /// <summary> Зависимость для выполнения операций в транзакции. </summary>
    private readonly MaintenanceTransaction _transaction = new(context);

    /// <summary> Добавляет отчёт о выполненной работе. </summary>
    public async Task<MaintenanceWorkReportDto> AddReportAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId, CreateMaintenanceWorkReportDto command)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        var employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        var kind = command.Kind?.Trim().ToLowerInvariant();
        var reportText = command.Text?.Trim();
        if (command.CommandId == Guid.Empty || kind is not ("comment" or "defect" or "extra")
            || string.IsNullOrWhiteSpace(reportText) || reportText.Length > 2000)
        {
            throw new InvalidOperationException("Укажите тип и текст сообщения не длиннее 2000 символов.");
        }

        return await this._transaction.ExecuteAsync(
            () => this.AddReportCoreAsync(userId, organizationId, maintenanceId, detailId, command, employee, kind!, reportText!),
            () => context.MaintenanceWorkReports.AsNoTracking().AnyAsync(item => item.CommandId == command.CommandId));
    }

    /// <summary> Внутренняя реализация добавления отчёта о работе. </summary>
    private async Task<MaintenanceWorkReportDto> AddReportCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        CreateMaintenanceWorkReportDto command,
        OrganizationEmployee employee,
        string kind,
        string reportText
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
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employee.Id} FOR UPDATE""");
        employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        var previous = await context.MaintenanceWorkReports.AsNoTracking()
            .SingleOrDefaultAsync(report => report.CommandId == command.CommandId);
        if (previous != null)
        {
            if (previous.OrganizationId != organizationId || previous.MaintenanceId != maintenanceId
                || previous.DetailId != detailId || previous.ActorUserId != userId
                || previous.Kind != kind || previous.Text != reportText)
            {
                throw new InvalidOperationException("Идентификатор запроса уже использован для другого действия.");
            }

            return MaintenanceWorkMapping.MapReport(previous, employee.Name);
        }

        if (maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            throw new InvalidOperationException("Обслуживание недоступно для замечаний.");
        }

        var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId
            && !MaintenanceWorkMapping.IsMaterial(item));
        if (detail == null || !MaintenanceWorkMapping.IsAssignedTo(maintenance, detail, employee.Id))
        {
            throw new UnauthorizedAccessException("Работа не назначена этому исполнителю.");
        }

        var memberId = employee.MemberId!.Value;
        var report = new MaintenanceWorkReport
        {
            CommandId = command.CommandId,
            OrganizationId = organizationId,
            MaintenanceId = maintenanceId,
            DetailId = detailId,
            EmployeeId = employee.Id,
            MemberId = memberId,
            ActorUserId = userId,
            Kind = kind,
            Text = reportText,
            CreatedAt = systemClock.UtcNow
        };
        context.MaintenanceWorkReports.Add(report);
        await context.SaveChangesAsync();

        return MaintenanceWorkMapping.MapReport(report, employee.Name);
    }

    /// <summary> Принимает решение по отчёту о работе. </summary>
    public async Task<MaintenanceWorkReportDto> DecideReportAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid reportId, DecideMaintenanceWorkReportDto command)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        var decision = command.Decision?.Trim().ToLowerInvariant();
        var reason = command.Reason?.Trim();
        if (decision is not ("approved" or "rejected") || reason is { Length: > 1000 }
            || decision == "rejected" && string.IsNullOrWhiteSpace(reason)
            || decision == "approved" && !MaintenanceWorkReportService.IsValidApprovedPrice(command.Price))
        {
            throw new InvalidOperationException("Укажите корректное решение, цену и причину отказа.");
        }

        var decisionAt = systemClock.UtcNow;
        return await this._transaction.ExecuteAsync(
            () => this.DecideReportCoreAsync(userId, organizationId, maintenanceId, reportId, command, decision!, reason, decisionAt),
            () => context.MaintenanceWorkReports.AsNoTracking().AnyAsync(item => item.Id == reportId && item.DecidedAt == decisionAt && item.DecidedByUserId == userId));
    }

    /// <summary> Проверяет диапазон цены дополнительной работы и точность до копеек. </summary>
    private static bool IsValidApprovedPrice(decimal? price) =>
        price is >= 0 and <= 1000000000 && Math.Round(price.Value, 2, MidpointRounding.AwayFromZero) == price.Value;
    /// <summary> Внутренняя реализация принятия решения по отчёту. </summary>
    private async Task<MaintenanceWorkReportDto> DecideReportCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid reportId,
        DecideMaintenanceWorkReportDto command,
        string decision,
        string? reason,
        DateTime decisionAt
    )
    {
        await this._transaction.LockOrganizationAsync(organizationId);
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
        var report = await context.MaintenanceWorkReports
            .SingleOrDefaultAsync(item => item.Id == reportId && item.MaintenanceId == maintenanceId
                && item.OrganizationId == organizationId && item.Kind == "extra")
            ?? throw new KeyNotFoundException("Запрос дополнительной работы не найден.");
        if (report.Decision != null)
        {
            throw new DbUpdateConcurrencyException("Решение уже принято. Обновите карточку.");
        }
        if (maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
        {
            throw new InvalidOperationException("Обслуживание недоступно для согласования.");
        }

        if (decision == "approved")
        {
            var organizationCurrencyCode = await context.Organizations
                .Where(organization => organization.Id == organizationId)
                .Select(organization => organization.CurrencyCode)
                .SingleAsync();
            MaintenanceCurrencyRules.EnsureMatchesOrganizationCurrency(maintenance, organizationCurrencyCode);
            await maintenancePlanAccess.EnsureCanAssignEmployeeAsync(userId, organizationId, null, report.EmployeeId);

            if (!MaintenanceVatRates.IsValid(command.VatRateCode))
            {
                throw new InvalidOperationException("Укажите ставку НДС для дополнительной работы.");
            }

            var employee = await context.OrganizationEmployees.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == report.EmployeeId && item.OrganizationId == organizationId
                    && item.IsActive && item.MemberId == report.MemberId)
                ?? throw new InvalidOperationException("Исполнитель больше недоступен для назначения.");
            var detailId = Guid.NewGuid();
            maintenance.ReplaceDetails([.. maintenance.Details, new MaintenanceDetail
            {
                DetailId = detailId,
                Name = report.Text,
                Price = command.Price,
                VatRateCode = command.VatRateCode,
                EmployeeId = employee.Id,
                EmployeeName = employee.Name
            }]);
            // Отмечаем родителя, чтобы проверка версии заказа охватила изменения строк.
            context.Entry(maintenance).Property(item => item.Comment).IsModified = true;
            report.ApprovedDetailId = detailId;
            report.ApprovedPrice = command.Price;
        }

        report.Decision = decision;
        report.DecisionReason = reason;
        report.DecidedByUserId = userId;
        report.DecidedAt = decisionAt;
        await context.SaveChangesAsync();

        var employeeName = await context.OrganizationEmployees.AsNoTracking()
            .Where(employee => employee.Id == report.EmployeeId)
            .Select(employee => employee.Name)
            .SingleAsync();

        return MaintenanceWorkMapping.MapReport(report, employeeName);
    }
}
