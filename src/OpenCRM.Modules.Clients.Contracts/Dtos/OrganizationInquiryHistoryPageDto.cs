namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Страница истории обращения. </summary>
public sealed class OrganizationInquiryHistoryPageDto
{
    public IReadOnlyList<OrganizationInquiryEventDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
}
