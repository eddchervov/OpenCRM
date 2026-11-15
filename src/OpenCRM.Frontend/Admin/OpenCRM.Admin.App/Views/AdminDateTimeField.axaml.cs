using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace OpenCRM.Admin.App.Views;

/// <summary> Поле выбора даты и времени с единым календарём и 24-часовым вводом. </summary>
public sealed partial class AdminDateTimeField : UserControl
{
    public static readonly StyledProperty<DateTime?> ValueProperty =
        AvaloniaProperty.Register<AdminDateTimeField, DateTime?>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<AdminDateTimeField, string>(nameof(Placeholder), "Не задано");

    public static readonly StyledProperty<bool> IsPickerOpenProperty =
        AvaloniaProperty.Register<AdminDateTimeField, bool>(nameof(IsPickerOpen));

    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("ru-RU");
    private bool _isUpdatingControls;
    private DateTime? _draftDate;
    private TimeSpan? _draftTime;

    public AdminDateTimeField()
    {
        this.InitializeComponent();
        this.CalendarControl.PropertyChanged += this.OnCalendarPropertyChanged;
        this.TimeControl.PropertyChanged += this.OnTimePropertyChanged;
        this.UpdateDisplay();
    }

    public DateTime? Value
    {
        get => this.GetValue(AdminDateTimeField.ValueProperty);
        set => this.SetValue(AdminDateTimeField.ValueProperty, value);
    }

    public string Placeholder
    {
        get => this.GetValue(AdminDateTimeField.PlaceholderProperty);
        set => this.SetValue(AdminDateTimeField.PlaceholderProperty, value);
    }

    public bool IsPickerOpen
    {
        get => this.GetValue(AdminDateTimeField.IsPickerOpenProperty);
        set => this.SetValue(AdminDateTimeField.IsPickerOpenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsEnabledProperty && !this.IsEnabled)
        {
            this.IsPickerOpen = false;
        }

        if (change.Property == AdminDateTimeField.ValueProperty)
        {
            this.UpdateDisplay();
            this.LoadDraftFromValue();
        }

        if (change.Property == AdminDateTimeField.PlaceholderProperty)
        {
            this.UpdateDisplay();
        }
    }

    private void OnOpenClick(object? sender, RoutedEventArgs args)
    {
        this.LoadDraftFromValue();
        this.IsPickerOpen = true;
    }

    private void OnClearClick(object? sender, RoutedEventArgs args)
    {
        this.SetCurrentValue(AdminDateTimeField.ValueProperty, null);
        this.SetCurrentValue(AdminDateTimeField.IsPickerOpenProperty, false);
    }

    private void OnTodayClick(object? sender, RoutedEventArgs args)
    {
        this._draftDate = DateTime.Today;
        this._draftTime ??= TimeSpan.Zero;
        this.UpdatePickerControls();
    }

    private void OnClearDraftClick(object? sender, RoutedEventArgs args)
    {
        this._draftDate = null;
        this._draftTime = null;
        this.UpdatePickerControls();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs args)
    {
        this.IsPickerOpen = false;
    }

    private void OnDoneClick(object? sender, RoutedEventArgs args)
    {
        DateTime? value = this._draftDate is null
            ? null
            : DateTime.SpecifyKind(this._draftDate.Value.Date.Add(this._draftTime ?? TimeSpan.Zero), DateTimeKind.Unspecified);
        this.SetCurrentValue(AdminDateTimeField.ValueProperty, value);
        this.SetCurrentValue(AdminDateTimeField.IsPickerOpenProperty, false);
    }

    private void OnCalendarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (this._isUpdatingControls || args.Property != Avalonia.Controls.Calendar.SelectedDateProperty)
        {
            return;
        }

        this._draftDate = this.CalendarControl.SelectedDate?.Date;
        if (this._draftDate is not null)
        {
            this._draftTime ??= TimeSpan.Zero;
        }

        this.UpdatePickerControls();
    }

    private void OnTimePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (this._isUpdatingControls || args.Property != TimePicker.SelectedTimeProperty)
        {
            return;
        }

        this._draftTime = this.TimeControl.SelectedTime;
    }

    private void LoadDraftFromValue()
    {
        this._draftDate = this.Value?.Date;
        this._draftTime = this.Value?.TimeOfDay;
        this.UpdatePickerControls();
    }

    private void UpdatePickerControls()
    {
        this._isUpdatingControls = true;
        this.CalendarControl.SelectedDate = this._draftDate;
        this.CalendarControl.DisplayDate = this._draftDate ?? DateTime.Today;
        this.TimeControl.SelectedTime = this._draftTime ?? TimeSpan.Zero;
        this._isUpdatingControls = false;
    }

    private void UpdateDisplay()
    {
        var value = this.Value;
        this.DisplayLabel.Text = value?.ToString("dd.MM.yyyy HH:mm", this._culture) ?? this.Placeholder;
        this.DisplayLabel.Foreground = value is null ? Brush.Parse("#929BA4") : Brush.Parse("#F1F3F5");
        this.ClearButton.IsVisible = value is not null;
    }
}
