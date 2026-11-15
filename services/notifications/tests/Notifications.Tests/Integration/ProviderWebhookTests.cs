using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Contracts;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Providers.UnisenderGo;

namespace Notifications.Tests.Integration;

/// <summary>Проверяет подпись провайдера, атомарный outbox и защиту от повторных вебхуков.</summary>
[Trait("Category", "Integration")]
public sealed class ProviderWebhookTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    /// <summary> Фиксированный ключ шаблона, используемый в тестах вебхуков. </summary>
    private const string TestKey = "isolated-webhook-test-key";

    /// <summary> Проверяет, что повторный вебхук не создаёт второе адресованное событие. </summary>
    [Fact]
    public async Task DuplicateWebhookProducesOneAddressedEvent()
    {
        var key = Guid.NewGuid().ToString("N");
        var body = CreateBody("catalog", key);
        var responses = await Task.WhenAll(this.HandleAsync(body), this.HandleAsync(body));
        Assert.All(responses, response => Assert.Equal(200, ((IStatusCodeHttpResult)response).StatusCode));
        var result = await fixture.WaitAsync<NotificationDeliveryReported>("catalog", item => item.IdempotencyKey == key);
        Assert.Equal("clicked", result.Status);
        Assert.Equal("https://example.com", result.Url);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.Equal(1, await db.ProviderWebhookEvents.CountAsync(item => item.Id == result.EventId));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    /// <summary> Проверяет отклонение неверной подписи и чужого приложения. </summary>
    [Fact]
    public async Task InvalidSignatureAndForeignApplicationAreRejected()
    {
        var valid = CreateBody("catalog", Guid.NewGuid().ToString("N"));
        var tampered = valid.Replace("clicked", "opened", StringComparison.Ordinal);
        Assert.Equal(401, ((IStatusCodeHttpResult)await this.HandleAsync(tampered)).StatusCode);
        var foreign = CreateBody("unknown", Guid.NewGuid().ToString("N"));
        Assert.Equal(400, ((IStatusCodeHttpResult)await this.HandleAsync(foreign)).StatusCode);
    }

    /// <summary> Проверяет сохранение идентичности и корреляции события. </summary>
    [Fact]
    public void EventIdentityAndCorrelationArePreserved()
    {
        var body = CreateBody("opencrm", "notification-123");
        var root = JsonNode.Parse(body)!;
        Assert.True(UnisenderGoWebhookParser.VerifySignature(root, body, TestKey));
        var first = Assert.Single(UnisenderGoWebhookParser.Parse(root));
        var repeated = Assert.Single(UnisenderGoWebhookParser.Parse(root));
        Assert.Equal("opencrm", first.ApplicationId);
        Assert.Equal("notification-123", first.IdempotencyKey);
        Assert.Equal(first.EventId, repeated.EventId);
        Assert.Equal(TimeSpan.Zero, first.OccurredAt.Offset);
    }

    /// <summary> Проверяет, что пользовательский Razor даёт полное HTML и текст без исполнения входных данных. </summary>
    [Fact]
    public async Task CustomRazorReturnsFullHtmlAndTextWithoutEvaluatingInput()
    {
        var key = Guid.NewGuid().ToString("N");
        var request = new CreateNotificationRequest(key, "email", "custom-email@example.invalid", "system/custom-email",
            new Dictionary<string, JsonElement>
            {
                ["subject"] = JsonSerializer.SerializeToElement("Тема"),
                ["htmlBody"] = JsonSerializer.SerializeToElement("<p>{{текст}} @(1+1) &amp; данные</p>")
            }, null, null, null);
        await fixture.SendAsync("opencrm", new SendNotification(request));
        var result = await fixture.WaitAsync<NotificationStatusChanged>("opencrm",
            item => item.IdempotencyKey == key && item.Status == "simulated");
        Assert.NotNull(result.Content);
        Assert.Contains("<!doctype html>", result.Content.Body);
        Assert.Contains("<p>{{текст}} @(1+1) &amp; данные</p>", result.Content.Body);
        Assert.Contains("{{текст}} @(1+1) & данные", result.Content.TextBody);
        Assert.DoesNotContain("<style", result.Content.TextBody);
    }

    /// <summary> Проверяет игнорирование событий без обязательных метаданных. </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("opencrm", null)]
    [InlineData(null, "notification-123")]
    public async Task EventsWithoutRequiredMetadataAreIgnored(string? applicationId, string? key)
    {
        var body = CreateBody(applicationId, key);
        Assert.Empty(UnisenderGoWebhookParser.Parse(JsonNode.Parse(body)!));
        Assert.Equal(200, ((IStatusCodeHttpResult)await this.HandleAsync(body)).StatusCode);
    }

    /// <summary> Обрабатывает тело вебхука в тестовом обработчике. </summary>
    private async Task<IResult> HandleAsync(string body)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = new UnisenderGoWebhookHandler(
            scope.ServiceProvider.GetRequiredService<NotificationDbContext>(),
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>(),
            Options.Create(new UnisenderGoOptions { WebhookKey = TestKey }),
            Options.Create(new NotificationMessagingOptions { Applications = ["opencrm", "catalog"] }));
        var http = new DefaultHttpContext();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Request.Body = stream;

        return await handler.HandleAsync(http.Request, CancellationToken.None);
    }

    /// <summary> Формирует тело вебхука с подписью по протоколу Unisender Go. </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351",
        Justification = "Тест формирует подпись по протоколу Unisender Go.")]
    private static string CreateBody(string? applicationId, string? key)
    {
        var body = JsonSerializer.Serialize(new
        {
            auth = TestKey,
            events_by_user = new[]
            {
                new
                {
                    events = new[]
                    {
                        new
                        {
                            event_name = "transactional_email_status",
                            event_data = new
                            {
                                status = "clicked", event_time = "2026-09-09 10:00:00",
                                email = "contact@example.invalid", job_id = "job-123", url = "https://example.com",
                                metadata = new { application_id = applicationId, notification_key = key }
                            }
                        }
                    }
                }
            }
        });
        var signature = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        return body.Replace(TestKey, signature, StringComparison.Ordinal);
    }
}
