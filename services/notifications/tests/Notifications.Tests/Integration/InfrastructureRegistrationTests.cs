using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Notifications.Contracts;
using Notifications.Infrastructure;

namespace Notifications.Tests.Integration;

/// <summary> Проверяет регистрацию компонентов инфраструктуры в DI. </summary>
[Trait("Category", "Integration")]
public sealed class InfrastructureRegistrationTests
{
    /// <summary>Миграции требуют только базу; отсутствующие провайдеры не препятствуют обслуживанию.</summary>
    [Fact]
    public void MigrationsDoNotRequireBrokerOrProviders()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationsDb"] = "Host=localhost;Database=test;Username=test;Password=test"
        }).Build();
        var environment = Mock.Of<IHostEnvironment>(item => item.EnvironmentName == Environments.Production);

        StartupConfigurationValidator.Validate(configuration, environment, migrationsOnly: true);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationInfrastructure(configuration, migrationsOnly: true);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void ChatDeliveryRequiresAnExplicitApplicationWhenWorkerServesMultipleApplications()
    {
        var configuration = CreateChatConfiguration(
            ["opencrm", "opencrm-development"],
            conversationApplicationId: null);
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNotificationInfrastructure(configuration));

        Assert.Contains("Conversations:ApplicationId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatDeliveryUsesOnlyTheSelectedApplicationQueues()
    {
        var configuration = CreateChatConfiguration(
            ["opencrm", "opencrm-development"],
            conversationApplicationId: "opencrm");
        var services = new ServiceCollection();
        services.AddNotificationInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NotificationMessagingOptions>>().Value;

        Assert.Equal("opencrm", options.ConversationApplicationId);
        Assert.Equal(
            ("opencrm", ConversationDeliveryProvider.Max),
            options.GetConversationDeliveryRoute(new Uri("rabbitmq://localhost/notifications-opencrm-conversation-max")));
        Assert.Equal("opencrm", options.GetTelegramChatApplicationId(
            new Uri("rabbitmq://localhost/notifications-opencrm-telegram-chat")));
        Assert.Equal(
            ("opencrm", ConversationDeliveryProvider.Telegram),
            options.GetConversationDeliveryRoute(new Uri("rabbitmq://localhost/notifications-opencrm-conversation-telegram")));
        Assert.Throws<InvalidOperationException>(() => options.GetConversationDeliveryRoute(
            new Uri("rabbitmq://localhost/notifications-opencrm-development-conversation-max")));
        Assert.Throws<InvalidOperationException>(() => options.GetConversationDeliveryRoute(
            new Uri("rabbitmq://localhost/notifications-opencrm-development-conversation-telegram")));
        Assert.Throws<InvalidOperationException>(() => options.GetTelegramChatApplicationId(
            new Uri("rabbitmq://localhost/notifications-opencrm-development-telegram-chat")));
    }

    [Fact]
    public void ChatDeliveryCanInferApplicationWhenWorkerServesOneApplication()
    {
        var configuration = CreateChatConfiguration(["opencrm"], conversationApplicationId: null);
        var services = new ServiceCollection();

        services.AddNotificationInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<NotificationMessagingOptions>>().Value;
        Assert.Equal("opencrm", options.ConversationApplicationId);
    }

    private static IConfiguration CreateChatConfiguration(string[] applications, string? conversationApplicationId)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationsDb"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["RabbitMq:Host"] = "localhost",
            ["RabbitMq:Port"] = "5672",
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:Username"] = "test",
            ["RabbitMq:Password"] = "test",
            ["TelegramChat:Enabled"] = "true",
            ["TelegramChat:OpenCRMBaseUrl"] = "https://opencrm.example",
            ["TelegramChat:ServiceKey"] = new string('s', 40),
            ["TelegramChat:RelayBaseUrl"] = "https://relay.example",
            ["TelegramChat:RelayKey"] = new string('r', 40),
            ["TelegramChat:RequestTimeoutSeconds"] = "20",
            ["TelegramChat:PrefetchCount"] = "8",
            ["TelegramChat:ConcurrencyLimit"] = "4",
            ["TelegramChat:QueueMaxLength"] = "100",
            ["TelegramChat:QueueMaxBytes"] = "1048576",
            ["TelegramChat:InboxWorkerConcurrency"] = "4",
            ["TelegramChat:InboxPollIntervalSeconds"] = "1",
            ["TelegramChat:InboxLeaseSeconds"] = "300",
            ["Conversations:ApplicationId"] = conversationApplicationId,
            ["RabbitMq:ApplicationId"] = conversationApplicationId
        };
        for (var index = 0; index < applications.Length; index++)
        {
            values[$"RabbitMq:Applications:{index}"] = applications[index];
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
