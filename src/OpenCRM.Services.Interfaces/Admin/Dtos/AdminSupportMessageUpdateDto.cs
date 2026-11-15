namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO обновления сообщения поддержки. </summary>
public sealed class AdminSupportMessageUpdateDto
{
    /// <summary> Код статуса сообщения. </summary>
    public string? StatusCode { get; init; }

    /// <summary> Комментарий администратора. </summary>
    public string? AdminComment { get; init; }
}
