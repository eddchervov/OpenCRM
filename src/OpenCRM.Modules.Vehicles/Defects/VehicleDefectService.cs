using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Defects;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Defects;

/// <summary> Фасад для чтения и изменения дефектов техники. </summary>
public class VehicleDefectService : IVehicleDefectService
{
    /// <summary> Сервис запросов дефектов. </summary>
    private readonly VehicleDefectQueryService _queries;

    /// <summary> Сервис команд дефектов. </summary>
    private readonly VehicleDefectCommandService _commands;

    /// <summary> Создаёт фасад дефектов с сервисами запросов и команд. </summary>
    public VehicleDefectService(
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService)
    {
        this._queries = new VehicleDefectQueryService(context, organizationSubscriptionService);
        this._commands = new VehicleDefectCommandService(context, organizationSubscriptionService);
    }

    /// <summary> Возвращает постраничный список дефектов с фильтрами. </summary>
    public Task<VehicleDefectListResponse> GetAsync(
        string userId,
        Guid organizationId,
        int page = 1,
        int pageSize = 12,
        string? searchQuery = null,
        string? statusCode = null,
        Guid? vehicleId = null,
        string? sort = null) =>
        this._queries.GetAsync(userId, organizationId, page, pageSize, searchQuery, statusCode, vehicleId, sort);

    /// <summary> Возвращает дефект по идентификатору. </summary>
    public Task<VehicleDefectDto?> GetByIdAsync(string userId, Guid organizationId, Guid defectId) =>
        this._queries.GetByIdAsync(userId, organizationId, defectId);

    /// <summary> Возвращает историю изменений дефекта. </summary>
    public Task<IReadOnlyList<VehicleDefectHistoryItemDto>> GetHistoryAsync(string userId, Guid organizationId, Guid defectId) =>
        this._queries.GetHistoryAsync(userId, organizationId, defectId);

    /// <summary> Возвращает варианты обслуживания для привязки к дефекту. </summary>
    public Task<IReadOnlyList<VehicleDefectMaintenanceOptionDto>> GetMaintenanceOptionsAsync(string userId, Guid organizationId, Guid defectId) =>
        this._queries.GetMaintenanceOptionsAsync(userId, organizationId, defectId);

    /// <summary> Возвращает варианты дефектов для привязки к обслуживанию. </summary>
    public Task<IReadOnlyList<VehicleDefectOptionDto>> GetOptionsForMaintenanceAsync(string userId, Guid organizationId, Guid maintenanceId) =>
        this._queries.GetOptionsForMaintenanceAsync(userId, organizationId, maintenanceId);

    /// <summary> Создаёт дефект техники. </summary>
    public Task<Guid> CreateAsync(string userId, Guid organizationId, CreateVehicleDefectDto dto) =>
        this._commands.CreateAsync(userId, organizationId, dto);

    /// <summary> Обновляет данные дефекта техники. </summary>
    public Task UpdateAsync(string userId, Guid organizationId, Guid defectId, UpdateVehicleDefectDto dto) =>
        this._commands.UpdateAsync(userId, organizationId, defectId, dto);

    /// <summary> Удаляет дефект техники. </summary>
    public Task DeleteAsync(string userId, Guid organizationId, Guid defectId) =>
        this._commands.DeleteAsync(userId, organizationId, defectId);

    /// <summary> Обновляет статус дефекта техники. </summary>
    public Task UpdateStatusAsync(string userId, Guid organizationId, Guid defectId, string statusCode) =>
        this._commands.UpdateStatusAsync(userId, organizationId, defectId, statusCode);

    /// <summary> Связывает дефект с обслуживанием. </summary>
    public Task LinkMaintenanceAsync(string userId, Guid organizationId, Guid defectId, Guid maintenanceId) =>
        this._commands.LinkMaintenanceAsync(userId, organizationId, defectId, maintenanceId);

    /// <summary> Отвязывает дефект от обслуживания. </summary>
    public Task UnlinkMaintenanceAsync(string userId, Guid organizationId, Guid defectId, Guid maintenanceId) =>
        this._commands.UnlinkMaintenanceAsync(userId, organizationId, defectId, maintenanceId);
}
