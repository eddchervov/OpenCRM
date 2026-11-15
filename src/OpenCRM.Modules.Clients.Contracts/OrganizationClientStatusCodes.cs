namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Коды статусов клиентов организации. </summary>
public static class OrganizationClientStatusCodes
{
    /// <summary> Код статуса «Активный». </summary>
    public const string Active = "active";

    /// <summary> Код статуса «Лид». </summary>
    public const string Lead = "lead";

    /// <summary> Код статуса «Требует внимания». </summary>
    public const string Attention = "attention";

    /// <summary> Код статуса «Приостановлен». </summary>
    public const string Paused = "paused";

    /// <summary> Код статуса «Потерян». </summary>
    public const string Lost = "lost";

    /// <summary> Список всех кодов статусов. </summary>
    public static readonly IReadOnlyList<string> All =
    [
        Active,
        Lead,
        Attention,
        Paused,
        Lost
    ];

    /// <summary> Приводит код статуса к допустимому значению. </summary>
    public static string Normalize(string? statusCode)
    {
        var normalized = statusCode?.Trim().ToLowerInvariant();
        return All.Contains(normalized) ? normalized! : Active;
    }

    /// <summary> Возвращает название по коду статуса. </summary>
    public static string GetName(string? statusCode) => Normalize(statusCode) switch
    {
        Lead => "Лид",
        Attention => "Требует внимания",
        Paused => "Пауза",
        Lost => "Потерян",
        _ => "Активный"
    };
}
