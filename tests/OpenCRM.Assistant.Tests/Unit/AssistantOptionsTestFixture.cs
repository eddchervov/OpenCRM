using Microsoft.Extensions.Configuration;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.Assistant.Tests.Unit;

internal static class AssistantOptionsTestFixture
{
    private static readonly Lazy<AssistantOptions> s_options = new(Load);

    public static AssistantOptions Options => s_options.Value;

    private static AssistantOptions Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assistant-appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

        return AssistantOptions.Load(configuration);
    }
}
