using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Localization;

/// <summary> Переводит роль в списках участников и приглашений без изменения её кода. </summary>
public sealed class LocalizedOrganizationRoleExtension : MarkupExtension
{
    public string Path { get; set; } = "RoleCode";

    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Mode = BindingMode.OneWay,
        Converter = new OrganizationRoleConverter(),
        Bindings =
        {
            new Binding(this.Path),
            new Binding(nameof(AdminLocalization.Language)) { Source = AdminLocalization.Instance }
        }
    };

    private sealed class OrganizationRoleConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count > 0 && values[0] is string code ? AdminDisplayLabels.OrganizationRole(code, code) : string.Empty;
    }
}
