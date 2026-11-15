using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;
using OpenCRM.Admin.Api;
using Helpers;

namespace OpenCRM.Admin.App;

/// <summary> Класс приложения Avalonia администраторской панели. </summary>
public partial class App : Application
{
    /// <summary> Инициализирует ресурсы приложения из XAML. </summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary> Завершает инициализацию приложения и настраивает зависимости. </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var brand = ProjectBrand.Read(_ => null);
            var address = new Uri(brand.PublicBaseUrl);
            var storageSettings = new DesktopStorageSettings(null, null);
            IReadOnlyList<AdminProductSection> productSections = [];
            string? configurationError = null;
            try
            {
                brand = DesktopSettings.LoadBrand();
                address = DesktopSettings.LoadApiAddress(brand);
                var brandFilePath = Path.Combine(AppContext.BaseDirectory, "brand.json");
                storageSettings = DesktopStorageSettings.Load(brandFilePath);
                productSections = DesktopSettings.LoadProductSections(brandFilePath);
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            {
                configurationError = "Не удалось прочитать настройки сервера. " + exception.Message;
            }

            var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                BaseAddress = address,
                Timeout = TimeSpan.FromSeconds(20),
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };

            IAdminSessionStore? sessionStore = OperatingSystem.IsWindows()
                ? new ProtectedAdminSessionStore(address, storageSettings.SessionDirectory)
                : null;
            var sessionClient = new AdminSessionClient(httpClient, sessionStore: sessionStore);
            var viewModel = new MainWindowViewModel(
                sessionClient,
                address,
                configurationError,
                DashboardPreferences.LoadRefreshSeconds(storageSettings.DashboardPreferencesFile),
                new PendingPaymentActionStore(storageSettings.PendingActionsDirectory),
                productSections);
            viewModel.Dashboard.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(DashboardViewModel.RefreshSeconds))
                {
                    DashboardPreferences.SaveRefreshSeconds(
                        viewModel.Dashboard.RefreshSeconds,
                        storageSettings.DashboardPreferencesFile);
                }
            };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            timer.Tick += (_, _) =>
            {
                _ = viewModel.RefreshBackgroundProfileAsync();
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                Title = $"{brand.DisplayName} — Администрирование"
            };
            desktop.MainWindow.Opened += async (_, _) =>
            {
                if (viewModel.RestoreSessionCommand.CanExecute(null))
                {
                    await viewModel.RestoreSessionCommand.ExecuteAsync(null);
                }
            };
            viewModel.Users.Management.ImpersonationRequested += (userId, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var userCardUri = new Uri(address, $"/Admin/User?userId={Uri.EscapeDataString(userId)}");
                using var browser = Process.Start(new ProcessStartInfo(userCardUri.AbsoluteUri) { UseShellExecute = true });

                return Task.CompletedTask;
            };
            var isClosing = false;
            var canClose = false;
            desktop.MainWindow.Closing += async (_, eventArgs) =>
            {
                if (canClose)
                {
                    return;
                }

                eventArgs.Cancel = true;
                if (isClosing)
                {
                    return;
                }

                isClosing = true;
                timer.Stop();
                await viewModel.ShutdownAsync();
                sessionClient.Dispose();
                httpClient.Dispose();
                canClose = true;
                desktop.MainWindow.Close();
            };

            timer.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
