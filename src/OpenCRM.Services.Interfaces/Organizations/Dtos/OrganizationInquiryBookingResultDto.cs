namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Результат конвертации обращения в плановую запись. </summary>
public sealed class OrganizationInquiryBookingResultDto
{
    /// <summary> Идентификатор созданной записи. </summary>
    public Guid MaintenanceId { get; init; }

    /// <summary> URL страницы созданной записи. </summary>
    public string MaintenanceUrl { get; init; } = string.Empty;
}
