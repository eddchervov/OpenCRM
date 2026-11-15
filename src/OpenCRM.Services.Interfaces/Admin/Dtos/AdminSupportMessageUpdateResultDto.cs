namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Результат обновления сообщения поддержки. </summary>
public class AdminSupportMessageUpdateResultDto
{
    /// <summary> Признак успешного обновления. </summary>
    public bool Succeeded { get; set; }

    /// <summary> Признак того, что сообщение не найдено. </summary>
    public bool NotFound { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }

    /// <summary> Сообщение об успехе. </summary>
    public string? SuccessMessage { get; set; }
}
