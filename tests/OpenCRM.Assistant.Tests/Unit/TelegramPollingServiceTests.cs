using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class TelegramPollingServiceTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", true)]
    [InlineData("AiPilot", false)]
    public async Task StartAsync_DoesNotOpenTransportOutsideEnabledPilot(string environmentName, bool isEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "assistant-appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AssistantTelegram:BotToken"] = "123456:test-token",
                ["AssistantTelegram:BotUsername"] = "test_pilot_bot",
                ["AssistantTelegram:WebhookSecret"] = "TestWebhookSecret_123",
                ["AssistantTelegram:LongPollingEnabled"] = isEnabled.ToString()
            }).Build();
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns(environmentName);
        using var service = new AssistantTelegramPollingService(scopes.Object, configuration, environment.Object,
            NullLogger<AssistantTelegramPollingService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        scopes.Verify(item => item.CreateScope(), Times.Never);
    }
}
