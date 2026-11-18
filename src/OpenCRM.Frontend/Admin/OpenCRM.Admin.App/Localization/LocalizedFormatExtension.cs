using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace OpenCRM.Admin.App.Localization;

/// <summary> Форматирует локализованный шаблон по значению привязки и обновляет его при смене языка. </summary>
public sealed class LocalizedFormatExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string FallbackKey { get; set; } = string.Empty;

    public bool UseLocalTime { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Mode = BindingMode.OneWay,
        Converter = new LocalizedFormatConverter(this.Key, this.FallbackKey, this.UseLocalTime),
        Bindings =
        {
            new Binding(this.Path) { Mode = BindingMode.OneWay },
            new Binding(nameof(AdminLocalization.Language))
            {
                Source = AdminLocalization.Instance,
                Mode = BindingMode.OneWay
            }
        }
    };

    private sealed class LocalizedFormatConverter(string key, string fallbackKey, bool useLocalTime) : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2 || values[0] is UnsetValueType || values[0] == null)
            {
                return string.IsNullOrEmpty(fallbackKey)
                    ? string.Empty
                    : AdminLocalization.Instance[fallbackKey];
            }

            if (string.IsNullOrEmpty(key))
            {
                return values[0]?.ToString() ?? string.Empty;
            }

            var value = useLocalTime
                ? values[0] switch
                {
                    DateTime dateTime => dateTime.ToLocalTime(),
                    DateTimeOffset dateTimeOffset => dateTimeOffset.ToLocalTime(),
                    _ => values[0]
                }
                : values[0];

            return AdminLocalization.Instance.Format(key, value);
        }
    }
}
