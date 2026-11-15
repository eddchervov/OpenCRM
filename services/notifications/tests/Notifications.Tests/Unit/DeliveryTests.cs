using Microsoft.EntityFrameworkCore;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Notifications.Application;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Providers.SmsRu;

namespace Notifications.Tests.Unit;

/// <summary>Проверяет границу между принятием провайдером, сохранением состояния и ошибкой доставки.</summary>
public sealed class DeliveryTests
{
    /// <summary>Ошибка БД после отправки не порождает вторую, неуспешную запись попытки.</summary>
    [Fact]
    public async Task PersistenceFailureAfterSendIsNotProviderFailure()
    {
        var notification = Notification.Create(
            "opencrm", "save-failure", NotificationChannel.Email, "contact@example.invalid",
            null, "{}", null, "body", DateTimeOffset.UtcNow, null);
        notification.CaptureContent(null, "body", false, null);
        notification.MarkProcessing();
        var repository = new Mock<INotificationRepository>();
        repository.Setup(item => item.ClaimAsync(notification.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        var sender = new Mock<INotificationChannelSender>();
        sender.SetupGet(item => item.Channel).Returns(NotificationChannel.Email);
        sender.Setup(item => item.SendAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync("provider-id");
        var publisher = new Mock<IIntegrationEventPublisher>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Database unavailable."));
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(item => item.UtcNow).Returns(DateTimeOffset.UtcNow);
        var dispatcher = new NotificationDispatcher(
            repository.Object,
            new NotificationContentResolver(Mock.Of<INotificationTemplateRepository>(), new TemplateRenderer(), Mock.Of<ISystemTemplateRenderer>()),
            [sender.Object], unitOfWork.Object, publisher.Object,
            clock.Object, NullLogger<NotificationDispatcher>.Instance);

        await Assert.ThrowsAsync<IOException>(() => dispatcher.DispatchAsync(notification.Id, CancellationToken.None));

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        repository.Verify(item => item.AddAttempt(It.Is<NotificationAttempt>(attempt => attempt.Succeeded)), Times.Once);
        repository.Verify(item => item.AddAttempt(It.Is<NotificationAttempt>(attempt => !attempt.Succeeded)), Times.Never);
        publisher.Verify(item => item.SendToApplicationAsync(
            "opencrm", It.Is<NotificationStatusChanged>(result => result.Status == "provider-accepted"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Старое задание не отправляется, если снимок не удалось записать в БД.</summary>
    [Fact]
    public async Task LegacySnapshotPersistenceFailurePreventsProviderCall()
    {
        var notification = Notification.Create(
            "opencrm", "snapshot-failure", NotificationChannel.Email, "contact@example.invalid",
            null, "{}", null, "body", DateTimeOffset.UtcNow, null);
        notification.MarkProcessing();
        var repository = new Mock<INotificationRepository>();
        repository.Setup(item => item.ClaimAsync(
                notification.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Database unavailable."));
        var sender = new Mock<INotificationChannelSender>(MockBehavior.Strict);
        var dispatcher = new NotificationDispatcher(
            repository.Object,
            new NotificationContentResolver(Mock.Of<INotificationTemplateRepository>(), new TemplateRenderer(), Mock.Of<ISystemTemplateRenderer>()),
            [sender.Object], unitOfWork.Object, Mock.Of<IIntegrationEventPublisher>(),
            Mock.Of<ISystemClock>(), NullLogger<NotificationDispatcher>.Instance);

        await Assert.ThrowsAsync<IOException>(() => dispatcher.DispatchAsync(notification.Id, CancellationToken.None));

        Assert.Equal(NotificationStatus.Processing, notification.Status);
        Assert.Null(notification.LastError);
        repository.Verify(item => item.AddAttempt(It.IsAny<NotificationAttempt>()), Times.Never);
        sender.VerifyNoOtherCalls();
    }

    /// <summary>HTTP 200 с ошибкой SMS.ru не считается успешной отправкой.</summary>
    [Theory]
    [InlineData("""{"status":"ERROR","status_code":201}""")]
    [InlineData("""{"status":"OK","status_code":100,"sms":{"79990000000":{"status":"ERROR","status_code":207}}}""")]
    [InlineData("""{"status":"OK","status_code":100,"sms":{"79990000000":{"status":"OK","status_code":100}}}""")]
    public async Task SmsProviderRejectionIsNotSuccess(string response)
    {
        using var client = new HttpClient(new StaticResponseHandler(response)) { BaseAddress = new Uri("https://sms.ru") };
        var sender = CreateSmsSender(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("+79990000000", null, "test", false, CancellationToken.None));
    }

    /// <summary>В результате сохраняется идентификатор SMS, а не весь ответ с балансом и номером телефона.</summary>
    [Fact]
    public async Task SmsProviderAcceptanceReturnsMessageId()
    {
        const string response = """
            {"status":"OK","status_code":100,"sms":{"79990000000":{"status":"OK","status_code":100,"sms_id":"id-123"}}}
            """;
        using var client = new HttpClient(new StaticResponseHandler(response)) { BaseAddress = new Uri("https://sms.ru") };
        var sender = CreateSmsSender(client);

        var result = await sender.SendAsync("+79990000000", null, "test", false, CancellationToken.None);

        Assert.Equal("id-123", result);
    }

    /// <summary>Историческая имитация остаётся simulated при повторном запросе состояния.</summary>
    [Fact]
    public void SnapshotPreservesSimulatedDelivery()
    {
        var notification = Notification.Create(
            "opencrm", "simulated", NotificationChannel.Email, "contact@example.invalid",
            null, "{}", null, "body", DateTimeOffset.UtcNow, null);
        notification.MarkProcessing();
        notification.MarkSent(DateTimeOffset.UtcNow);
        notification.Attempts.Add(new NotificationAttempt(
            notification.Id, 1, DateTimeOffset.UtcNow, true, "log:test", null));

        Assert.Equal("simulated", notification.ToStatusChanged(DateTimeOffset.UtcNow).Status);
    }

    /// <summary>Текущая модель соответствует последней миграции.</summary>
    [Fact]
    public void ExistingMigrationMatchesModel()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql("Host=localhost;Database=model-check")
            .Options;
        using var context = new NotificationDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    /// <summary> Создаёт отправитель SMS.RU с переданным HttpClient. </summary>
    private static SmsRuNotificationSender CreateSmsSender(HttpClient client)
    {
        return new SmsRuNotificationSender(
            client, Options.Create(new DeliveryOptions { LogOnly = false }),
            Options.Create(new SmsRuOptions { ApiKey = "unit-test" }),
            NullLogger<SmsRuNotificationSender>.Instance);
    }

    /// <summary> Тестовый обработчик HTTP, возвращающий фиксированный ответ. </summary>
    private sealed class StaticResponseHandler(string response) : HttpMessageHandler
    {
        /// <summary> Возвращает заготовленный HTTP-ответ на запрос. </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }
    }
}
