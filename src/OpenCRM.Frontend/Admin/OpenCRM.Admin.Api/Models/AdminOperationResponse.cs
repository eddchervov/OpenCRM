namespace OpenCRM.Admin.Api.Models;

/// <summary> Ответ административного API на изменение. </summary>
public sealed class AdminOperationResponse
{
    /// <summary> Сообщение о результате операции. </summary>
    public string Message { get; init; } = string.Empty;
}
