namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Результат создания обращения в поддержку. </summary>
public class SupportMessageCreateResultDto
{
    /// <summary> Признак успешного создания обращения. </summary>
    public bool Succeeded { get; set; }

    /// <summary> Идентификатор созданного сообщения. </summary>
    public int? MessageId { get; set; }

    /// <summary> Сообщение об ошибке. </summary>
    public string? ErrorMessage { get; set; }

    /// <summary> Сообщение об успешном создании. </summary>
    public string? SuccessMessage { get; set; }
}
