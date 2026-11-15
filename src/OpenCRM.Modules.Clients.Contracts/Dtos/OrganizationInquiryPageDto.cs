namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Страница списка обращений организации. </summary>
public sealed class OrganizationInquiryPageDto
{
    public IReadOnlyList<OrganizationInquiryDto> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public bool CanManage { get; init; }
    public bool CanViewClients { get; init; }
    public bool CanViewVehicles { get; init; }
    public string? TimeZoneId { get; init; }
}
