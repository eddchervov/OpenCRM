namespace Notifications.Domain;

/// <summary>
/// Корневая сущность уведомления, управляющая его данными и переходами состояния.
/// </summary>
public sealed class Notification
{
    /// <summary> Создаёт экземпляр для инфраструктуры. </summary>
    private Notification()
    {
    }

    /// <summary> Создаёт уведомление с заданными данными. </summary>
    private Notification(
        Guid id,
        string applicationId,
        string idempotencyKey,
        NotificationChannel channel,
        string recipient,
        string? templateKey,
        string dataJson,
        string? subject,
        string? body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledAt)
    {
        this.Id = id;
        this.ApplicationId = applicationId;
        this.IdempotencyKey = idempotencyKey;
        this.Channel = channel;
        this.Recipient = recipient;
        this.TemplateKey = templateKey;
        this.DataJson = dataJson;
        this.Subject = subject;
        this.Body = body;
        this.CreatedAt = createdAt;
        this.ScheduledAt = scheduledAt;
        this.Status = NotificationStatus.Queued;
    }

    /// <summary>Уникальный идентификатор уведомления.</summary>
    public Guid Id { get; private set; }

    /// <summary>Идентификатор приложения-владельца для изоляции данных клиентов.</summary>
    public string ApplicationId { get; private set; } = string.Empty;

    /// <summary>Ключ, предотвращающий повторное создание одного бизнес-события.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Выбранный канал доставки.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>Адрес или номер получателя.</summary>
    public string Recipient { get; private set; } = string.Empty;

    /// <summary>Ключ шаблона; отсутствует при передаче готового текста.</summary>
    public string? TemplateKey { get; private set; }

    /// <summary>Версия шаблона; null у старых записей и сообщений без шаблона.</summary>
    public int? TemplateVersion { get; private set; }

    /// <summary>Формат сохранённого тела; null означает, что снимок ещё не создан.</summary>
    public bool? IsHtml { get; private set; }

    /// <summary>Снимок значений переменных шаблона в формате JSON.</summary>
    public string DataJson { get; private set; } = "{}";

    /// <summary>Сохранённая тема письма после подстановки параметров.</summary>
    public string? Subject { get; private set; }

    /// <summary>Сохранённое тело сообщения после подстановки параметров.</summary>
    public string? Body { get; private set; }

    /// <summary>Текущее состояние жизненного цикла.</summary>
    public NotificationStatus Status { get; private set; }

    /// <summary>Количество атомарно начатых попыток доставки.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Момент создания записи.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Самый ранний разрешённый момент отправки.</summary>
    public DateTimeOffset? ScheduledAt { get; private set; }

    /// <summary>Момент публикации команды доставки в брокер.</summary>
    public DateTimeOffset? DispatchRequestedAt { get; private set; }

    /// <summary>Начало текущей обработки; используется как аренда для восстановления зависших задач.</summary>
    public DateTimeOffset? ProcessingStartedAt { get; private set; }

    /// <summary>Момент успешной передачи сообщения внешнему провайдеру.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Последняя зафиксированная ошибка доставки.</summary>
    public string? LastError { get; private set; }

    /// <summary>История завершённых попыток доставки.</summary>
    public ICollection<NotificationAttempt> Attempts { get; private set; } = [];

    /// <summary>Создаёт новое уведомление в состоянии ожидания и проверяет обязательные данные.</summary>
    public static Notification Create(
        string applicationId,
        string idempotencyKey,
        NotificationChannel channel,
        string recipient,
        string? templateKey,
        string dataJson,
        string? subject,
        string? body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        if (string.IsNullOrWhiteSpace(templateKey) && string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("TemplateKey or Body is required.");
        }

        return new Notification(
            Guid.NewGuid(),
            applicationId.Trim(),
            idempotencyKey.Trim(),
            channel,
            recipient.Trim(),
            templateKey?.Trim(),
            dataJson,
            subject,
            body,
            createdAt,
            scheduledAt);
    }

    /// <summary>Однократно сохраняет содержимое, общее для всех попыток доставки.</summary>
    public void CaptureContent(string? subject, string body, bool isHtml, int? templateVersion)
    {
        if (this.IsHtml.HasValue)
        {
            throw new InvalidOperationException("Notification content has already been captured.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        this.Subject = subject;
        this.Body = body;
        this.IsHtml = isHtml;
        this.TemplateVersion = templateVersion;
    }

    /// <summary>Фиксирует публикацию команды доставки, исключая повторную плановую публикацию.</summary>
    public void MarkDispatchRequested(DateTimeOffset requestedAt)
    {
        this.DispatchRequestedAt = requestedAt;
    }

    /// <summary>Переводит уведомление в обработку и увеличивает счётчик попыток.</summary>
    public void MarkProcessing()
    {
        if (this.Status is NotificationStatus.Sent or NotificationStatus.Failed)
        {
            return;
        }

        this.Status = NotificationStatus.Processing;
        this.AttemptCount++;
    }

    /// <summary>Завершает жизненный цикл успешной отправкой и освобождает аренду обработки.</summary>
    public void MarkSent(DateTimeOffset sentAt)
    {
        this.Status = NotificationStatus.Sent;
        this.ProcessingStartedAt = null;
        this.SentAt = sentAt;
        this.LastError = null;
    }

    /// <summary>Завершает жизненный цикл окончательной ошибкой.</summary>
    public void MarkFailed(string error)
    {
        this.Status = NotificationStatus.Failed;
        this.ProcessingStartedAt = null;
        this.LastError = error;
    }

    /// <summary>Возвращает уведомление в очередь для следующей попытки.</summary>
    public void MarkQueuedForRetry(string error)
    {
        this.Status = NotificationStatus.Queued;
        this.ProcessingStartedAt = null;
        this.LastError = error;
    }
}
