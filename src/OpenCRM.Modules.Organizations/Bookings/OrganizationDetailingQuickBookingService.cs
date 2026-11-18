using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
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

/// <summary> Сервис быстрой записи клиента в детейлинг. </summary>
public sealed class OrganizationDetailingQuickBookingService(
    OpenCRMContext context,
    IQuickBookingClientLookupService clientLookup,
    IQuickBookingVehicleLookupService vehicleLookup,
    IOrganizationQueryService organizationService,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationClientCommandService organizationClientService,
    IVehicleCommandService vehicleService,
    IMaintenanceCreateService maintenanceCreateService,
    ICatalogQueryService scenarioCatalogService,
    IVehicleRegistrationPlatePolicy? registrationPlates = null)
    : IOrganizationDetailingQuickBookingService
{
    private readonly OrganizationTransaction _transaction = new(context);

    /// <summary> Общая логика подготовки данных быстрой записи. </summary>
    private readonly QuickBookingSupport _quickBookingSupport = new(clientLookup, organizationClientService, scenarioCatalogService);

    /// <summary> Создаёт быструю запись в детейлинг. </summary>
    public async Task<OrganizationDetailingQuickBookingResultDto> CreateAsync(
        string userId,
        Guid organizationId,
        CreateOrganizationDetailingQuickBookingDto dto)
    {
        var organization = await organizationService.GetByIdAsync(userId, organizationId);
        if (!OrganizationDetailingQuickBookingService.IsDetailingScenario(organization.PrimaryScenarioCode))
        {
            throw new InvalidOperationException("Быстрая запись по госномеру доступна только для сценария \"Детейлинг\".");
        }

        await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.MaintenanceCreate);

        var savedMaintenanceId = Guid.Empty;

        return await this._transaction.ExecuteAsync(async () =>
        {
            await this._transaction.LockOrganizationAsync(organizationId);
            var result = await this.CreateCoreAsync(userId, organizationId, organization, dto);
            savedMaintenanceId = result.MaintenanceId;

            return result;
        }, () => context.Maintenance.AsNoTracking().AnyAsync(value => value.Id == savedMaintenanceId && value.Vehicle.OrganizationId == organizationId));
    }

    private async Task<OrganizationDetailingQuickBookingResultDto> CreateCoreAsync(
        string userId,
        Guid organizationId,
        OpenCRM.Modules.Organizations.Contracts.Dtos.OrganizationDto organization,
        CreateOrganizationDetailingQuickBookingDto dto)
    {
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
        if (string.IsNullOrWhiteSpace(normalizedPlate))
        {
            throw new InvalidOperationException("Укажите госномер авто для быстрой записи.");
        }

        var requestedClientId = dto.OrganizationClientId;
        QuickBookingVehicleDto? vehicle;
        if (dto.VehicleId.HasValue)
        {
            vehicle = await vehicleLookup.GetSelectedAsync(userId, organizationId, dto.VehicleId.Value)
                ?? throw new InvalidOperationException("Выбранный автомобиль не найден в организации.");
            var plateMatches = otherFormat || vehicle.RegistrationCountryCode is not null and not "RU"
                ? string.Equals(dto.RegistrationPlate.Trim(), vehicle.RegistrationPlate?.Trim(), StringComparison.Ordinal)
                : normalizedPlate == vehicleLookup.NormalizePlateForSearch(vehicle.RegistrationPlate);
            if (!string.IsNullOrEmpty(registrationCountryCode)
                && registrationCountryCode != (vehicle.RegistrationCountryCode ?? "RU"))
            {
                throw new InvalidOperationException("Страна регистрации выбранного автомобиля изменилась. Выберите автомобиль заново.");
            }

            if (vehicle.Kind != dto.Kind || !plateMatches)
            {
                throw new InvalidOperationException("Выбранный автомобиль не соответствует госномеру или типу техники. Выберите автомобиль заново.");
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(registrationCountryCode))
            {
                vehicle = await vehicleLookup.FindByPlateAsync(
                    userId, organizationId, plate, null, dto.Kind, otherFormat, registrationCountryCode);
            }
            else if (otherFormat)
            {
                vehicle = await vehicleLookup.FindByPlateAsync(userId, organizationId, plate, null, dto.Kind, allowOtherFormat: true);
            }
            else
            {
                vehicle = await vehicleLookup.FindByPlateAsync(userId, organizationId, plate, null, dto.Kind);
            }
        }

        var vehicleCreated = false;
        var clientCreated = false;
        Guid? clientId = vehicle?.OrganizationClientId;
        var hasContact = requestedClientId.HasValue || !string.IsNullOrWhiteSpace(dto.ClientPhone)
            || !string.IsNullOrWhiteSpace(dto.ClientName);

        if (vehicle?.OrganizationClientId is Guid previousClientId && hasContact)
        {
            var contactId = requestedClientId ?? await clientLookup.FindExistingAsync(userId, organizationId, dto.ClientPhone, dto.ClientName);
            if (contactId != previousClientId)
            {
                throw new InvalidOperationException("У автомобиля уже указан другой клиент. Выберите его или измените привязку в карточке автомобиля.");
            }

            if (!await clientLookup.ExistsAsync(userId, organizationId, previousClientId))
            {
                throw new InvalidOperationException("Клиент автомобиля недоступен. Обновите привязку в карточке автомобиля.");
            }
        }
        else if (vehicle != null && hasContact)
        {
            var resolved = await this._quickBookingSupport.ResolveClientIdAsync(userId, organizationId,
                requestedClientId, dto.ClientPhone, dto.ClientName, "Создан из быстрой записи детейлинга");
            clientId = resolved.ClientId;
            clientCreated = resolved.Created;
            if (clientId.HasValue)
            {
                await vehicleService.AssignClientIfUnlinkedAsync(userId, organizationId, vehicle.Id, clientId.Value);
                vehicle.OrganizationClientId = clientId;
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
                "Создан из быстрой записи детейлинга");
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
            Comment = QuickBookingSupport.NormalizeNullable(dto.Comment),
            Details = services,
            LinkedDefectIds = []
        });

        return new OrganizationDetailingQuickBookingResultDto
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

    /// <summary> Определяет, относится ли код сценария к детейлингу. </summary>
    private static bool IsDetailingScenario(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code)
            ? OrganizationTypeCodes.ServiceStation
            : code.Trim().ToLowerInvariant();

        return normalized == OrganizationTypeCodes.Detailing;
    }
}
