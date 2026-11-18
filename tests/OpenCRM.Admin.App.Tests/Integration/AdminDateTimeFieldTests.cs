using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.App.Views;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Tests.Integration;

[Collection("Admin localization")]
public sealed class AdminDateTimeFieldTests
{
    [AvaloniaFact]
    public void ChangingLanguageKeepsDraftAndRegionalDateFormat()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        using var preference = localization.UsePreferencePath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "language.txt"));
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        localization.Language = "ru";
        var value = new DateTime(2026, 7, 14, 15, 37, 0, DateTimeKind.Unspecified);
        var field = new AdminDateTimeField { Value = value };
        var window = new Window { Width = 620, Height = 700, Content = field };
        window.Show();

        try
        {
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeFieldOpen");
            var calendar = window.GetVisualDescendants().OfType<Avalonia.Controls.Calendar>().Single();
            var time = window.GetVisualDescendants().OfType<TimePicker>().Single();
            calendar.SelectedDate = value.AddDays(2);
            time.SelectedTime = new TimeSpan(18, 25, 0);

            foreach (var language in new[] { "en", "zh-Hans", "ru" })
            {
                localization.Language = language;
                Dispatcher.UIThread.RunJobs();
                var culture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : language == "ru" ? "ru-RU" : "zh-CN");
                var header = calendar.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_HeaderButton");
                Assert.Equal(calendar.DisplayDate.ToString("Y", culture), header.Content);
                Assert.Equal(value.AddDays(2).Date, calendar.SelectedDate);
                Assert.Equal(new TimeSpan(18, 25, 0), time.SelectedTime);
                Assert.Equal(value, field.Value);
                Assert.True(field.IsPickerOpen);
                Assert.Equal("en-GB", CultureInfo.CurrentCulture.Name);
                Assert.Contains(value.ToString("g", CultureInfo.CurrentCulture), window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            }

            AdminDateTimeFieldTests.Click(window, "AdminDateTimeDone");
            Assert.Equal(new DateTime(2026, 7, 16, 18, 25, 0, DateTimeKind.Unspecified), field.Value);
        }
        finally
        {
            window.Close();
            localization.Language = previousLanguage;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [AvaloniaFact]
    public void DateAndTimeCanBeSelectedClearedAndUpdatedThroughTwoWayBinding()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        var model = new DateTimeFieldModel();
        var field = new AdminDateTimeField { Placeholder = "Не задано" };
        field.Bind(AdminDateTimeField.ValueProperty, new Avalonia.Data.Binding(nameof(DateTimeFieldModel.Value))
        {
            Mode = Avalonia.Data.BindingMode.TwoWay
        });
        var window = new Window { Width = 620, Height = 700, Content = field, DataContext = model };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Не задано", window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeFieldOpen");
            Assert.True(field.IsPickerOpen);
            Dispatcher.UIThread.RunJobs();

            var calendar = window.GetVisualDescendants().OfType<Avalonia.Controls.Calendar>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "AdminDateTimeCalendar");
            var timePicker = window.GetVisualDescendants().OfType<TimePicker>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "AdminDateTimeTime");
            Assert.Equal(CalendarSelectionMode.SingleDate, calendar.SelectionMode);
            Assert.Equal("24HourClock", timePicker.ClockIdentifier);
            Assert.Equal(1, timePicker.MinuteIncrement);
            AdminDateTimeFieldTests.SavePopupScreenshot(window);

            calendar.SelectedDate = new DateTime(2026, 7, 14);
            timePicker.SelectedTime = new TimeSpan(15, 37, 0);
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeDone");

            var expected = new DateTime(2026, 7, 14, 15, 37, 0, DateTimeKind.Unspecified);
            Assert.Equal(expected, model.Value);
            Assert.Equal(expected, field.Value);
            Assert.Contains(expected.ToString("g", CultureInfo.CurrentCulture), window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            Assert.False(field.IsPickerOpen);

            model.Value = new DateTime(2027, 1, 2, 8, 10, 0, DateTimeKind.Unspecified);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(model.Value, field.Value);
            Assert.Contains(model.Value.Value.ToString("g", CultureInfo.CurrentCulture), window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));

            AdminDateTimeFieldTests.Click(window, "AdminDateTimeFieldClear");
            Assert.Null(model.Value);
            Assert.Null(field.Value);
            Assert.Contains("Не задано", window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [AvaloniaFact]
    public void PickingTimeWithoutDateDoesNotCreateDateValue()
    {
        var field = new AdminDateTimeField();
        var window = new Window { Width = 620, Height = 700, Content = field };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeFieldOpen");
            Dispatcher.UIThread.RunJobs();
            var timePicker = window.GetVisualDescendants().OfType<TimePicker>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "AdminDateTimeTime");
            timePicker.SelectedTime = new TimeSpan(11, 20, 0);
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeDone");

            Assert.Null(field.Value);
            Assert.False(field.IsPickerOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CancelKeepsValueAndDisablingFieldClosesPopup()
    {
        var original = new DateTime(2026, 10, 15, 9, 37, 0, DateTimeKind.Unspecified);
        var field = new AdminDateTimeField { Value = original };
        var window = new Window { Width = 620, Height = 700, Content = field };
        window.Show();
        try
        {
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeFieldOpen");
            var calendar = window.GetVisualDescendants().OfType<Avalonia.Controls.Calendar>().Single();
            calendar.SelectedDate = original.AddDays(3);
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeCancel");
            Assert.Equal(original, field.Value);
            AdminDateTimeFieldTests.Click(window, "AdminDateTimeFieldOpen");
            Assert.True(field.IsPickerOpen);
            field.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(field.IsPickerOpen);
            Assert.Equal(original, field.Value);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, string automationId)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SavePopupScreenshot(Window window)
    {
        var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(Path.Combine(directory, "admin-date-time-field-popup.png"));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private sealed class DateTimeFieldModel : INotifyPropertyChanged
    {
        private DateTime? _value;

        public event PropertyChangedEventHandler? PropertyChanged;

        public DateTime? Value
        {
            get => this._value;
            set
            {
                if (this._value == value)
                {
                    return;
                }

                this._value = value;
                this.OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
