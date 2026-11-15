using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class TelegramSecurityTests
{
    private const string Secret = "a sufficiently long webhook secret for tests";

    [Fact]
    public void MatchesWebhookSecret_RequiresExactConfiguredSecret()
    {
        Assert.True(AssistantTelegramSecurity.MatchesWebhookSecret(Secret, Secret));
        Assert.False(AssistantTelegramSecurity.MatchesWebhookSecret(Secret, Secret + "x"));
        Assert.False(AssistantTelegramSecurity.MatchesWebhookSecret(Secret, null));
        Assert.False(AssistantTelegramSecurity.MatchesWebhookSecret(null, Secret));
    }

    [Fact]
    public void Callback_IsBoundToTelegramUserAndExpires()
    {
        var createdAt = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var callback = AssistantTelegramSecurity.CreateCallback(Secret, 1234, "c:abc", createdAt, 10);

        Assert.True(AssistantTelegramSecurity.TryValidateCallback(Secret, 1234, callback, createdAt.AddMinutes(9), 10, out var command));
        Assert.Equal("c:abc", command);
        Assert.False(AssistantTelegramSecurity.TryValidateCallback(Secret, 5678, callback, createdAt, 10, out _));
        Assert.False(AssistantTelegramSecurity.TryValidateCallback(Secret, 1234, callback, createdAt.AddMinutes(11), 10, out _));
    }

    [Fact]
    public void ChoiceHash_ChangesWhenClarificationChanges()
    {
        var first = new AssistantClarification("vehicle", "Выберите автомобиль", [new AssistantChoice("A 123 AA", "1")]);
        var changed = first with { Choices = [new AssistantChoice("B 456 BB", "2")] };

        Assert.NotEqual(AssistantTelegramSecurity.HashChoice(first), AssistantTelegramSecurity.HashChoice(changed));
    }

    [Fact]
    public void Callback_StaysWithinTelegramSizeLimit()
    {
        var command = $"a:{Guid.NewGuid():N}:999:{new string('a', 4)}";
        var callback = AssistantTelegramSecurity.CreateCallback(Secret, 1234, command, DateTime.UtcNow, 10);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(callback) <= 64);
    }
}
