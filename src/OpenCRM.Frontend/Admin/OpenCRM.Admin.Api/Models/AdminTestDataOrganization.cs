namespace OpenCRM.Admin.Api.Models;

public sealed class AdminTestDataOrganization
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string OwnerUserId { get; init; } = string.Empty;
    public string? OwnerEmail { get; init; }
    public string ScenarioCode { get; init; } = string.Empty;
    public string ScenarioLabel => this.ScenarioCode switch
    {
        "detailing" => "Детейлинг",
        "service-station" => "СТО",
        "fleet" => "Автопарк",
        _ => "Не указан"
    };

    public bool IsActive { get; init; }
    public int VehiclesCount { get; init; }
    public int MaintenanceCount { get; init; }
}
