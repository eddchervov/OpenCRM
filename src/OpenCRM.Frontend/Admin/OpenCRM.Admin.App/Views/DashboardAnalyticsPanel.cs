using Avalonia;
using Avalonia.Controls;

namespace OpenCRM.Admin.App.Views;

/// <summary> Панель компоновки аналитики на главной странице. </summary>
public sealed class DashboardAnalyticsPanel : Panel
{
    /// <summary> Зазор между элементами панели. </summary>
    private const double Gap = 12;

    /// <summary> Ширина, при которой используется двухколоночная компоновка. </summary>
    private const double TwoColumnWidth = 1040;

    /// <summary> Измеряет элементы для определения размера панели. </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = this.Children.Where(child => child.IsVisible).ToArray();
        var columns = children.Length > 1 && availableSize.Width >= DashboardAnalyticsPanel.TwoColumnWidth && double.IsFinite(availableSize.Width) ? 2 : 1;
        var width = (availableSize.Width - (columns - 1) * DashboardAnalyticsPanel.Gap) / columns;
        var height = 0.0;
        var maximumWidth = 0.0;
        for (var row = 0; row < children.Length; row += columns)
        {
            var rowHeight = 0.0;
            for (var column = 0; column < columns && row + column < children.Length; column++)
            {
                var child = children[row + column];
                child.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                maximumWidth = Math.Max(maximumWidth, child.DesiredSize.Width);
            }

            height += rowHeight + (row == 0 ? 0 : DashboardAnalyticsPanel.Gap);
        }

        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : maximumWidth, height);
    }

    /// <summary> Размещает элементы по рассчитанной компоновке. </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = this.Children.Where(child => child.IsVisible).ToArray();
        var columns = children.Length > 1 && finalSize.Width >= DashboardAnalyticsPanel.TwoColumnWidth ? 2 : 1;
        var width = Math.Max(0, (finalSize.Width - (columns - 1) * DashboardAnalyticsPanel.Gap) / columns);
        var top = 0.0;
        for (var row = 0; row < children.Length; row += columns)
        {
            var rowHeight = 0.0;
            for (var column = 0; column < columns && row + column < children.Length; column++)
            {
                var child = children[row + column];
                child.Arrange(new Rect(column * (width + DashboardAnalyticsPanel.Gap), top, width, child.DesiredSize.Height));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            top += rowHeight + DashboardAnalyticsPanel.Gap;
        }

        return finalSize;
    }
}
