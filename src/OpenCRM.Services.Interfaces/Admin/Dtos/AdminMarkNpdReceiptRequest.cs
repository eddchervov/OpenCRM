namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на отметку чека НПД. </summary>
public sealed class AdminMarkNpdReceiptRequest
{
    /// <summary> Признак выданного чека. </summary>
    public bool Issued { get; init; }

    /// <summary> Ожидаемый признак выдачи чека. </summary>
    public bool ExpectedIssued { get; init; }

    /// <summary> Причина изменения. </summary>
    public string? Reason { get; init; }
}
