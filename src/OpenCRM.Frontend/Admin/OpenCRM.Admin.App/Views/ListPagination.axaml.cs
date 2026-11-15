using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace OpenCRM.Admin.App.Views;

/// <summary> Общая навигация по страницам списков административной панели. </summary>
public partial class ListPagination : UserControl
{
    public static readonly StyledProperty<string> PageLabelProperty =
        AvaloniaProperty.Register<ListPagination, string>(nameof(PageLabel), string.Empty);

    public static readonly StyledProperty<ICommand?> PreviousCommandProperty =
        AvaloniaProperty.Register<ListPagination, ICommand?>(nameof(PreviousCommand));

    public static readonly StyledProperty<ICommand?> NextCommandProperty =
        AvaloniaProperty.Register<ListPagination, ICommand?>(nameof(NextCommand));

    public ListPagination() => this.InitializeComponent();

    public string PageLabel
    {
        get => this.GetValue(ListPagination.PageLabelProperty);
        set => this.SetValue(ListPagination.PageLabelProperty, value);
    }

    public ICommand? PreviousCommand
    {
        get => this.GetValue(ListPagination.PreviousCommandProperty);
        set => this.SetValue(ListPagination.PreviousCommandProperty, value);
    }

    public ICommand? NextCommand
    {
        get => this.GetValue(ListPagination.NextCommandProperty);
        set => this.SetValue(ListPagination.NextCommandProperty, value);
    }
}
