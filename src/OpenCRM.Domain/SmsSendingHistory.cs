using OpenCRM.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Domain;

/// <summary> История отправки SMS. </summary>
public class SmsSendingHistory
{
    /// <summary> Идентификатор записи истории SMS. </summary>
    public int Id { get; set; }

    /// <summary> Код SMS. </summary>
    public string SMSCode { get; set; } = default!;

    /// <summary> Текст SMS-сообщения. </summary>
    public string Text { get; set; } = default!;

    /// <summary> Номер телефона получателя. </summary>
    public string Phone { get; set; } = default!;

    /// <summary> Идентификатор пользователя. </summary>
    public Guid? UserId { get; set; }

    /// <summary> Дата и время отправки SMS. </summary>
    [Display(Name = "Дата/Время кода SMS")]
    public DateTime DateTime { get; set; } = default!;

    /// <summary> Ответ сервиса отправки SMS. </summary>
    public string? Response { get; set; }

    /// <summary> IP-адрес пользователя при отправке. </summary>
    public string? UserIpAddress { get; set; }

    /// <summary> Тип записи истории SMS. </summary>
    public SmsSendingHistoryType Type { get; set; }

}

