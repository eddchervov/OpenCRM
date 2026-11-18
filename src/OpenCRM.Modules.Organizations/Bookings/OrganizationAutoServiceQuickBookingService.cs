using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Bookings;

namespace OpenCRM.Modules.Organizations;

/// <summary> Сервис быстрой записи клиента в автосервис. </summary>
public sealed class OrganizationAutoServiceQuickBookingService(
    IQuickBookingClientLookupService clientLookup,
    IQuickBookingVehicleLookupService vehicleLookup,
    IOrganizationQueryService organizationService,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationClientCommandService organizationClientService,
    IVehicleCommandService vehicleService,
    IMaintenanceCreateService maintenanceCreateService,
    ICatalogQueryService scenarioCatalogService,
    IVehicleRegistrationPlatePolicy? registrationPlates = null)
    : IOrganizationAutoServiceQuickBookingService
{
    /// <summary> Общая логика подготовки данных быстрой записи. </summary>
    private readonly QuickBookingSupport _quickBookingSupport = new(clientLookup, organizationClientService, scenarioCatalogService);

    /// <summary> Создаёт быструю запись в автосервис. </summary>
    public async Task<OrganizationAutoServiceQuickBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        CreateOrganizationAutoServiceQuickBookingDto dto)
    {
        var organization = await organizationService.GetByIdAsync(userId, organizationId);
        if (!OrganizationAutoServiceQuickBookingService.IsAutoServiceScenario(organization.PrimaryScenarioCode))
        {
            throw new InvalidOperationException("Быстрая приемка доступна только для сценария \"СТО / Автосервис\".");
        }

        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.MaintenanceCreate);

        var registrationCountryCode = InternationalRegistrationPlateRules.NormalizeCountryCode(dto.RegistrationCountryCode);
        var hasForeignCountry = registrationCountryCode is not null and not "RU";
        var foreignPlate = hasForeignCountry ? InternationalRegistrationPlateRules.NormalizePlate(dto.RegistrationPlate) : null;
        if (!Enum.IsDefined(dto.Kind))
        {
            throw new InvalidOperationException("Выберите поддерживаемый тип техники.");
        }

        var services = QuickBookingSupport.CreateServices(dto.Services, dto.ServiceId, dto.Price);
        await this._quickBookingSupport.PrepareServicesAsync(userId, organizationId, services);

        var otherFormat = hasForeignCountry || dto.AllowOtherRegistrationPlateFormat || registrationPlates is not null &&
            !registrationPlates.GetFormats().Formats.Any(format => format.Kinds.Contains((int)dto.Kind));
        var plate = hasForeignCountry ? foreignPlate ?? string.Empty
            : otherFormat ? dto.RegistrationPlate.Trim() : QuickBookingSupport.NormalizeDisplay(dto.RegistrationPlate);
        var normalizedPlate = vehicleLookup.NormalizePlateForSearch(plate);
        if (!dto.VehicleId.HasValue && string.IsNullOrWhiteSpace(normalizedPlate))
        {
            throw new InvalidOperationException("Укажите госномер авто для приемки.");
        }

        var requestedClientId = dto.OrganizationClientId;
        QuickBookingVehicleDto? vehicle;
        if (dto.VehicleId.HasValue)
        {
            var selectedVehicle = await vehicleLookup.GetSelectedAsync(userId, organizationId, dto.VehicleId.Value)
                ?? throw new InvalidOperationException("Выбранный автомобиль не найден.");
            if (selectedVehicle.Kind != dto.Kind || requestedClientId.HasValue
                && selectedVehicle.OrganizationClientId != requestedClientId.Value)
            {
                throw new InvalidOperationException("Выбранный автомобиль не относится к указанному клиенту или типу техники.");
            }

            var plateMatches = otherFormat || selectedVehicle.RegistrationCountryCode is not null and not "RU"
                ? string.Equals(dto.RegistrationPlate.Trim(), selectedVehicle.RegistrationPlate?.Trim(), StringComparison.Ordinal)
                : normalizedPlate == vehicleLookup.NormalizePlateForSearch(selectedVehicle.RegistrationPlate);
            if (!string.IsNullOrWhiteSpace(normalizedPlate) && !plateMatches)
            {
                throw new InvalidOperationException("Измените госномер в карточке автомобиля перед созданием записи.");
            }

            if (!string.IsNullOrEmpty(registrationCountryCode)
                && registrationCountryCode != (selectedVehicle.RegistrationCountryCode ?? "RU"))
            {
                throw new InvalidOperationException("Страна регистрации выбранного автомобиля изменилась. Выберите автомобиль заново.");
            }

            vehicle = selectedVehicle;
        }
        else
        {
            if (!string.IsNullOrEmpty(registrationCountryCode))
            {
                vehicle = await vehicleLookup.FindByPlateAsync(
                    userId, organizationId, plate, requestedClientId, dto.Kind, otherFormat, registrationCountryCode);
            }
            else if (otherFormat)
            {
                vehicle = await vehicleLookup.FindByPlateAsync(userId, organizationId, plate, requestedClientId, dto.Kind, allowOtherFormat: true);
            }
            else
            {
                vehicle = await vehicleLookup.FindByPlateAsync(userId, organizationId, plate, requestedClientId, dto.Kind);
            }
        }
        var vehicleCreated = false;
        var clientCreated = false;
        Guid? clientId = vehicle?.OrganizationClientId;

        if (vehicle != null && requestedClientId.HasValue)
        {
            var clientResult = await this._quickBookingSupport.ResolveClientIdAsync(
                userId,
                organizationId,
                requestedClientId,
                dto.ClientPhone,
                dto.ClientName,
                "Создан из быстрой приемки СТО");
            clientId = clientResult.ClientId;
            clientCreated = clientResult.Created;

            if (clientId.HasValue && vehicle.OrganizationClientId != clientId.Value)
            {
                throw new InvalidOperationException("Выбранное авто не относится к этому клиенту.");
            }
        }

        if (vehicle == null)
        {
            if (!hasForeignCountry)
            {
                registrationPlates?.Validate(plate, dto.Kind, dto.AllowOtherRegistrationPlateFormat);
            }
            var clientResult = await this._quickBookingSupport.ResolveClientIdAsync(
                userId,
                organizationId,
                requestedClientId,
                dto.ClientPhone,
                dto.ClientName,
                "Создан из быстрой приемки СТО");
            clientId = clientResult.ClientId;
            clientCreated = clientResult.Created;

            var modelId = QuickBookingSupport.NormalizeModelId(dto.ModelId);
            var vehicleId = await vehicleService.Create(userId, organizationId, new CreateVehicleDto
            {
                Kind = dto.Kind,
                ModelId = modelId,
                ManualBrandName = modelId.HasValue ? null : QuickBookingSupport.NormalizeNullable(dto.ManualBrandName),
                ManualModelName = modelId.HasValue ? null : QuickBookingSupport.NormalizeNullable(dto.ManualModelName),
                Name = QuickBookingSupport.NormalizeNullable(dto.VehicleText),
                RegistrationPlate = plate,
                RegistrationCountryCode = registrationCountryCode,
                AllowOtherRegistrationPlateFormat = dto.AllowOtherRegistrationPlateFormat,
                OrganizationId = organizationId,
                OrganizationClientId = clientId
            });

            vehicle = new QuickBookingVehicleDto { Id = vehicleId, OrganizationClientId = clientId };
            vehicleCreated = true;
        }

        var comment = QuickBookingSupport.NormalizeNullable(dto.Comment);
        var isSimpleWorkflow = string.Equals(
            organization.MaintenanceWorkflowMode,
            "simple",
            StringComparison.OrdinalIgnoreCase);
        var isPlannedBooking = dto.IsPlannedBooking || !isSimpleWorkflow;
        var maintenanceId = await maintenanceCreateService.Create(userId, organizationId, vehicle.Id, new CreateMaintenanceDto
        {
            Status = isPlannedBooking ? MaintenanceStatus.Planned : MaintenanceStatus.Completed,
            Type = MaintenanceType.Maintenance,
            PlannedDate = dto.PlannedDate.Date,
            PlannedStartTime = dto.PlannedStartTime,
            PlannedEndDate = dto.PlannedEndDate,
            PlannedEndTime = dto.PlannedEndTime,
            CompletedDate = isPlannedBooking ? null : dto.PlannedDate.Date,
            Location = QuickBookingSupport.NormalizeNullable(dto.Location),
            Performer = null,
            Mileage = null,
            Comment = comment,
            BuyerClientId = dto.BuyerClientId,
            HasBuyerSelection = dto.BuyerClientId.HasValue,
            Details = services,
            LinkedDefectIds = []
        });

        return new OrganizationAutoServiceQuickBookingResultDto
        {
            VehicleId = vehicle.Id,
            MaintenanceId = maintenanceId,
            OrganizationClientId = vehicle.OrganizationClientId ?? clientId,
            VehicleCreated = vehicleCreated,
            ClientCreated = clientCreated,
            VehicleUrl = $"#vehicles/{vehicle.Id:D}",
            MaintenanceUrl = $"#maintenance/{maintenanceId:D}"
        };
    }

    /// <summary> Определяет, относится ли код сценария к автосервису. </summary>
    private static bool IsAutoServiceScenario(string? code)
    {
        var normalized = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(code);
        return string.Equals(
            normalized,
            OrganizationTypeCodes.ServiceStation,
            StringComparison.OrdinalIgnoreCase);
    }
}
