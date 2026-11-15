namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Предпросмотр списка получателей рассылки. </summary>
public sealed class EmailRecipientsPreviewResponse
{
    /// <summary> Общее количество получателей по критериям. </summary>
    public int TotalCount { get; init; }

    /// <summary> Элементы предпросмотра получателей. </summary>
    public IReadOnlyList<EmailRecipientPreviewItem> Items { get; init; } = [];
}
