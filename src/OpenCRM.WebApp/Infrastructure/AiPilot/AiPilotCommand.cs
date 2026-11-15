using System.Text.Json;

namespace OpenCRM.WebApp.Infrastructure.AiPilot;

public sealed class AiPilotCommand
{
    public string Action { get; init; } = "unsupported";
    public string? RegistrationPlate { get; init; }

    /// <summary> Номер, нестандартный формат которого сотрудник явно подтвердил в диалоге. </summary>
    public string? ConfirmedRegistrationPlate { get; init; }
    public string? Date { get; init; }
    public string? StartTime { get; init; }
    public string? EndTime { get; init; }
    public List<AiPilotServiceLine> Services { get; init; } = [];
    public decimal? TotalAmount { get; init; }
    public decimal? PaidAmount { get; init; }
    public string? PaymentMethod { get; init; }
    public string? Comment { get; init; }
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];
}

public sealed class AiPilotServiceLine
{
    public string Name { get; init; } = string.Empty;
    public decimal? Price { get; init; }

    /// <summary> Серверная отметка явного ответа сотрудника об отложенном уточнении цены. </summary>
    public bool IsPriceDeferred { get; init; }
}
