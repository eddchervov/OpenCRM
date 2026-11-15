namespace OpenCRM.Services.Interfaces.Users.Dtos;

/// <summary> DTO личной статистики пользователя. </summary>
public class UserPersonalStatsDto
{
    /// <summary> Количество транспортных средств пользователя. </summary>
    public int VehiclesCount { get; set; }

    /// <summary> Количество обслуживаний пользователя. </summary>
    public int MaintenanceCount { get; set; }
}
