using Microsoft.EntityFrameworkCore;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Читает занятые интервалы постов рабочего пространства. </summary>
internal sealed class MaintenanceSlotQueryService : IMaintenanceSlotQueryService
{
    /// <summary> Контекст базы данных для чтения занятых постов. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Проверка подписки организации. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Создаёт сервис с контекстом и проверкой подписки. </summary>
    public MaintenanceSlotQueryService(OpenCRMContext context, IOrganizationSubscriptionService organizationSubscriptionService)
    {
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
    }

    /// <summary> Возвращает занятые интервалы постов на дату. </summary>
    public async Task<IReadOnlyList<OccupiedMaintenanceSlotDto>> GetOccupiedSlots(
        string userId,
        Guid organizationId,
        DateOnly date,
        string location,
        Guid? excludeMaintenanceId = null,
        DateOnly? dateTo = null)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);
        var normalizedLocation = MaintenanceSlotRules.NormalizeSlotLocation(location);
        if (string.IsNullOrEmpty(normalizedLocation))
        {
            return [];
        }

        if (dateTo.HasValue && dateTo.Value < date)
        {
            throw new InvalidOperationException("Дата окончания не может быть раньше даты начала.");
        }

        var from = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = (dateTo ?? date).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);
        var items = await MaintenanceSlotRules.QueryOccupiedSlots(this._context, organizationId, from, to, excludeMaintenanceId)
            .OrderBy(x => x.PlannedDate ?? x.Date)
            .ThenBy(x => x.PlannedStartTime)
            .Include(x => x.Vehicle)
            .ToListAsync();

        return items
            .Where(x => MaintenanceSlotRules.NormalizeSlotLocation(x.Location) == normalizedLocation)
            .Where(x => MaintenanceSlotRules.GetPlannedStart(x) < to && MaintenanceSlotRules.GetPlannedEnd(x) > from)
            .Select(x => new OccupiedMaintenanceSlotDto(
                x.Id,
                Math.Max(0, (MaintenanceSlotRules.GetPlannedStart(x) - from).TotalMinutes),
                Math.Min((to - from).TotalMinutes, (MaintenanceSlotRules.GetPlannedEnd(x) - from).TotalMinutes),
                x.Vehicle.RegistrationPlate ?? x.Vehicle.Name ?? "Автомобиль",
                x.Details.FirstOrDefault(detail => string.IsNullOrEmpty(detail.MaterialName))?.Name,
                MaintenanceSlotRules.GetPlannedStart(x),
                MaintenanceSlotRules.GetPlannedEnd(x)))
            .ToList();
    }

    /// <summary> Проверяет право пользователя читать рабочее пространство. </summary>
    private async Task EnsureCanReadWorkspaceAsync(string userId, Guid organizationId)
    {
        await this._organizationSubscriptionService.EnsureWorkspaceCanReadAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.MaintenanceView);
    }
}
