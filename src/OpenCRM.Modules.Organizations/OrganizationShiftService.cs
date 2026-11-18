using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Shifts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations;

/// <summary> Фасадный сервис работы со сменами сотрудников организации. </summary>
public sealed class OrganizationShiftService : IOrganizationShiftService
{
    /// <summary> Зависимость запросов смен. </summary>
    private readonly OrganizationShiftQueryService _queries;

    /// <summary> Зависимость команд смен. </summary>
    private readonly OrganizationShiftCommandService _commands;

    /// <summary> Создаёт сервис смен с зависимостями. </summary>
    public OrganizationShiftService(
        OpenCRMContext context,
        IOrganizationSubscriptionService subscriptionService,
        IOrganizationPermissionService permissionService,
        IOrganizationAuditService auditService)
    {
        this._queries = new OrganizationShiftQueryService(context, subscriptionService, permissionService);
        this._commands = new OrganizationShiftCommandService(
            context,
            subscriptionService,
            auditService,
            new OrganizationTransaction(context));
    }

    /// <summary> Возвращает расписание смен на неделю. </summary>
    public Task<OrganizationShiftWeekDto> GetWeekAsync(string userId, Guid organizationId, DateOnly weekStart) =>
        this._queries.GetWeekAsync(userId, organizationId, weekStart);

    /// <summary> Возвращает смены текущего сотрудника. </summary>
    public Task<IReadOnlyList<OrganizationShiftDto>> GetOwnAsync(string userId, Guid organizationId) =>
        this._queries.GetOwnAsync(userId, organizationId);

    /// <summary> Возвращает доступность сотрудника за период. </summary>
    public Task<OrganizationShiftAvailabilityDto> GetAvailabilityAsync(
        string userId,
        Guid organizationId,
        Guid employeeId,
        string startLocal,
        string endLocal) =>
        this._queries.GetAvailabilityAsync(userId, organizationId, employeeId, startLocal, endLocal);

    /// <summary> Устанавливает часовой пояс организации. </summary>
    public Task SetTimeZoneAsync(string userId, Guid organizationId, string timeZoneId, string? expectedTimeZoneId = null) =>
        this._commands.SetTimeZoneAsync(userId, organizationId, timeZoneId, expectedTimeZoneId);

    /// <summary> Сохраняет смену или создаёт новую. </summary>
    public Task<Guid> SaveAsync(string userId, Guid organizationId, Guid? shiftId, SaveOrganizationShiftDto dto) =>
        this._commands.SaveAsync(userId, organizationId, shiftId, dto);

    /// <summary> Отменяет смену сотрудника с указанием причины. </summary>
    public Task CancelAsync(string userId, Guid organizationId, Guid shiftId, uint version, string? reason) =>
        this._commands.CancelAsync(userId, organizationId, shiftId, version, reason);

    /// <summary> Публикует смены на неделю. </summary>
    public Task<int> PublishWeekAsync(string userId, Guid organizationId, DateOnly weekStart) =>
        this._commands.PublishWeekAsync(userId, organizationId, weekStart);

    /// <summary> Копирует смены предыдущей недели на указанную неделю. </summary>
    public Task<int> CopyPreviousWeekAsync(string userId, Guid organizationId, DateOnly weekStart) =>
        this._commands.CopyPreviousWeekAsync(userId, organizationId, weekStart);
}
