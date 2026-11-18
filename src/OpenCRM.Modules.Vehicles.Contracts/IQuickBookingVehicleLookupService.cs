using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary>
/// Выполняет поиск техники для быстрой записи после проверки права на создание обслуживания.
/// Поиск не изменяет данные и не управляет транзакцией.
/// </summary>
public interface IQuickBookingVehicleLookupService
{
    /// <summary>Нормализует госномер для сопоставления латинских и кириллических символов.</summary>
    string NormalizePlateForSearch(string? value);

    /// <summary>Ищет не удалённую технику по номеру в указанной организации.</summary>
    Task<QuickBookingVehicleDto?> FindByPlateAsync(
        string userId,
        Guid organizationId,
        string? plate,
        Guid? clientId,
        VehicleKind kind);

    /// <summary> Ищет технику с явным выбором свободного формата номера. </summary>
    Task<QuickBookingVehicleDto?> FindByPlateAsync(
        string userId,
        Guid organizationId,
        string? plate,
        Guid? clientId,
        VehicleKind kind,
        bool allowOtherFormat);

    /// <summary> Ищет технику с учётом явно выбранной страны регистрации. </summary>
    Task<QuickBookingVehicleDto?> FindByPlateAsync(
        string userId,
        Guid organizationId,
        string? plate,
        Guid? clientId,
        VehicleKind kind,
        bool allowOtherFormat,
        string? registrationCountryCode);

    /// <summary>Возвращает выбранную, не удалённую технику указанной организации.</summary>
    Task<QuickBookingVehicleDto?> GetSelectedAsync(string userId, Guid organizationId, Guid vehicleId);
}
