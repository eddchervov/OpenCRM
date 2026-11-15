using Microsoft.Extensions.Configuration;
using OpenCRM.Modules.Notifications.Emails;

namespace OpenCRM.Modules.Notifications.Tests.Unit;

public sealed class DisabledEmailDeliveryTests
{
    [Theory]
    [InlineData("Disabled", false)]
    [InlineData("Disabled", true)]
    [InlineData("InMemory", false)]
    [InlineData("InMemory", true)]
    public async Task UnavailableDeliveryRejectsMessagesBeforeAccessingDatabase(string transport, bool useTemplate)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Transport"] = transport
        }).Build();
        var sender = new EmailSender(null!, configuration);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => useTemplate
            ? sender.QueueTemplateAsync("test@example.test", "Тест", "system/test", new { })
            : sender.QueueAsync("test@example.test", "Тест", "Текст", "test-key"));

        Assert.Contains("Notifications:Transport", exception.Message);
    }
}
