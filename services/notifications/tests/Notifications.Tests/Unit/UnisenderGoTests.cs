using System.Text.Json;
using Microsoft.Extensions.Options;
using MimeKit;
using Notifications.Infrastructure.Providers.UnisenderGo;

namespace Notifications.Tests.Unit;

/// <summary> Проверяет метаданные и настройки отслеживания отправки через Unisender Go. </summary>
public sealed class UnisenderGoTests
{
    /// <summary> Проверяет сохранение ключа уведомления и настроек отслеживания в метаданных. </summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void MetadataPreservesNotificationKeyAndTrackingSettings(bool enabled, int expected)
    {
        using var message = new MimeMessage();
        var enricher = new UnisenderGoMessageEnricher(Options.Create(new UnisenderGoOptions
        {
            TrackLinks = enabled,
            TrackRead = enabled
        }));
        enricher.Enrich(message, "campaign:notification", "catalog");
        var json = message.Headers["X-UNISENDER-GO"];
        Assert.NotNull(json);
        using var header = JsonDocument.Parse(json);
        Assert.Equal("catalog", header.RootElement.GetProperty("global_metadata").GetProperty("application_id").GetString());
        Assert.Equal(expected, header.RootElement.GetProperty("track_links").GetInt32());
        Assert.Equal(expected, header.RootElement.GetProperty("track_read").GetInt32());
        Assert.Equal("campaign:notification",
            header.RootElement.GetProperty("global_metadata").GetProperty("notification_key").GetString());
    }
}
