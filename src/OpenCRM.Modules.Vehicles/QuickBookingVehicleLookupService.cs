using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Поиск техники для быстрого создания записи. </summary>
public sealed class QuickBookingVehicleLookupService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptions,
    IVehicleRegistrationPlatePolicy? registrationPlates = null) : IQuickBookingVehicleLookupService
{
    /// <summary> Контекст базы данных для поиска техники. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Сервис подписки организации для проверок доступа. </summary>
    private readonly IOrganizationSubscriptionService _subscriptions = subscriptions;

    /// <summary> Ищет технику по госномеру с фильтрами. </summary>
    public async Task<QuickBookingVehicleDto?> FindByPlateAsync(
        string userId,
        Guid organizationId,
        string? plate,
        Guid? clientId,
        VehicleKind kind)
    {
        return await this.FindByPlateAsync(userId, organizationId, plate, clientId, kind, allowOtherFormat: false);
    }

    /// <summary> Находит единственный номер, сохраняя различия свободного формата. </summary>
    public async Task<QuickBookingVehicleDto?> FindByPlateAsync(
        string userId,
        Guid organizationId,
        string? plate,
        Guid? clientId,
        VehicleKind kind,
        bool allowOtherFormat)
    {
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.MaintenanceCreate);

        var normalizedPlate = this.NormalizePlateForSearch(plate);
        if (string.IsNullOrWhiteSpace(normalizedPlate))
        {
            return null;
        }

        var vehicles = await this._context.Vehicles
            .AsNoTracking()
            .Where(vehicle => vehicle.OrganizationId == organizationId
                && !vehicle.IsDeleted
                && vehicle.Kind == kind
                && !string.IsNullOrEmpty(vehicle.RegistrationPlate))
            .Where(vehicle => !clientId.HasValue || vehicle.OrganizationClientId == clientId.Value)
            .Select(vehicle => new QuickBookingVehicleDto
            {
                Id = vehicle.Id,
                OrganizationClientId = vehicle.OrganizationClientId,
                Kind = vehicle.Kind,
                RegistrationPlate = vehicle.RegistrationPlate
            })
            .ToListAsync();

        var freeFormat = allowOtherFormat || registrationPlates is not null &&
            (!registrationPlates.GetFormats().Formats.Any(format => format.Kinds.Contains((int)kind)) || !registrationPlates.MatchesFormat(plate, kind));
        var matches = vehicles.Where(vehicle => freeFormat
            ? string.Equals(vehicle.RegistrationPlate?.Trim(), plate?.Trim(), StringComparison.Ordinal)
            : this.NormalizePlateForSearch(vehicle.RegistrationPlate) == normalizedPlate).Take(2).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException("Найдено несколько машин с таким номером. Выберите нужную машину из списка.");
        }

        return matches.SingleOrDefault();
    }

    /// <summary> Возвращает выбранную технику по идентификатору. </summary>
    public async Task<QuickBookingVehicleDto?> GetSelectedAsync(string userId, Guid organizationId, Guid vehicleId)
    {
        await this._subscriptions.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.MaintenanceCreate);

        return await this._context.Vehicles
            .AsNoTracking()
            .Where(vehicle => vehicle.Id == vehicleId
                && vehicle.OrganizationId == organizationId
                && !vehicle.IsDeleted)
            .Select(vehicle => new QuickBookingVehicleDto
            {
                Id = vehicle.Id,
                OrganizationClientId = vehicle.OrganizationClientId,
                Kind = vehicle.Kind,
                RegistrationPlate = vehicle.RegistrationPlate
            })
            .SingleOrDefaultAsync();
    }

    /// <summary> Нормализует госномер для поиска. </summary>
    public string NormalizePlateForSearch(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(QuickBookingVehicleLookupService.MapPlateCharacter(character));
            }
        }

        return builder.ToString();
    }

    /// <summary> Преобразует символ госномера к поисковому виду. </summary>
    private static char MapPlateCharacter(char character) => character switch
    {
        'A' => 'А',
        'B' => 'В',
        'C' => 'С',
        'E' => 'Е',
        'H' => 'Н',
        'K' => 'К',
        'M' => 'М',
        'O' => 'О',
        'P' => 'Р',
        'T' => 'Т',
        'X' => 'Х',
        'Y' => 'У',
        _ => character
    };
}
