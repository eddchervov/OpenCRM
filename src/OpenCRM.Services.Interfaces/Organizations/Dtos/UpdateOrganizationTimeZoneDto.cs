namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Запрос на смену часового пояса организации с проверкой ожидаемого значения. </summary>
public sealed class UpdateOrganizationTimeZoneDto
{
    /// <summary> Новый идентификатор часового пояса IANA. </summary>
    public string TimeZoneId { get; init; } = string.Empty;

    /// <summary> Часовой пояс, который клиент видел перед редактированием. </summary>
    public string? ExpectedTimeZoneId { get; init; }
}
