namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Параметры серверного списка обращений. </summary>
public sealed record OrganizationInquiryQueryDto
{
    public string? SearchQuery { get; init; }
    public string? SourceCode { get; init; }
    public string? ResponsibleUserId { get; init; }
    public string? StageCode { get; init; }
    public string? ActionDue { get; init; }
    public string? TimeZoneId { get; init; }
    public DateOnly? Date { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
