namespace OpenCRM.Domain;

/// <summary> Последняя просмотренная дата раздела административной панели. </summary>
public sealed class AdminNavigationReadState
{
    public string UserId { get; set; } = string.Empty;

    public string SectionCode { get; set; } = string.Empty;

    public DateTime ViewedThroughUtc { get; set; }
}
