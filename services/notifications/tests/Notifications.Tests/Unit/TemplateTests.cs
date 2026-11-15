using System.Text.Json;
using Moq;
using Notifications.Application;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure;

namespace Notifications.Tests.Unit;

/// <summary>Проверяет подстановку, неизменяемость версий и валидацию до сохранения задания.</summary>
public sealed class TemplateTests
{
    /// <summary> Проверяет экранирование HTML без исполнения содержимого параметров. </summary>
    [Fact]
    public void RendererEscapesHtmlWithoutEvaluatingParameterContents()
    {
        var renderer = new TemplateRenderer();
        var body = renderer.Render(
            "<p>{{name}}</p><a href=\"{{url:link}}\">Открыть</a>",
            new Dictionary<string, string>
            {
                ["name"] = "<script>{{another}}</script>",
                ["link"] = "https://example.com/?a=1&b=2"
            }, htmlEncodeValues: true);

        Assert.Equal(
            "<p>&lt;script&gt;{{another}}&lt;/script&gt;</p><a href=\"https://example.com/?a=1&amp;b=2\">Открыть</a>",
            body);
    }

    /// <summary> Проверяет отклонение синтаксически некорректных шаблонов. </summary>
    [Theory]
    [InlineData("{{name")]
    [InlineData("name}}")]
    [InlineData("{{{name}}}")]
    [InlineData("{{name + count}}")]
    [InlineData("{{html:body}}")]
    public void InvalidSyntaxIsRejected(string template)
    {
        Assert.Throws<ArgumentException>(() => new TemplateRenderer().Validate(template));
    }

    /// <summary> Проверяет отклонение небезопасных ссылок в шаблонах. </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("//example.com")]
    [InlineData("https://user:contact@example.invalid")]
    [InlineData("https://example.com/\r\n")]
    public void UnsafeLinksAreRejected(string link)
    {
        Assert.Throws<ArgumentException>(() => new TemplateRenderer().Render(
            "{{url:link}}", new Dictionary<string, string> { ["link"] = link }, true));
    }

    /// <summary> Проверяет выбор первой версии для старых JSON-команд. </summary>
    [Fact]
    public void OldJsonCommandsSelectVersionOne()
    {
        var request = JsonSerializer.Deserialize<CreateNotificationRequest>(
            """{"IdempotencyKey":"key","Channel":"email","Recipient":"contact@example.invalid","TemplateKey":"welcome"}""");
        var template = JsonSerializer.Deserialize<UpsertTemplateRequest>(
            """{"Channel":"email","SubjectTemplate":"Welcome","BodyTemplate":"Hello"}""");

        Assert.Equal(1, request!.TemplateVersion);
        Assert.Equal(1, template!.Version);
    }

    /// <summary> Проверяет отклонение некорректных данных шаблона до сохранения уведомления. </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"name":null}""")]
    [InlineData("""{"name":{"first":"Иван"}}""")]
    [InlineData("""{"name":["Иван"]}""")]
    public async Task InvalidTemplateDataIsRejectedBeforeSavingNotification(string dataJson)
    {
        var templates = CreateTemplateRepository("<p>{{name}}</p>");
        var notifications = new Mock<INotificationRepository>();
        var publisher = new Mock<IIntegrationEventPublisher>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new NotificationApplicationService(
            notifications.Object, new NotificationContentResolver(templates.Object, new TemplateRenderer(), Mock.Of<ISystemTemplateRenderer>()),
            publisher.Object, unitOfWork.Object, Mock.Of<ISystemClock>());
        var request = new CreateNotificationRequest(
            "key", "email", "contact@example.invalid", "welcome",
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(dataJson),
            null, null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.EnqueueAsync("opencrm", request, CancellationToken.None));

        notifications.Verify(item => item.Add(It.IsAny<Notification>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        publisher.Verify(item => item.PublishAsync(
            It.IsAny<NotificationDispatchRequested>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary> Проверяет, что отсутствие версии не подставляет другую версию. </summary>
    [Fact]
    public async Task MissingVersionDoesNotFallBackToAnotherVersion()
    {
        var resolver = new NotificationContentResolver(
            Mock.Of<INotificationTemplateRepository>(), new TemplateRenderer(), Mock.Of<ISystemTemplateRenderer>());
        var notification = CreateNotification("{}");

        await Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(notification, CancellationToken.None, 2));
    }

    /// <summary> Проверяет отклонение управляющих символов в отрисованной теме. </summary>
    [Theory]
    [InlineData("Тема\r\nBcc: contact@example.invalid")]
    [InlineData("\0")]
    public async Task RenderedSubjectRejectsControlCharacters(string subject)
    {
        var templates = CreateTemplateRepository("Письмо", "{{name}}");
        var resolver = new NotificationContentResolver(templates.Object, new TemplateRenderer(), Mock.Of<ISystemTemplateRenderer>());
        var notification = CreateNotification(JsonSerializer.Serialize(new { name = subject }));

        await Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(notification, CancellationToken.None));
    }

    /// <summary> Проверяет отклонение чрезмерно длинной отрисованной темы. </summary>
    [Fact]
    public async Task RenderedSubjectRejectsExcessiveLength()
    {
        var templates = CreateTemplateRepository("Письмо", "{{name}}");
        var resolver = new NotificationContentResolver(templates.Object, new TemplateRenderer(), Mock.Of<ISystemTemplateRenderer>());
        var notification = CreateNotification(JsonSerializer.Serialize(new { name = new string('a', 1001) }));

        await Assert.ThrowsAsync<ArgumentException>(() => resolver.ResolveAsync(notification, CancellationToken.None));
    }

    /// <summary> Проверяет, что сохранённое содержимое не перечитывает шаблон. </summary>
    [Fact]
    public async Task CapturedContentDoesNotReadTemplateAgain()
    {
        var templates = new Mock<INotificationTemplateRepository>(MockBehavior.Strict);
        var resolver = new NotificationContentResolver(templates.Object, new TemplateRenderer(), Mock.Of<ISystemTemplateRenderer>());
        var notification = CreateNotification("{}");
        notification.CaptureContent("Тема", "<p>Сохранено</p>", true, 2);

        var content = await resolver.ResolveAsync(notification, CancellationToken.None);

        Assert.Equal("<p>Сохранено</p>", content.Body);
        Assert.Equal("Тема", content.Subject);
        Assert.True(content.IsHtml);
        Assert.Equal(2, notification.TemplateVersion);
        Assert.Throws<InvalidOperationException>(() => notification.CaptureContent(null, "new", false, 3));
    }

    /// <summary> Проверяет запрет перезаписи существующей версии шаблона. </summary>
    [Fact]
    public async Task SameVersionCannotBeOverwritten()
    {
        var templates = CreateTemplateRepository("<p>{{name}}</p>");
        var publisher = new Mock<IIntegrationEventPublisher>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new TemplateApplicationService(
            templates.Object, new TemplateRenderer(), publisher.Object, unitOfWork.Object, Mock.Of<ISystemClock>(), Mock.Of<ISystemTemplateRenderer>());

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertAsync(
            "opencrm", "welcome", new UpsertTemplateRequest("email", null, "changed", true), CancellationToken.None));

        templates.Verify(item => item.Add(It.IsAny<NotificationTemplate>()), Times.Never);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary> Проверяет подтверждение того же содержимого без изменения версии. </summary>
    [Fact]
    public async Task SameContentIsAcknowledgedWithoutChangingTheVersion()
    {
        var templates = CreateTemplateRepository("<p>{{name}}</p>");
        var publisher = new Mock<IIntegrationEventPublisher>();
        var service = new TemplateApplicationService(
            templates.Object, new TemplateRenderer(), publisher.Object, Mock.Of<IUnitOfWork>(), Mock.Of<ISystemClock>(), Mock.Of<ISystemTemplateRenderer>());

        await service.UpsertAsync(
            "opencrm", "welcome", new UpsertTemplateRequest("email", null, "<p>{{name}}</p>", true), CancellationToken.None);

        templates.Verify(item => item.Add(It.IsAny<NotificationTemplate>()), Times.Never);
        publisher.Verify(item => item.SendToApplicationAsync(
            "opencrm", It.Is<NotificationTemplateUpdated>(result => result.Version == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary> Проверяет отклонение HTML в шаблоне SMS. </summary>
    [Fact]
    public async Task HtmlSmsTemplateIsRejected()
    {
        var service = new TemplateApplicationService(
            Mock.Of<INotificationTemplateRepository>(), new TemplateRenderer(),
            Mock.Of<IIntegrationEventPublisher>(), Mock.Of<IUnitOfWork>(), Mock.Of<ISystemClock>(), Mock.Of<ISystemTemplateRenderer>());

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpsertAsync(
            "opencrm", "sms", new UpsertTemplateRequest("sms", null, "<p>Код</p>", true), CancellationToken.None));
    }

    /// <summary> Создаёт тестовое уведомление с переданными JSON-данными шаблона. </summary>
    private static Notification CreateNotification(string dataJson)
    {
        return Notification.Create(
            "opencrm", "key", NotificationChannel.Email, "contact@example.invalid",
            "welcome", dataJson, null, null, DateTimeOffset.UtcNow, null);
    }

    /// <summary> Создаёт заглушку репозитория шаблонов с переданным телом и темой. </summary>
    private static Mock<INotificationTemplateRepository> CreateTemplateRepository(
        string body, string? subject = null)
    {
        var templates = new Mock<INotificationTemplateRepository>();
        templates.Setup(item => item.GetAsync(
                "opencrm", "welcome", NotificationChannel.Email, It.IsAny<CancellationToken>(), 1))
            .ReturnsAsync(new NotificationTemplate(
                "opencrm", "welcome", NotificationChannel.Email, subject, body, true, DateTimeOffset.UtcNow));

        return templates;
    }
}
