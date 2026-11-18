using System.Globalization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Views;

/// <summary> Элемент диаграммы на главной странице. </summary>
public sealed class DashboardChart : Control
{
    /// <summary> Регистрирует свойство со наборами данных диаграммы. </summary>
    public static readonly StyledProperty<IReadOnlyList<DashboardChartSeries>> SeriesProperty =
        AvaloniaProperty.Register<DashboardChart, IReadOnlyList<DashboardChartSeries>>(nameof(Series), []);

    /// <summary> Индекс выделенной точки диаграммы. </summary>
    private int? _selectedIndex;

    /// <summary> Инициализирует стили и метаданные элемента. </summary>
    static DashboardChart() => AffectsRender<DashboardChart>(DashboardChart.SeriesProperty);

    /// <summary> Создаёт элемент диаграммы. </summary>
    public DashboardChart()
    {
        this.Focusable = true;
        this.MinHeight = 250;
        this.ClipToBounds = true;
    }

    /// <summary> Наборы данных, отображаемые на диаграмме. </summary>
    public IReadOnlyList<DashboardChartSeries> Series
    {
        get => this.GetValue(DashboardChart.SeriesProperty);
        set => this.SetValue(DashboardChart.SeriesProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        AdminLocalization.Instance.PropertyChanged += this.OnLanguageChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        AdminLocalization.Instance.PropertyChanged -= this.OnLanguageChanged;
        base.OnDetachedFromVisualTree(args);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AdminLocalization.Language))
        {
            this.InvalidateVisual();
        }
    }

    /// <summary> Отрисовывает диаграмму и легенду. </summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var points = this.Series.FirstOrDefault()?.Points;
        if (points == null || points.Count == 0)
        {
            DashboardChart.DrawText(context, AdminLocalization.Instance["core.chart.noData"], new Point(20, 80), Brushes.Gray);

            return;
        }

        var legendBottom = this.DrawLegend(context);
        var plotTop = Math.Max(52, legendBottom + 26);
        var plot = new Rect(64, plotTop, Math.Max(1, this.Bounds.Width - 90), Math.Max(1, this.Bounds.Height - plotTop - 36));
        var maximum = Math.Max(1, this.Series.SelectMany(series => series.Points).Max(point => point.Value ?? 0));
        if (this.Series.All(series => series.Format == "count"))
        {
            maximum = Math.Ceiling(maximum / 4) * 4;
        }

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#303030")), 1);
        for (var index = 0; index <= 4; index++)
        {
            var y = plot.Top + index * plot.Height / 4;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DashboardChart.DrawText(context, (maximum * (4 - index) / 4).ToString("0.#", CultureInfo.CurrentCulture),
                new Point(2, y - 8), Brushes.Gray);
        }

        foreach (var series in this.Series)
        {
            var brush = new SolidColorBrush(Color.Parse(series.Color));
            var pen = new Pen(brush, 2);
            Point? previous = null;
            for (var index = 0; index < series.Points.Count; index++)
            {
                if (series.Points[index].Value is not { } value)
                {
                    previous = null;
                    continue;
                }

                var point = new Point(DashboardChart.X(plot, index, points.Count), plot.Bottom - value / maximum * plot.Height);
                if (previous is { } start)
                {
                    context.DrawLine(pen, start, point);
                }

                context.DrawEllipse(brush, null, point, 2.5, 2.5);
                previous = point;
            }
        }

        var labelStep = Math.Max(1, (int)Math.Ceiling(points.Count / Math.Max(2, plot.Width / 85)));
        for (var index = 0; index < points.Count; index++)
        {
            if (index % labelStep != 0 && index != points.Count - 1)
            {
                continue;
            }

            var label = DashboardChart.FormatDateLabel(points[index].Label);

            DashboardChart.DrawText(context, label, new Point(DashboardChart.X(plot, index, points.Count), plot.Bottom + 12),
                Brushes.Gray, true, this.Bounds.Width);
        }

        if (this._selectedIndex is { } selected && selected < points.Count)
        {
            var x = DashboardChart.X(plot, selected, points.Count);
            context.DrawLine(new Pen(Brushes.Gray, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        if (this._selectedIndex is { } dateIndex && dateIndex < points.Count)
        {
            DashboardChart.DrawText(context, DashboardChart.FormatDateLabel(points[dateIndex].Label),
                new Point(plot.Left, legendBottom + 4), Brushes.LightGray);
        }
    }

    /// <summary> Отрисовывает легенду и возвращает её высоту. </summary>
    private double DrawLegend(DrawingContext context)
    {
        var legendX = 64.0;
        var legendY = 6.0;
        foreach (var series in this.Series)
        {
            var label = series.Title;
            if (this._selectedIndex is { } legendIndex && legendIndex < series.Points.Count)
            {
                label += ": " + DashboardFormatting.Value(series.Points[legendIndex].Value, series.Format);
            }

            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                Typeface.Default, 11, new SolidColorBrush(Color.Parse(series.Color)));

            if (legendX > 64 && legendX + text.Width > this.Bounds.Width - 10)
            {
                legendX = 64;
                legendY += 18;
            }

            context.DrawText(text, new Point(legendX, legendY));
            legendX += text.Width + 20;
        }

        return legendY + 14;
    }

    /// <summary> Отслеживает наведение указателя для выделения точки. </summary>
    protected override void OnPointerMoved(PointerEventArgs eventArgs)
    {
        base.OnPointerMoved(eventArgs);
        var count = this.Series.FirstOrDefault()?.Points.Count ?? 0;
        if (count == 0)
        {
            return;
        }

        var ratio = Math.Clamp((eventArgs.GetPosition(this).X - 64) / Math.Max(1, this.Bounds.Width - 90), 0, 1);
        this._selectedIndex = (int)Math.Round(ratio * (count - 1));
        this.InvalidateVisual();
    }

    /// <summary> Обрабатывает нажатие указателя для выделения точки. </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs eventArgs)
    {
        base.OnPointerPressed(eventArgs);
        this.Focus();
        this.OnPointerMoved(eventArgs);
    }

    /// <summary> Снимает выделение точки при уходе указателя. </summary>
    protected override void OnPointerExited(PointerEventArgs eventArgs)
    {
        base.OnPointerExited(eventArgs);
        this._selectedIndex = null;
        this.InvalidateVisual();
    }

    /// <summary> Перемещает выделение точек клавишами. </summary>
    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        var count = this.Series.FirstOrDefault()?.Points.Count ?? 0;
        if (count == 0)
        {
            return;
        }

        this._selectedIndex = eventArgs.Key switch
        {
            Key.Home => 0,
            Key.End => count - 1,
            Key.Left => Math.Max(0, (this._selectedIndex ?? 1) - 1),
            Key.Right => Math.Min(count - 1, (this._selectedIndex ?? -1) + 1),
            _ => this._selectedIndex
        };

        eventArgs.Handled = eventArgs.Key is Key.Home or Key.End or Key.Left or Key.Right;
        this.InvalidateVisual();
    }

    /// <summary> Вычисляет координату точки по горизонтали. </summary>
    private static double X(Rect plot, int index, int count) => count <= 1
        ? plot.Center.X
        : plot.Left + index * plot.Width / (count - 1);

    private static string FormatDateLabel(string value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date)
        ? date.ToString("m", CultureInfo.CurrentCulture)
        : value;

    /// <summary> Рисует текст в указанной позиции. </summary>
    private static void DrawText(DrawingContext context, string value, Point origin, IBrush brush, bool center = false, double? right = null)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, brush);

        var x = Math.Clamp(center ? origin.X - text.Width / 2 : origin.X, 0, Math.Max(0, (right ?? double.MaxValue) - text.Width));
        context.DrawText(text, new Point(x, origin.Y));
    }
}
