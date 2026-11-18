using System.Globalization;
using System.Resources;

namespace OpenCRM.Admin.Launcher;

/// <summary> Локализует собственные сообщения лаунчера по языку операционной системы. </summary>
internal static class LauncherText
{
    private static readonly ResourceManager Resources = new(
        "OpenCRM.Admin.Launcher.Resources.Strings",
        typeof(LauncherText).Assembly);

    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, LauncherText.Get(key), arguments);
}
