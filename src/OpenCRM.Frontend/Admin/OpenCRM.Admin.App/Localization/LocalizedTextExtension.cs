using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace OpenCRM.Admin.App.Localization;

/// <summary> Создает обновляемую при смене языка привязку к переводу. </summary>
public sealed class LocalizedTextExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding(nameof(AdminLocalization.Language))
    {
        Source = AdminLocalization.Instance,
        Mode = BindingMode.OneWay,
        Converter = new LocalizedTextConverter(this.Key)
    };

    private sealed class LocalizedTextConverter(string key) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            AdminLocalization.Instance[key];

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
