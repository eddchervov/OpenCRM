namespace OpenCRM.Services.Interfaces.Media.Dto;

/// <summary> Ответ с URL медиафайла. </summary>
public class MediaUrlResponse
{
    /// <summary> URL медиафайла. </summary>
    public string Url { get; set; }

    /// <summary> Дата и время окончания действия URL. </summary>
    public DateTime ExpiresAt { get; set; }
}

