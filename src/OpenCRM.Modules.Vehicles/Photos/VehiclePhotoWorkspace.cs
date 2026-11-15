using OpenCRM.Domain;

namespace OpenCRM.Modules.Vehicles.Photos;

/// <summary> Правила доступа и путей для фотографий техники. </summary>
internal static class VehiclePhotoWorkspace
{
    /// <summary> Проверяет доступ пользователя к технике. </summary>
    internal static void EnsureAccess(string userId, Guid? organizationId, Vehicle vehicle)
    {
        if (organizationId.HasValue)
        {
            if (vehicle.OrganizationId != organizationId.Value)
            {
                throw new InvalidOperationException("У вас нет доступа к этой организации.");
            }

            return;
        }

        if (vehicle.UserId != userId || vehicle.OrganizationId.HasValue)
        {
            throw new InvalidOperationException("У вас нет доступа к этой технике.");
        }
    }

    /// <summary> Строит целевую папку хранения фотографий. </summary>
    internal static string BuildTargetFolder(string userId, Guid? organizationId)
    {
        return organizationId.HasValue
            ? $"img/organizations/{organizationId.Value:D}/vehicles"
            : $"img/users/{userId}/vehicles";
    }
}
