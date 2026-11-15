namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Информация о лимите техники для пользователя. </summary>
public class VehicleLimitInfoDto
{
    /// <summary> Текущее количество техники. </summary>
    public int CurrentVehicleCount { get; set; }

    /// <summary> Признак возможности добавления техники. </summary>
    public bool CanAddMoreVehicles { get; set; }

    /// <summary> Признак наличия активной подписки. </summary>
    public bool HasActiveSubscription { get; set; }

    /// <summary> Максимальное количество техники для пользователя. </summary>
    public int MaxVehiclesForUser { get; set; }

    /// <summary> Признак максимального плана. </summary>
    public bool IsMaxPlan { get; set; }

    /// <summary> Название текущего плана. </summary>
    public string? CurrentPlanName { get; set; }
}
