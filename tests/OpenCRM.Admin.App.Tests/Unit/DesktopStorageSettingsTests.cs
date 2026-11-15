using System.Text.Json;
using OpenCRM.Admin.App;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class DesktopStorageSettingsTests
{
    [Fact]
    public void LoadReadsConfiguredAbsoluteDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var brandFilePath = Path.Combine(root, "brand.json");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(brandFilePath, JsonSerializer.Serialize(new
            {
                AdminDesktop = new
                {
                    PendingActionsDirectory = Path.Combine(root, "pending"),
                    SessionDirectory = Path.Combine(root, "sessions"),
                    DashboardPreferencesFile = Path.Combine(root, "dashboard-refresh.txt")
                }
            }));

            var settings = DesktopStorageSettings.Load(brandFilePath);

            Assert.Equal(Path.Combine(root, "pending"), settings.PendingActionsDirectory);
            Assert.Equal(Path.Combine(root, "sessions"), settings.SessionDirectory);
            Assert.Equal(Path.Combine(root, "dashboard-refresh.txt"), settings.DashboardPreferencesFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsRelativeDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var brandFilePath = Path.Combine(root, "brand.json");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(brandFilePath, """
                {
                  "AdminDesktop": {
                    "PendingActionsDirectory": "data/pending"
                  }
                }
                """);

            Assert.Throws<InvalidOperationException>(() => DesktopStorageSettings.Load(brandFilePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadUsesDefaultsWhenFileOrSectionIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var brandFilePath = Path.Combine(root, "brand.json");
        Directory.CreateDirectory(root);

        try
        {
            var missingFileSettings = DesktopStorageSettings.Load(brandFilePath);
            File.WriteAllText(brandFilePath, "{ \"Brand\": {} }");
            var missingSectionSettings = DesktopStorageSettings.Load(brandFilePath);

            Assert.Null(missingFileSettings.PendingActionsDirectory);
            Assert.Null(missingFileSettings.SessionDirectory);
            Assert.Null(missingFileSettings.DashboardPreferencesFile);
            Assert.Null(missingSectionSettings.PendingActionsDirectory);
            Assert.Null(missingSectionSettings.SessionDirectory);
            Assert.Null(missingSectionSettings.DashboardPreferencesFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
