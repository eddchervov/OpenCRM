namespace OpenCRM.Modules.Notifications;

/// <summary> Настройки подключения к RabbitMQ для уведомлений. </summary>
public sealed class RabbitMqOptions
{
    /// <summary> Хост RabbitMQ. </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary> Порт RabbitMQ. </summary>
    public ushort Port { get; set; }

    /// <summary> Виртуальный хост RabbitMQ. </summary>
    public string VirtualHost { get; set; } = string.Empty;

    /// <summary> Имя пользователя RabbitMQ. </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary> Пароль RabbitMQ. </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary> Идентификатор приложения в RabbitMQ. </summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary> Интервал диспетчеризации исходящих сообщений в секундах. </summary>
    public int DispatchIntervalSeconds { get; set; }

    /// <summary> Таймаут ожидания ответа в секундах. </summary>
    public int ResponseTimeoutSeconds { get; set; }

    /// <summary> Таймаут отправки в секундах. </summary>
    public int SendTimeoutSeconds { get; set; }
}
