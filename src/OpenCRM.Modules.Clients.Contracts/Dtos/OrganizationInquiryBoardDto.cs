namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Ограниченная по этапам выдача доски обращений. </summary>
public sealed class OrganizationInquiryBoardDto
{
    public IReadOnlyList<OrganizationInquiryBoardColumnDto> Columns { get; init; } = [];
    public bool CanManage { get; init; }
    public bool CanViewClients { get; init; }
    public bool CanViewVehicles { get; init; }
    public string? TimeZoneId { get; init; }
}

/// <summary> Этап доски обращений и его порция карточек. </summary>
public sealed class OrganizationInquiryBoardColumnDto
{
    public string StageCode { get; init; } = string.Empty;
    public IReadOnlyList<OrganizationInquiryDto> Items { get; init; } = [];
    public int Count { get; init; }
    public int Page { get; init; }
    public bool HasMore { get; init; }
}
