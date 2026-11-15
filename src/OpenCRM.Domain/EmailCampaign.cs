using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Почтовая рассылка с параметрами отправки и статистикой. </summary>
public class EmailCampaign
{
    /// <summary> Идентификатор рассылки (GUID). </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Тема письма рассылки. </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary> HTML-тело письма рассылки. </summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary> Код статуса рассылки. </summary>
    public string StatusCode { get; set; } = EmailCampaignStatusCodes.Scheduled;

    /// <summary> Код режима выбора получателей рассылки. </summary>
    public string TargetMode { get; set; } = EmailCampaignTargetModeCodes.User;

    /// <summary> Идентификатор целевого пользователя рассылки. </summary>
    public string? TargetUserId { get; set; }

    /// <summary> Код типа целевой организации рассылки. </summary>
    public string? TargetOrganizationTypeCode { get; set; }

    /// <summary> Признак отправки только на подтверждённые адреса. </summary>
    public bool OnlyConfirmedEmail { get; set; } = true;

    /// <summary> Признак отправки только при согласии на рекламу. </summary>
    public bool OnlyAdvertisingConsent { get; set; }

    /// <summary> Интервал между отправками писем в секундах. </summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary> Планируемое время запуска рассылки. </summary>
    public DateTime StartAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время создания рассылки. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последнего изменения рассылки. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время фактического запуска рассылки. </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary> Дата и время завершения рассылки. </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary> Идентификатор пользователя, создавшего рассылку. </summary>
    public string? CreatedByUserId { get; set; }

    /// <summary> Пользователь, создавший рассылку. </summary>
    public ApplicationUser? CreatedByUser { get; set; }

    /// <summary> Общее количество получателей рассылки. </summary>
    public int TotalCount { get; set; }

    /// <summary> Количество имитированных отправок рассылки. </summary>
    public int SimulatedCount { get; set; }

    /// <summary> Количество отправленных писем рассылки. </summary>
    public int SentCount { get; set; }

    /// <summary> Количество неудачных отправок рассылки. </summary>
    public int FailedCount { get; set; }

    /// <summary> Количество пропущенных получателей рассылки. </summary>
    public int SkippedCount { get; set; }

    /// <summary> Последняя ошибка рассылки. </summary>
    public string? LastError { get; set; }

    /// <summary> Список получателей рассылки. </summary>
    public ICollection<EmailCampaignRecipient> Recipients { get; set; } = new HashSet<EmailCampaignRecipient>();
}

/// <summary> Получатель почтовой рассылки со статусом доставки. </summary>
public class EmailCampaignRecipient
{
    /// <summary> Идентификатор получателя (GUID). </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор рассылки. </summary>
    public Guid EmailCampaignId { get; set; }

    /// <summary> Рассылка, к которой относится получатель. </summary>
    public EmailCampaign EmailCampaign { get; set; } = null!;

    /// <summary> Идентификатор пользователя-получателя. </summary>
    public string? UserId { get; set; }

    /// <summary> Пользователь-получатель письма. </summary>
    public ApplicationUser? User { get; set; }

    /// <summary> Адрес электронной почты получателя. </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary> Отображаемое имя получателя. </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary> Код статуса отправки письма получателю. </summary>
    public string StatusCode { get; set; } = EmailCampaignRecipientStatusCodes.Pending;

    /// <summary> Количество попыток отправки письма. </summary>
    public int AttemptCount { get; set; }

    /// <summary> Дата и время создания записи получателя. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последней попытки отправки. </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary> Дата и время успешной отправки. </summary>
    public DateTime? SentAt { get; set; }

    /// <summary> Сообщение об ошибке отправки. </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary> Справочник кодов статусов рассылки. </summary>
public static class EmailCampaignStatusCodes
{
    /// <summary> Код статуса запланированной рассылки. </summary>
    public const string Scheduled = "scheduled";

    /// <summary> Код статуса выполняемой рассылки. </summary>
    public const string Running = "running";

    /// <summary> Код статуса завершённой рассылки. </summary>
    public const string Completed = "completed";

    /// <summary> Код статуса приостановленной рассылки. </summary>
    public const string Paused = "paused";

    /// <summary> Код статуса отменённой рассылки. </summary>
    public const string Canceled = "canceled";
}

/// <summary> Справочник кодов статусов получателя рассылки. </summary>
public static class EmailCampaignRecipientStatusCodes
{
    /// <summary> Код статуса ожидания отправки. </summary>
    public const string Pending = "pending";

    /// <summary> Код статуса письма в очереди. </summary>
    public const string Queued = "queued";

    /// <summary> Код статуса имитированной отправки. </summary>
    public const string Simulated = "simulated";

    /// <summary> Код статуса отправки в процессе. </summary>
    public const string Sending = "sending";

    /// <summary> Код статуса успешно отправленного письма. </summary>
    public const string Sent = "sent";

    /// <summary> Код статуса неудачной отправки. </summary>
    public const string Failed = "failed";

    /// <summary> Код статуса пропущенного получателя. </summary>
    public const string Skipped = "skipped";
}

/// <summary> Справочник кодов режимов выбора целевой аудитории рассылки. </summary>
public static class EmailCampaignTargetModeCodes
{
    /// <summary> Код режима отправки на указанный адрес. </summary>
    public const string Email = "email";

    /// <summary> Код режима отправки всем пользователям. </summary>
    public const string All = "all";

    /// <summary> Код режима отправки конкретному пользователю. </summary>
    public const string User = "user";

    /// <summary> Код режима отправки по типу организации. </summary>
    public const string OrganizationType = "organizationType";
}
