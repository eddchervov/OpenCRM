using OpenCRM.Modules.Planning.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Testing;

public sealed class MaintenanceTestScope : IDisposable
{
    private readonly ServiceProvider _provider;

    public MaintenanceTestScope(
        IMaintenanceRepository maintenanceRepository,
        IVehicleCounterCommandService vehicleService,
        ISystemClock systemClock,
        OpenCRMContext context,
        IOrganizationSubscriptionService access,
        IMaintenanceQueryService query,
        IMaintenanceSlotQueryService? slots,
        IMaintenanceConsumptionService consumption,
        IConfiguration? configuration = null,
        IMaintenanceSchedulerSyncService? scheduler = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(maintenanceRepository ?? Mock.Of<IMaintenanceRepository>());
        services.AddSingleton<IVehicleMaintenanceLookupService>(
            new VehicleMaintenanceLookupService(new VehicleRepository(context)));
        services.AddSingleton(vehicleService ?? Mock.Of<IVehicleCounterCommandService>());
        services.AddSingleton(systemClock ?? Mock.Of<ISystemClock>());
        services.AddSingleton(access);
        services.AddSingleton(consumption ?? Mock.Of<IMaintenanceConsumptionService>());
        services.AddSingleton(configuration ?? new ConfigurationBuilder().Build());
        services.AddSingleton(scheduler ?? Mock.Of<IMaintenanceSchedulerSyncService>());
        services.AddOpenCRMMaintenance();
        services.AddScoped(_ => query);
        if (slots != null)
        {
            services.AddScoped(_ => slots);
        }
        this._provider = services.BuildServiceProvider();
    }

    public T Get<T>() where T : notnull => this._provider.GetRequiredService<T>();

    public void Dispose() => this._provider.Dispose();
    public Task<IReadOnlyList<MaintenancePaymentDto>> GetPayments(string userId, Guid organizationId, Guid maintenanceId) =>
        this.Get<IMaintenancePaymentService>().GetPayments(userId, organizationId, maintenanceId);

    public Task AddPayment(string userId, Guid organizationId, Guid maintenanceId, AddMaintenancePaymentDto dto) =>
        this.Get<IMaintenancePaymentService>().AddPayment(userId, organizationId, maintenanceId, dto);

    public Task CancelPayment(string userId, Guid organizationId, Guid maintenanceId, Guid paymentId, string reason) =>
        this.Get<IMaintenancePaymentService>().CancelPayment(userId, organizationId, maintenanceId, paymentId, reason);

    public Task AssignEmployee(string userId, Guid organizationId, Guid maintenanceId, AssignMaintenanceEmployeeDto dto) =>
        this.Get<IMaintenanceAssignmentService>().AssignEmployee(userId, organizationId, maintenanceId, dto);

    public Task UpdateServices(string userId, Guid organizationId, Guid maintenanceId, UpdateMaintenanceServicesDto dto) =>
        this.Get<IMaintenanceUpdateService>().UpdateServices(userId, organizationId, maintenanceId, dto);

    public Task UpdateVatRates(string userId, Guid organizationId, Guid maintenanceId, UpdateMaintenanceVatRatesDto dto) =>
        this.Get<IMaintenanceUpdateService>().UpdateVatRates(userId, organizationId, maintenanceId, dto);

    public Task<IReadOnlyList<OccupiedMaintenanceSlotDto>> GetOccupiedSlots(
        string userId, Guid organizationId, DateOnly date, string location, Guid? excludeMaintenanceId = null, DateOnly? dateTo = null) =>
        this.Get<IMaintenanceSlotQueryService>().GetOccupiedSlots(userId, organizationId, date, location, excludeMaintenanceId, dateTo);

    public Task<GetMaintenanceByIdDto> GetById(string userId, Guid maintenanceId) =>
        this.Get<IMaintenanceQueryService>().GetById(userId, maintenanceId);

    public Task<GetMaintenanceByIdDto> GetById(string userId, Guid? organizationId, Guid maintenanceId) =>
        this.Get<IMaintenanceQueryService>().GetById(userId, organizationId, maintenanceId);

    public Task<MaintenanceListResponse> Get(string userId, Guid vehicleId, int page = 1, int pageSize = 10, string? sort = null, int? type = null, int? status = null) =>
        this.Get<IMaintenanceQueryService>().Get(userId, vehicleId, page, pageSize, sort, type, status);

    public Task<MaintenanceListResponse> Get(string userId, Guid? organizationId, Guid vehicleId, int page = 1, int pageSize = 10, string? sort = null, int? type = null, int? status = null) =>
        this.Get<IMaintenanceQueryService>().Get(userId, organizationId, vehicleId, page, pageSize, sort, type, status);

    public Task<MaintenanceListResponse> GetDeleted(string userId, Guid vehicleId, int page = 1, int pageSize = 10) =>
        this.Get<IMaintenanceQueryService>().GetDeleted(userId, vehicleId, page, pageSize);

    public Task<MaintenanceListResponse> GetDeleted(string userId, Guid? organizationId, Guid vehicleId, int page = 1, int pageSize = 10) =>
        this.Get<IMaintenanceQueryService>().GetDeleted(userId, organizationId, vehicleId, page, pageSize);

    public Task<IEnumerable<GetMaintenanceDto>> Get(string userId, Guid vehicleId) =>
        this.Get<IMaintenanceQueryService>().Get(userId, vehicleId);

    public Task<IEnumerable<GetMaintenanceDto>> Get(string userId, Guid? organizationId, Guid vehicleId) =>
        this.Get<IMaintenanceQueryService>().Get(userId, organizationId, vehicleId);

    public Task<IReadOnlyList<MaintenanceHistoryEventDto>> GetHistory(string userId, Guid maintenanceId) =>
        this.Get<IMaintenanceQueryService>().GetHistory(userId, maintenanceId);

    public Task<IReadOnlyList<MaintenanceHistoryEventDto>> GetHistory(string userId, Guid? organizationId, Guid maintenanceId) =>
        this.Get<IMaintenanceQueryService>().GetHistory(userId, organizationId, maintenanceId);

    public Task<Guid> Create(string userId, Guid vehicleId, CreateMaintenanceDto dto) =>
        this.Get<IMaintenanceCreateService>().Create(userId, vehicleId, dto);

    public Task<Guid> Create(string userId, Guid? organizationId, Guid vehicleId, CreateMaintenanceDto dto) =>
        this.Get<IMaintenanceCreateService>().Create(userId, organizationId, vehicleId, dto);

    public Task Update(string userId, Guid maintenanceId, UpdateMaintenanceDto dto) =>
        this.Get<IMaintenanceUpdateService>().Update(userId, maintenanceId, dto);

    public Task Update(string userId, Guid? organizationId, Guid maintenanceId, UpdateMaintenanceDto dto) =>
        this.Get<IMaintenanceUpdateService>().Update(userId, organizationId, maintenanceId, dto);

    public Task<MaintenanceStatus> UpdatePlannedDate(
        string userId, Guid? organizationId, Guid maintenanceId, UpdateMaintenancePlannedDateDto dto) =>
        this.Get<IMaintenanceSchedulingService>().UpdatePlannedDate(userId, organizationId, maintenanceId, dto);

    public Task ChangeWorkflow(string userId, Guid maintenanceId, MaintenanceWorkflowActionDto dto) =>
        this.Get<IMaintenanceWorkflowService>().ChangeWorkflow(userId, maintenanceId, dto);

    public Task ChangeWorkflow(string userId, Guid? organizationId, Guid maintenanceId, MaintenanceWorkflowActionDto dto) =>
        this.Get<IMaintenanceWorkflowService>().ChangeWorkflow(userId, organizationId, maintenanceId, dto);

    public Task SaveIntake(string userId, Guid organizationId, Guid maintenanceId, SaveMaintenanceIntakeDto dto) =>
        this.Get<IMaintenanceIntakeService>().SaveIntake(userId, organizationId, maintenanceId, dto);

    public Task<int> EnsureWorkActNumber(string userId, Guid organizationId, Guid maintenanceId) =>
        this.Get<IMaintenanceDocumentService>().EnsureWorkActNumber(userId, organizationId, maintenanceId);

    public Task TrackDocumentGenerated(string userId, Guid? organizationId, Guid maintenanceId, string title, string? description = null) =>
        this.Get<IMaintenanceDocumentService>().TrackDocumentGenerated(userId, organizationId, maintenanceId, title, description);

    public Task<MaintenanceDocumentDto> AddDocument(string userId, Guid? organizationId, Guid maintenanceId, CreateMaintenanceDocumentDto dto) =>
        this.Get<IMaintenanceDocumentService>().AddDocument(userId, organizationId, maintenanceId, dto);

    public Task<IReadOnlyList<MaintenanceDocumentDto>> GetDocuments(string userId, Guid? organizationId, Guid maintenanceId) =>
        this.Get<IMaintenanceDocumentService>().GetDocuments(userId, organizationId, maintenanceId);

    public Task<MaintenanceDocumentDownloadDto> GetDocumentForDownload(string userId, Guid? organizationId, Guid documentId) =>
        this.Get<IMaintenanceDocumentService>().GetDocumentForDownload(userId, organizationId, documentId);

    public Task Delete(string userId, Guid maintenanceId) =>
        this.Get<IMaintenanceDeleteService>().Delete(userId, maintenanceId);

    public Task Delete(string userId, Guid? organizationId, Guid maintenanceId) =>
        this.Get<IMaintenanceDeleteService>().Delete(userId, organizationId, maintenanceId);
}
