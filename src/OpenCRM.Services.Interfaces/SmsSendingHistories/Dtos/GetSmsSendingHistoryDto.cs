using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.SmsSendingHistories.Dtos;

/// <summary> DTO записи истории отправки SMS. </summary>
public class GetSmsSendingHistoryDto
{
    /// <summary> Идентификатор записи. </summary>
    public int Id { get; set; }

    /// <summary> IP-адрес пользователя, если известен. </summary>
    public string? UserIpAddress { get; set; }

    /// <summary> Идентификатор пользователя, если известен. </summary>
    public Guid? UserId { get; set; }

    /// <summary> Текст отправленного сообщения. </summary>
    public string Text { get; set; } = default!;

    /// <summary> Дата отправки SMS. </summary>
    public DateTime DateTime { get; set; } = default!;

    /// <summary> Ответ SMS-провайдера, если есть. </summary>
    public string? Response { get; set; }

    /// <summary> Код, отправленный в SMS. </summary>
    public string SMSCode { get; set; } = default!;

    /// <summary> Номер телефона получателя. </summary>
    public string Phone { get; set; } = default!;

    /// <summary> Тип отправки SMS. </summary>
    public SmsSendingHistoryType Type { get; set; }
}

