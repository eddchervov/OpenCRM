namespace OpenCRM.Admin.Api.Models;

/// <summary> Обращение в поддержку для административной панели. </summary>
public sealed class AdminDashboardSupportMessage
{
    /// <summary> Идентификатор сообщения. </summary>
    public int Id { get; init; }

    /// <summary> Тема обращения. </summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary> Код статуса обращения. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Момент создания обращения. </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
