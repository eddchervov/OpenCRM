using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Domain.Interfaces.Catalog;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Сервис команд управления техникой. </summary>
public sealed class VehicleCommandService : IVehicleCommandService
{
    /// <summary> Тип события истории о создании техники. </summary>
    private const string VehicleEventCreated = "vehicle_created";

    /// <summary> Тип события истории об изменении техники. </summary>
    private const string VehicleEventUpdated = "vehicle_updated";

    /// <summary> Тип события истории об отправке техники в архив. </summary>
    private const string VehicleEventArchived = "vehicle_archived";

    /// <summary> Тип события истории о восстановлении из архива. </summary>
    private const string VehicleEventRestored = "vehicle_restored";

    /// <summary> Тип события истории о передаче техники. </summary>
    private const string VehicleEventTransferred = "vehicle_transferred";

    /// <summary> Репозиторий техники. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Репозиторий моделей техники. </summary>
    private readonly IModelRepository _modelRepository;

    /// <summary> Системные часы для фиксации времени. </summary>
    private readonly ISystemClock _systemClock;

    /// <summary> Репозиторий предложений по каталогу техники. </summary>
    private readonly IVehicleCatalogSuggestionRepository _vehicleCatalogSuggestionRepository;

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Сервис подписки организации для проверок. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Записывает события в историю техники. </summary>
    private readonly VehicleHistoryWriter _historyWriter;

    /// <summary> Сервис проверки лимитов техники. </summary>
    private readonly VehicleLimitQueryService _vehicleLimits;

    /// <summary> Политика нормализации и проверки регистрационных номеров. </summary>
    private readonly IVehicleRegistrationPlatePolicy? _registrationPlatePolicy;

    /// <summary> Создаёт сервис команд техники с зависимостями. </summary>
    public VehicleCommandService(
        IVehicleRepository vehicleRepository,
        IModelRepository modelRepository,
        ISystemClock systemClock,
        IVehicleCatalogSuggestionRepository vehicleCatalogSuggestionRepository,
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService,
        VehicleHistoryWriter historyWriter,
        VehicleLimitQueryService vehicleLimits,
        IVehicleRegistrationPlatePolicy? registrationPlatePolicy = null)
    {
        this._vehicleRepository = vehicleRepository;
        this._modelRepository = modelRepository;
        this._systemClock = systemClock;
        this._vehicleCatalogSuggestionRepository = vehicleCatalogSuggestionRepository;
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
        this._historyWriter = historyWriter;
        this._vehicleLimits = vehicleLimits;
        this._registrationPlatePolicy = registrationPlatePolicy;
    }

    /// <summary> Создаёт технику пользователя. </summary>
    public async Task<Guid> Create(string userId, CreateVehicleDto dto)
    {
        return await this.Create(userId, dto.OrganizationId, dto);
    }

    /// <summary> Создаёт технику в рабочем пространстве организации или личном. </summary>
    public async Task<Guid> Create(string userId, Guid? organizationId, CreateVehicleDto dto)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesCreate);

        if (!organizationId.HasValue)
        {
            if (!await this._vehicleLimits.CanUserAddVehicleAsync(userId, organizationId))
            {
                await this.ThrowVehicleLimitExceededAsync(userId, organizationId);
            }

            return await this.CreateCoreAsync(userId, organizationId, dto);
        }

        return await this.ExecuteOrganizationTransactionAsync(
            organizationId.Value,
            async () =>
            {
                await this.EnsureVehicleLimitAsync(userId, organizationId);

                return await this.CreateCoreAsync(userId, organizationId, dto);
            },
            vehicleId => this._context.Vehicles.AsNoTracking()
                .AnyAsync(vehicle => vehicle.Id == vehicleId && vehicle.OrganizationId == organizationId.Value));
    }

    private async Task<Guid> CreateCoreAsync(string userId, Guid? organizationId, CreateVehicleDto dto)
    {
        VehicleCommandService.ValidateYearIssue(dto.YearIssue);

        var normalizedName = VehicleCommandService.NormalizeManualValue(dto.Name);
        var normalizedVin = VehicleCommandService.NormalizeManualValue(dto.Vin);
        var normalizedRegistrationPlate = this.NormalizeAndValidateRegistrationPlate(dto.RegistrationPlate, dto.Kind, dto.AllowOtherRegistrationPlateFormat);
        var normalizedBodyNumber = VehicleCommandService.NormalizeManualValue(dto.BodyNumber);
        var normalizedEngineNumber = VehicleCommandService.NormalizeManualValue(dto.EngineNumber);
        var normalizedColor = VehicleCommandService.NormalizeManualValue(dto.Color);
        var normalizedRegistrationCertificate = VehicleCommandService.NormalizeManualValue(dto.RegistrationCertificate);
        var normalizedDescription = VehicleCommandService.NormalizeManualValue(dto.Description);
        var normalizedBrand = VehicleCommandService.NormalizeManualValue(dto.ManualBrandName);
        var normalizedModel = VehicleCommandService.NormalizeManualValue(dto.ManualModelName);

        VehicleCommandService.ValidateSafeText("Бренд", normalizedBrand);
        VehicleCommandService.ValidateSafeText("Модель", normalizedModel);
        VehicleCommandService.ValidateSafeText("Имя / номер техники", normalizedName);
        VehicleCommandService.ValidateSafeText("Цвет", normalizedColor);
        VehicleCommandService.ValidateSafeText("Описание", normalizedDescription);
        VehicleCommandService.ValidateVehicleIdentifier(normalizedName, normalizedVin, normalizedRegistrationPlate, normalizedBodyNumber, normalizedEngineNumber, normalizedRegistrationCertificate);
        VehicleCommandService.ValidateVin(normalizedVin);
        var (brandId, modelId) = await ResolveCatalogSelectionAsync(dto.ModelId, dto.Kind);
        var organizationClientId = await this.NormalizeOrganizationClientIdAsync(organizationId, dto.OrganizationClientId);

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            Kind = dto.Kind,
            MileageUnit = dto.MileageUnit,
            ModelId = modelId,
            Name = normalizedName,
            Vin = normalizedVin,
            RegistrationPlate = normalizedRegistrationPlate,
            BodyNumber = normalizedBodyNumber,
            EngineNumber = normalizedEngineNumber,
            Color = normalizedColor,
            RegistrationCertificate = normalizedRegistrationCertificate,
            CommissioningDate = dto.CommissioningDate,
            BrandId = brandId,
            DateTimeCreation = this._systemClock.UtcNow,
            Description = normalizedDescription,
            UserId = userId,
            OrganizationId = organizationId,
            OrganizationClientId = organizationClientId,
            YearIssue = dto.YearIssue,
            ManualBrandName = modelId.HasValue ? null : normalizedBrand,
            ManualModelName = modelId.HasValue ? null : normalizedModel
        };

        await this._vehicleRepository.Add(vehicle);
        await this._vehicleRepository.Save();
        await this._historyWriter.AddAsync(
            userId,
            organizationId,
            vehicle.Id,
            VehicleCommandService.VehicleEventCreated,
            "Техника создана",
            VehicleCommandService.BuildVehicleSummary(vehicle),
            null,
            null);
        await this.TryQueueCatalogSuggestionAsync(userId, dto.Kind, vehicle.ManualBrandName, vehicle.ManualModelName);

        return vehicle.Id;
    }

    /// <summary> Обновляет данные техники пользователя. </summary>
    public async Task Update(string userId, Guid vehicleId, UpdateVehicleDto dto)
    {
        await this.Update(userId, null, vehicleId, dto);
    }

    /// <summary> Обновляет данные техники с учётом организации. </summary>
    public async Task Update(string userId, Guid? organizationId, Guid vehicleId, UpdateVehicleDto dto)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesEdit);
        VehicleCommandService.ValidateYearIssue(dto.YearIssue);

        var normalizedName = VehicleCommandService.NormalizeManualValue(dto.Name);
        var normalizedVin = VehicleCommandService.NormalizeManualValue(dto.Vin);
        var requestedRegistrationPlate = VehicleCommandService.NormalizeManualValue(dto.RegistrationPlate);
        var normalizedBodyNumber = VehicleCommandService.NormalizeManualValue(dto.BodyNumber);
        var normalizedEngineNumber = VehicleCommandService.NormalizeManualValue(dto.EngineNumber);
        var normalizedColor = VehicleCommandService.NormalizeManualValue(dto.Color);
        var normalizedRegistrationCertificate = VehicleCommandService.NormalizeManualValue(dto.RegistrationCertificate);
        var normalizedDescription = VehicleCommandService.NormalizeManualValue(dto.Description);
        var normalizedBrand = VehicleCommandService.NormalizeManualValue(dto.ManualBrandName);
        var normalizedModel = VehicleCommandService.NormalizeManualValue(dto.ManualModelName);

        VehicleCommandService.ValidateSafeText("Бренд", normalizedBrand);
        VehicleCommandService.ValidateSafeText("Модель", normalizedModel);
        VehicleCommandService.ValidateSafeText("Имя / номер техники", normalizedName);
        VehicleCommandService.ValidateSafeText("Цвет", normalizedColor);
        VehicleCommandService.ValidateSafeText("Описание", normalizedDescription);
        VehicleCommandService.ValidateVehicleIdentifier(normalizedName, normalizedVin, requestedRegistrationPlate, normalizedBodyNumber, normalizedEngineNumber, normalizedRegistrationCertificate);
        VehicleCommandService.ValidateVin(normalizedVin);
        var (brandId, modelId) = await ResolveCatalogSelectionAsync(dto.ModelId, dto.Kind);
        var organizationClientId = await this.NormalizeOrganizationClientIdAsync(organizationId, dto.OrganizationClientId);

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new Exception($"Не найдена техника с vehicleId={vehicleId} и userId={userId}");

        var registrationPlateIsUnchanged = dto.Kind == vehicle.Kind &&
            this.RegistrationPlatesAreEquivalent(
                dto.RegistrationPlate,
                vehicle.RegistrationPlate,
                dto.Kind,
                dto.AllowOtherRegistrationPlateFormat);
        var normalizedRegistrationPlate = registrationPlateIsUnchanged
            ? vehicle.RegistrationPlate
            : this.NormalizeAndValidateRegistrationPlate(dto.RegistrationPlate, dto.Kind, dto.AllowOtherRegistrationPlateFormat);

        var previous = VehicleSnapshot.From(vehicle);

        vehicle.Kind = dto.Kind;
        vehicle.MileageUnit = dto.MileageUnit;
        vehicle.ModelId = modelId;
        vehicle.Name = normalizedName;
        vehicle.Vin = normalizedVin;
        vehicle.RegistrationPlate = normalizedRegistrationPlate;
        vehicle.BodyNumber = normalizedBodyNumber;
        vehicle.EngineNumber = normalizedEngineNumber;
        vehicle.Color = normalizedColor;
        vehicle.RegistrationCertificate = normalizedRegistrationCertificate;
        vehicle.CommissioningDate = dto.CommissioningDate;
        vehicle.BrandId = brandId;
        vehicle.Description = normalizedDescription;
        vehicle.OrganizationClientId = organizationClientId;
        vehicle.YearIssue = dto.YearIssue;
        vehicle.ManualBrandName = modelId.HasValue ? null : normalizedBrand;
        vehicle.ManualModelName = modelId.HasValue ? null : normalizedModel;

        this._vehicleRepository.Update(vehicle);
        await this._vehicleRepository.Save();
        var changes = VehicleCommandService.BuildVehicleChanges(previous, vehicle);
        if (changes.Count > 0)
        {
            await this._historyWriter.AddAsync(
                userId,
                organizationId,
                vehicle.Id,
                VehicleCommandService.VehicleEventUpdated,
                "Техника изменена",
                string.Join("\n", changes),
                null,
                null);
        }

        await this.TryQueueCatalogSuggestionAsync(userId, dto.Kind, vehicle.ManualBrandName, vehicle.ManualModelName);
    }

    /// <summary> Привязывает клиента без перезаписи прежнего владельца и остальных данных автомобиля. </summary>
    public async Task AssignClientIfUnlinkedAsync(string userId, Guid organizationId, Guid vehicleId, Guid clientId)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesEdit);
        await this.ExecuteOrganizationTransactionAsync(organizationId, async () =>
        {
            await this.NormalizeOrganizationClientIdAsync(organizationId, clientId);
            var vehicle = await this._context.Vehicles.AsNoTracking().SingleOrDefaultAsync(value =>
                value.Id == vehicleId && value.OrganizationId == organizationId && !value.IsDeleted)
                ?? throw new InvalidOperationException("Выбранный автомобиль не найден в организации.");
            if (vehicle.OrganizationClientId == clientId)
            {
                return vehicleId;
            }

            if (vehicle.OrganizationClientId.HasValue)
            {
                throw new InvalidOperationException("У автомобиля уже указан другой клиент. Измените привязку в карточке автомобиля.");
            }

            var affected = await this._context.Vehicles.Where(value => value.Id == vehicleId
                && value.OrganizationId == organizationId && !value.IsDeleted && value.OrganizationClientId == null)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.OrganizationClientId, clientId));
            if (affected != 1)
            {
                throw new InvalidOperationException("Клиент автомобиля изменился. Обновите данные и повторите запись.");
            }

            var tracked = this._context.ChangeTracker.Entries<Vehicle>().FirstOrDefault(entry => entry.Entity.Id == vehicleId);
            if (tracked != null)
            {
                var property = tracked.Property(value => value.OrganizationClientId);
                property.CurrentValue = clientId;
                property.OriginalValue = clientId;
            }

            await this._historyWriter.AddAsync(userId, organizationId, vehicleId, VehicleEventUpdated,
                "Клиент автомобиля указан", $"Клиент: {clientId:D}", "client", clientId);

            return vehicleId;
        }, id => this._context.Vehicles.AsNoTracking().AnyAsync(value => value.Id == id
            && value.OrganizationId == organizationId && value.OrganizationClientId == clientId));
    }

    /// <summary> Отправляет технику пользователя в архив. </summary>
    public async Task Archive(string userId, Guid vehicleId)
    {
        await this.Archive(userId, null, vehicleId);
    }

    /// <summary> Отправляет технику в архив с учётом организации. </summary>
    public async Task Archive(string userId, Guid? organizationId, Guid vehicleId)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesArchive);

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new Exception($"Не найдена техника с vehicleId={vehicleId} и userId={userId}");

        vehicle.Archive(this._systemClock.UtcNow);
        this._vehicleRepository.Update(vehicle);
        await this._vehicleRepository.Save();
        await this._historyWriter.AddAsync(
            userId,
            organizationId,
            vehicle.Id,
            VehicleCommandService.VehicleEventArchived,
            "Техника отправлена в архив",
            null,
            null,
            null);
    }

    /// <summary> Восстанавливает технику пользователя из архива. </summary>
    public async Task RestoreFromArchive(string userId, Guid vehicleId)
    {
        await this.RestoreFromArchive(userId, null, vehicleId);
    }

    /// <summary> Восстанавливает технику из архива с учётом организации. </summary>
    public async Task RestoreFromArchive(string userId, Guid? organizationId, Guid vehicleId)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesArchive);

        if (!organizationId.HasValue
            || !(await this._organizationSubscriptionService.GetWorkspaceAccessAsync(userId, organizationId.Value)).IsFreePlan)
        {
            if (!await this._vehicleLimits.CanUserAddVehicleAsync(userId, organizationId))
            {
                await this.ThrowVehicleLimitExceededAsync(userId, organizationId);
            }
        }

        var vehicle = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new Exception($"Не найдена техника с vehicleId={vehicleId} и userId={userId}");

        if (!vehicle.IsArchived)
        {
            throw new InvalidOperationException("Техника не находится в архиве");
        }

        vehicle.Restore();
        this._vehicleRepository.Update(vehicle);
        await this._vehicleRepository.Save();
        await this._historyWriter.AddAsync(
            userId,
            organizationId,
            vehicle.Id,
            VehicleCommandService.VehicleEventRestored,
            "Техника восстановлена из архива",
            null,
            null,
            null);
    }

    /// <summary> Передаёт технику в организацию. </summary>
    public async Task TransferToOrganizationAsync(string userId, Guid vehicleId, Guid organizationId)
    {
        await this.EnsureCanWriteWorkspaceAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesCreate);

        await this.ExecuteOrganizationTransactionAsync(
            organizationId,
            async () =>
            {
                await this.EnsureVehicleLimitAsync(userId, organizationId);
                var vehicle = await this._vehicleRepository.Get(userId, null, vehicleId)
                    ?? throw new Exception($"Не найдена личная техника с vehicleId={vehicleId} и userId={userId}");

                if (vehicle.IsArchived)
                {
                    throw new InvalidOperationException("Архивную технику нельзя перенести в рабочее пространство. Сначала восстановите ее.");
                }

                vehicle.OrganizationId = organizationId;
                vehicle.OrganizationClientId = null;
                this._vehicleRepository.Update(vehicle);
                await this._vehicleRepository.Save();
                await this._historyWriter.AddAsync(
                    userId,
                    organizationId,
                    vehicle.Id,
                    VehicleCommandService.VehicleEventTransferred,
                    "Техника перенесена в рабочее пространство",
                    null,
                    null,
                    null);

                return vehicle.Id;
            },
            transferredVehicleId => this._context.Vehicles.AsNoTracking()
                .AnyAsync(vehicle => vehicle.Id == transferredVehicleId && vehicle.OrganizationId == organizationId));
    }

    /// <summary> Очищает истекшие записи архива техники. </summary>
    public async Task CleanUpArchive()
    {
        var vehicles = await this._vehicleRepository.GetArchived();
        foreach (var vehicle in vehicles)
        {
            if (!vehicle.ArchiveDate.HasValue)
            {
                continue;
            }

            if (!VehicleArchivePolicy.IsExpired(vehicle.ArchiveDate.Value, this._systemClock.UtcNow))
            {
                continue;
            }

            vehicle.Delete(this._systemClock.UtcNow);
            this._vehicleRepository.Update(vehicle);
            await this._vehicleRepository.Save();
        }
    }

    private async Task EnsureVehicleLimitAsync(string userId, Guid? organizationId)
    {
        if (!await this._vehicleLimits.CanUserAddVehicleAsync(userId, organizationId))
        {
            await this.ThrowVehicleLimitExceededAsync(userId, organizationId);
        }
    }

    private async Task ThrowVehicleLimitExceededAsync(string userId, Guid? organizationId)
    {
        var currentCount = await this._vehicleLimits.GetWorkspaceVehicleCountAsync(userId, organizationId);
        var workspaceLimit = await this._vehicleLimits.GetWorkspaceVehicleLimitAsync(userId, organizationId);
        throw new InvalidOperationException($"Превышен лимит техники. У вас {currentCount} из {workspaceLimit} ед. техники.");
    }

    private async Task<Guid> ExecuteOrganizationTransactionAsync(
        Guid organizationId,
        Func<Task<Guid>> action,
        Func<Guid, Task<bool>> verifySucceeded)
    {
        if (this._context.Database.CurrentTransaction is not null)
        {
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR NO KEY UPDATE");

            return await action();
        }

        var isRetry = false;
        Guid resultId = Guid.Empty;
        await this._context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                this._context.ChangeTracker.Clear();
            }

            isRetry = true;
            await this._context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR NO KEY UPDATE");
            resultId = await action();
        }, () => verifySucceeded(resultId));

        return resultId;
    }

    /// <summary> Строит список изменений техники по сравнению со снимком. </summary>
    private static List<string> BuildVehicleChanges(VehicleSnapshot previous, Vehicle vehicle)
    {
        var changes = new List<string>();

        VehicleCommandService.AddChange(changes, "Тип", VehicleCommandService.GetVehicleKindText(previous.Kind), VehicleCommandService.GetVehicleKindText(vehicle.Kind));
        VehicleCommandService.AddChange(changes, "Учет пробега/наработки", VehicleCommandService.GetMileageUnitText(previous.MileageUnit), VehicleCommandService.GetMileageUnitText(vehicle.MileageUnit));
        VehicleCommandService.AddChange(changes, "Бренд", previous.BrandName, VehicleCommandService.GetVehicleBrandName(vehicle));
        VehicleCommandService.AddChange(changes, "Модель", previous.ModelName, VehicleCommandService.GetVehicleModelName(vehicle));
        VehicleCommandService.AddChange(changes, "Название", previous.Name, vehicle.Name);
        VehicleCommandService.AddChange(changes, "Год выпуска", VehicleCommandService.FormatNullableNumber(previous.YearIssue), VehicleCommandService.FormatNullableNumber(vehicle.YearIssue));
        VehicleCommandService.AddChange(changes, "VIN/серийный номер", previous.Vin, vehicle.Vin);
        VehicleCommandService.AddChange(changes, "Госномер", previous.RegistrationPlate, vehicle.RegistrationPlate);
        VehicleCommandService.AddChange(changes, "Номер кузова/рамы", previous.BodyNumber, vehicle.BodyNumber);
        VehicleCommandService.AddChange(changes, "Номер двигателя", previous.EngineNumber, vehicle.EngineNumber);
        VehicleCommandService.AddChange(changes, "Цвет", previous.Color, vehicle.Color);
        VehicleCommandService.AddChange(changes, "Регистрационный документ", previous.RegistrationCertificate, vehicle.RegistrationCertificate);
        VehicleCommandService.AddChange(changes, "Дата ввода в эксплуатацию", VehicleCommandService.FormatDate(previous.CommissioningDate), VehicleCommandService.FormatDate(vehicle.CommissioningDate));
        VehicleCommandService.AddChange(changes, "Описание", previous.Description, vehicle.Description);

        return changes;
    }

    /// <summary> Добавляет описание изменения поля в список. </summary>
    private static void AddChange(List<string> changes, string fieldName, string? previousValue, string? currentValue)
    {
        var previousDisplay = VehicleCommandService.FormatNullableText(previousValue);
        var currentDisplay = VehicleCommandService.FormatNullableText(currentValue);
        if (string.Equals(previousDisplay, currentDisplay, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add($"{fieldName}: {previousDisplay} -> {currentDisplay}");
    }

    /// <summary> Формирует краткое описание техники. </summary>
    private static string BuildVehicleSummary(Vehicle vehicle)
    {
        var title = Helpers.VehicleDisplayName.Format(VehicleCommandService.GetVehicleBrandName(vehicle), VehicleCommandService.GetVehicleModelName(vehicle), vehicle.RegistrationPlate);
        return string.IsNullOrWhiteSpace(vehicle.Name)
            ? title
            : $"{title}; Название: {vehicle.Name}";
    }

    /// <summary> Возвращает название марки техники. </summary>
    private static string GetVehicleBrandName(Vehicle vehicle) => vehicle.BrandId.HasValue
        ? vehicle.Brand?.Name ?? string.Empty
        : vehicle.ManualBrandName ?? string.Empty;

    /// <summary> Возвращает название модели техники. </summary>
    private static string GetVehicleModelName(Vehicle vehicle) => vehicle.ModelId.HasValue
        ? vehicle.Model?.Name ?? string.Empty
        : vehicle.ManualModelName ?? string.Empty;

    /// <summary> Возвращает текст вида техники. </summary>
    private static string GetVehicleKindText(VehicleKind kind) => kind switch
    {
        VehicleKind.PassengerCar => "Легковой автомобиль",
        VehicleKind.Truck => "Грузовая техника",
        VehicleKind.Motorcycle => "Мотоцикл",
        VehicleKind.ATV => "Квадроцикл",
        VehicleKind.UTV => "Багги / UTV",
        VehicleKind.Snowmobile => "Снегоход",
        VehicleKind.JetSki => "Гидроцикл",
        VehicleKind.Boat => "Лодка / катер",
        VehicleKind.SpecialEquipment => "Спецтехника",
        VehicleKind.Trailer => "Прицеп",
        VehicleKind.Other => "Другая техника",
        _ => "Техника"
    };

    /// <summary> Возвращает текст единицы пробега. </summary>
    private static string GetMileageUnitText(VehicleMileageUnit unit) => unit == VehicleMileageUnit.EngineHours ? "Моточасы" : "Километры";

    /// <summary> Форматирует дату для описания истории. </summary>
    private static string FormatDate(DateTime? value) => value.HasValue ? value.Value.Date.ToString("dd.MM.yyyy") : "не указано";

    /// <summary> Форматирует необязательное число для истории. </summary>
    private static string FormatNullableNumber(int? value) => value.HasValue ? value.Value.ToString() : "не указано";

    /// <summary> Форматирует необязательный текст для истории. </summary>
    private static string FormatNullableText(string? value) => string.IsNullOrWhiteSpace(value) ? "не указано" : value.Trim();

    /// <summary> Снимок основных полей техники для фиксации изменений. </summary>
    private sealed record VehicleSnapshot(
        VehicleKind Kind,
        VehicleMileageUnit MileageUnit,
        string? BrandName,
        string? ModelName,
        string? Name,
        int? YearIssue,
        string? Vin,
        string? RegistrationPlate,
        string? BodyNumber,
        string? EngineNumber,
        string? Color,
        string? RegistrationCertificate,
        DateTime? CommissioningDate,
        string? Description)
    {
        /// <summary> Создаёт снимок текущего состояния техники. </summary>
        public static VehicleSnapshot From(Vehicle vehicle)
        {
            return new VehicleSnapshot(
                vehicle.Kind,
                vehicle.MileageUnit,
                VehicleCommandService.GetVehicleBrandName(vehicle),
                VehicleCommandService.GetVehicleModelName(vehicle),
                vehicle.Name,
                vehicle.YearIssue,
                vehicle.Vin,
                vehicle.RegistrationPlate,
                vehicle.BodyNumber,
                vehicle.EngineNumber,
                vehicle.Color,
                vehicle.RegistrationCertificate,
                vehicle.CommissioningDate?.Date,
                vehicle.Description);
        }
    }

    /// <summary> Разрешает выбор марки и модели из каталога или ручного ввода. </summary>
    private async Task<(int? brandId, int? modelId)> ResolveCatalogSelectionAsync(int? modelId, VehicleKind vehicleKind)
    {
        if (modelId.HasValue)
        {
            var model = await this._modelRepository.Get(modelId.Value) ?? throw new Exception($"Не найдена модель с ид={modelId}");
            if (model.Kind != vehicleKind)
            {
                throw new InvalidOperationException("Выбранная модель не соответствует типу техники.");
            }

            if (model.Brand.Kind != vehicleKind)
            {
                throw new InvalidOperationException("Бренд выбранной модели не соответствует типу техники.");
            }

            return (model.BrandId, model.Id);
        }

        return (null, null);
    }

    /// <summary> Нормализует значение ручного ввода. </summary>
    private static string? NormalizeManualValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : VehicleCommandService.CollapseWhitespace(value.Trim());

    /// <summary> Схлопывает повторяющиеся пробелы в строке. </summary>
    private static string CollapseWhitespace(string value) => Regex.Replace(value, @"\s+", " ");

    /// <summary> Проверяет текстовое значение на недопустимое содержимое. </summary>
    private static void ValidateSafeText(string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Contains('<') || value.Contains('>') || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{fieldName}: HTML-теги и скрипты не допускаются.");
        }
    }

    /// <summary> Проверяет корректность VIN. </summary>
    private static void ValidateVin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Regex.IsMatch(value, "^[A-Za-z0-9]{5,32}$"))
        {
            throw new InvalidOperationException("VIN/серийный номер должен содержать от 5 до 32 латинских букв и цифр.");
        }
    }

    /// <summary> Проверяет корректность госномера. </summary>
    private static void ValidateRegistrationPlate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Regex.IsMatch(value, "^[A-Za-zА-Яа-яЁё0-9\\-\\s]{2,16}$"))
        {
            throw new InvalidOperationException("Госномер должен содержать от 2 до 16 букв, цифр, пробелов или дефисов.");
        }
    }

    private string? NormalizeAndValidateRegistrationPlate(string? value, VehicleKind kind, bool allowOtherFormat)
    {
        if (this._registrationPlatePolicy is null)
        {
            var normalized = VehicleCommandService.NormalizeManualValue(value);
            VehicleCommandService.ValidateRegistrationPlate(normalized);

            return normalized;
        }

        var allowAlternateFormat = allowOtherFormat || !this._registrationPlatePolicy.GetFormats().Formats
            .Any(format => format.Kinds.Contains((int)kind));
        var normalizedWithPolicy = this._registrationPlatePolicy.Normalize(value, allowAlternateFormat);
        this._registrationPlatePolicy.Validate(normalizedWithPolicy, kind, allowAlternateFormat);

        return normalizedWithPolicy;
    }

    private bool RegistrationPlatesAreEquivalent(string? first, string? second, VehicleKind kind, bool allowOtherFormat)
    {
        if (string.Equals(first, second, StringComparison.Ordinal))
        {
            return true;
        }

        if (this._registrationPlatePolicy is null)
        {
            return string.Equals(
                VehicleCommandService.NormalizeManualValue(first),
                VehicleCommandService.NormalizeManualValue(second),
                StringComparison.OrdinalIgnoreCase);
        }

        var allowAlternateFormat = allowOtherFormat || !this._registrationPlatePolicy.GetFormats().Formats
            .Any(format => format.Kinds.Contains((int)kind));
        if (allowAlternateFormat)
        {
            return string.Equals(first?.Trim(), second?.Trim(), StringComparison.Ordinal);
        }

        try
        {
            return string.Equals(
                this._registrationPlatePolicy.Normalize(first),
                this._registrationPlatePolicy.Normalize(second),
                StringComparison.Ordinal);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary> Проверяет наличие хотя бы одного идентификатора техники. </summary>
    private static void ValidateVehicleIdentifier(params string?[] identifiers)
    {
        if (identifiers.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            return;
        }

        throw new InvalidOperationException("Укажите хотя бы один идентификатор техники: госномер, VIN, номер кузова, номер двигателя, СТС или внутренний номер.");
    }

    /// <summary> Проверяет корректность года выпуска. </summary>
    private static void ValidateYearIssue(int? yearIssue)
    {
        if (!yearIssue.HasValue)
        {
            return;
        }

        var currentYear = DateTime.UtcNow.Year;
        if (yearIssue.Value < 1900 || yearIssue.Value > currentYear)
        {
            throw new InvalidOperationException($"Год выпуска должен быть в диапазоне от 1900 до {currentYear}.");
        }
    }

    /// <summary> Ставит в очередь предложение каталога по ручному вводу марки и модели. </summary>
    private async Task TryQueueCatalogSuggestionAsync(string userId, VehicleKind kind, string? manualBrandName, string? manualModelName)
    {
        if (string.IsNullOrWhiteSpace(manualBrandName) || string.IsNullOrWhiteSpace(manualModelName))
        {
            return;
        }

        if (await this._vehicleCatalogSuggestionRepository.ExistsInCatalogAsync(kind, manualBrandName, manualModelName))
        {
            return;
        }

        var duplicate = await this._vehicleCatalogSuggestionRepository.FindOpenDuplicateAsync(kind, manualBrandName, manualModelName);
        if (duplicate != null)
        {
            return;
        }

        await this._vehicleCatalogSuggestionRepository.AddAsync(new VehicleCatalogSuggestion
        {
            Kind = kind,
            BrandName = manualBrandName,
            ModelName = manualModelName,
            SuggestedByUserId = userId,
            CreatedAt = this._systemClock.UtcNow,
            Status = VehicleCatalogSuggestionStatus.Pending
        });

        await this._vehicleCatalogSuggestionRepository.SaveAsync();
    }

    /// <summary> Проверяет право пользователя изменять данные рабочего пространства. </summary>
    private async Task EnsureCanWriteWorkspaceAsync(
        string userId,
        Guid? organizationId,
        string permissionCode = OrganizationPermissionCodes.VehiclesEdit)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await this._organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId.Value, permissionCode);
    }

    /// <summary> Нормализует идентификатор клиента организации. </summary>
    private async Task<Guid?> NormalizeOrganizationClientIdAsync(Guid? organizationId, Guid? organizationClientId)
    {
        if (!organizationId.HasValue)
        {
            if (organizationClientId.HasValue)
            {
                throw new InvalidOperationException("Клиента можно привязать только внутри рабочего пространства.");
            }

            return null;
        }

        if (!organizationClientId.HasValue)
        {
            return null;
        }

        var exists = await this._context.OrganizationClients
            .AsNoTracking()
            .AnyAsync(x => x.Id == organizationClientId.Value
                && x.OrganizationId == organizationId.Value
                && !x.IsDeleted);

        if (!exists)
        {
            throw new InvalidOperationException("Выбранный клиент не найден в этом рабочем пространстве.");
        }

        return organizationClientId.Value;
    }
}
