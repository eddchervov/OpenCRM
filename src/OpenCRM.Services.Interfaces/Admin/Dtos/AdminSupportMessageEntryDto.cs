using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO записи переписки по сообщению поддержки. </summary>
public class AdminSupportMessageEntryDto
{
    /// <summary> Идентификатор записи. </summary>
    public int Id { get; set; }

    /// <summary> Признак записи от сотрудника. </summary>
    public bool IsStaff { get; set; }

    /// <summary> Вид записи. </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary> Текст записи. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Дата создания записи. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Статус доставки записи. </summary>
    public string? DeliveryStatus { get; set; }

    /// <summary> Ошибка доставки записи. </summary>
    public string? DeliveryError { get; set; }

    /// <summary> Вложения этого сообщения. </summary>
    public IReadOnlyList<SupportAttachmentDto> Attachments { get; set; } = [];
}
