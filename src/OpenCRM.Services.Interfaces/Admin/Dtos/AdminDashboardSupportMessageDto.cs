namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO сообщения поддержки для дашборда. </summary>
public sealed class AdminDashboardSupportMessageDto
{
    /// <summary> Идентификатор сообщения. </summary>
    public int Id { get; init; }

    /// <summary> Тема сообщения. </summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary> Код статуса сообщения. </summary>
    public string StatusCode { get; init; } = string.Empty;

    /// <summary> Время создания сообщения. </summary>
    public DateTime CreatedAt { get; init; }
}
