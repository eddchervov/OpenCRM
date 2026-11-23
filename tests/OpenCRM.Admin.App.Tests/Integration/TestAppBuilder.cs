using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using OpenCRM.Admin.App.Localization;

[assembly: AvaloniaTestApplication(typeof(OpenCRM.Admin.App.Tests.Integration.TestAppBuilder))]

namespace OpenCRM.Admin.App.Tests.Integration;

public static class TestAppBuilder
{
    private static readonly IDisposable s_preferencePathScope = TestAppBuilder.InitializeLocalization();

    private static IDisposable InitializeLocalization()
    {
        var localization = AdminLocalization.Instance;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-tests-{Guid.NewGuid():N}.txt");
        var preferencePathScope = localization.UsePreferencePath(preferencePath);
        localization.Language = "ru";

        return preferencePathScope;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        GC.KeepAlive(TestAppBuilder.s_preferencePathScope);

        return AppBuilder.Configure<App>()
            .WithInterFont()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
