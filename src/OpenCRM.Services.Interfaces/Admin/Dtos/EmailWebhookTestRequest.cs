namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос создания тестового webhook-события. </summary>
public sealed class EmailWebhookTestRequest
{
    /// <summary> Идентификатор письма, если указан. </summary>
    public int? EmailMessageId { get; init; }

    /// <summary> Статус для тестового события, если указан. </summary>
    public string? Status { get; init; }
}
