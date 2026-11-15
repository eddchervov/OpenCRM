namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на редактирование деталей чека НПД. </summary>
public sealed class AdminNpdReceiptDetailsRequest
{
    /// <summary> Ссылка на чек. </summary>
    public string? ReceiptUrl { get; init; }

    /// <summary> Комментарий. </summary>
    public string? Comment { get; init; }
}
