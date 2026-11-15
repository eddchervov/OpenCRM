using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Tests.Unit;

/// <summary>
/// Проверяет ключевые инварианты доменной модели и преобразование внешних каналов.
/// </summary>
public sealed class NotificationTests
{
    /// <summary> Проверяет запрет уведомления без шаблона и готового тела. </summary>
    [Fact]
    /// <summary>Проверяет запрет уведомления без шаблона и готового тела.</summary>
    public void CreateThrowsWhenTemplateAndBodyAreMissing()
    {
        static Notification action() => Notification.Create(
            "opencrm",
            "key-1",
            NotificationChannel.Email,
            "contact@example.invalid",
            templateKey: null,
            dataJson: "{}",
            subject: "Subject",
            body: null,
            DateTimeOffset.UtcNow,
            scheduledAt: null);

        Assert.Throws<ArgumentException>((Func<Notification>)action);
    }

    /// <summary> Проверяет переход успешно обработанного уведомления в конечное состояние. </summary>
    [Fact]
    /// <summary>Проверяет переход успешно обработанного уведомления в конечное состояние.</summary>
    public void MarkSentSetsTerminalStateAndTimestamp()
    {
        var notification = CreateNotification();
        var sentAt = DateTimeOffset.UtcNow;

        notification.MarkProcessing();
        notification.MarkSent(sentAt);

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(sentAt, notification.SentAt);
        Assert.Equal(1, notification.AttemptCount);
        Assert.Null(notification.LastError);
    }

    /// <summary> Проверяет контрактные строковые значения поддерживаемых каналов. </summary>
    [Theory]
    [InlineData("email", NotificationChannel.Email)]
    [InlineData("EMAIL", NotificationChannel.Email)]
    [InlineData("sms", NotificationChannel.Sms)]
    /// <summary>Проверяет контрактные строковые значения поддерживаемых каналов.</summary>
    public void ParseChannelReturnsExpectedChannel(
        string value,
        NotificationChannel expectedChannel)
    {
        var channel = NotificationMapping.ParseChannel(value);

        Assert.Equal(expectedChannel, channel);
    }

    /// <summary> Проверяет нормализацию идентификатора приложения и ключа идемпотентности. </summary>
    [Fact]
    /// <summary>Проверяет нормализацию идентификатора приложения и ключа идемпотентности.</summary>
    public void CreateNormalizesTenantAndIdempotencyKey()
    {
        var notification = Notification.Create(
            " opencrm ",
            " notification-1 ",
            NotificationChannel.Sms,
            " +7 000 000-00-01 ",
            templateKey: null,
            dataJson: "{}",
            subject: null,
            body: "Test",
            DateTimeOffset.UtcNow,
            scheduledAt: null);

        Assert.Equal("opencrm", notification.ApplicationId);
        Assert.Equal("notification-1", notification.IdempotencyKey);
        Assert.Equal("+7 000 000-00-01", notification.Recipient);
    }

    /// <summary> Создаёт тестовое уведомление с корректными данными. </summary>
    private static Notification CreateNotification()
    {
        return Notification.Create(
            "opencrm",
            "key-1",
            NotificationChannel.Email,
            "contact@example.invalid",
            templateKey: null,
            dataJson: "{}",
            subject: "Subject",
            body: "Body",
            DateTimeOffset.UtcNow,
            scheduledAt: null);
    }
}
