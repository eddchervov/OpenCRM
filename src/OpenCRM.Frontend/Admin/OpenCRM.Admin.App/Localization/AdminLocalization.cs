using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.Localization;

/// <summary> Язык интерфейса настольной панели администратора. </summary>
public sealed class AdminLocalization : INotifyPropertyChanged
{
    private static readonly ResourceManager s_resources = new("OpenCRM.Admin.App.Localization.Strings", typeof(AdminLocalization).Assembly);
    private string _language;
    private string _preferencePath;

    public static AdminLocalization Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary> Поддерживаемые коды языка интерфейса. </summary>
    public IReadOnlyList<AdminUiLanguageOption> Languages => AdminUiLanguage.Supported;

    public string Language
    {
        get => this._language;
        set
        {
            var normalized = AdminUiLanguage.Normalize(value);
            if (this._language == normalized)
            {
                return;
            }

            this._language = normalized;
            this.SaveLanguage(normalized);
            this.OnPropertyChanged();
            this.OnPropertyChanged(nameof(LanguageName));
            this.OnPropertyChanged("Item[]");
        }
    }

    /// <summary> Название выбранного языка на самом языке. </summary>
    public string LanguageName => AdminUiLanguage.Supported.First(language => language.Code == this.Language).NativeName;

    /// <summary> Возвращает перевод ключа, а для неизвестного ключа сам ключ. </summary>
    public string this[string key] => AdminLocalization.s_resources.GetString(
        key,
        CultureInfo.GetCultureInfo(AdminUiLanguage.Supported.First(language => language.Code == this.Language).LanguageTag)) ?? key;

    /// <summary> Подставляет параметры с региональным форматом независимо от языка интерфейса. </summary>
    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, this[key], arguments);

    public AdminLocalization(string? preferencePath = null, string? defaultLanguage = null)
    {
        this._preferencePath = Path.GetFullPath(preferencePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCRM", "Admin", "language.txt"));
        this._language = this.LoadLanguage(defaultLanguage);
    }

    /// <summary> Временно направляет сохранение выбора языка в заданный файл. </summary>
    public IDisposable UsePreferencePath(string preferencePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferencePath);
        var previousPath = this._preferencePath;
        this._preferencePath = Path.GetFullPath(preferencePath);

        return new PreferencePathScope(() => this._preferencePath = previousPath);
    }

    private string LoadLanguage(string? defaultLanguage)
    {
        try
        {
            if (File.Exists(this._preferencePath))
            {
                var language = File.ReadAllText(this._preferencePath).Trim();
                if (AdminUiLanguage.Supported.Any(option => option.Code == language))
                {
                    return language;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Не удалось прочитать настройку языка: {exception.Message}");
        }

        var operatingSystemLanguage = defaultLanguage ?? CultureInfo.CurrentUICulture.Name;
        return AdminUiLanguage.Supported.FirstOrDefault(language => language.Aliases.Any(alias =>
            operatingSystemLanguage.StartsWith(alias, StringComparison.OrdinalIgnoreCase)))?.Code ?? "ru";
    }

    private void SaveLanguage(string language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this._preferencePath)!);
            File.WriteAllText(this._preferencePath, language);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Не удалось сохранить настройку языка: {exception.Message}");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class PreferencePathScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
