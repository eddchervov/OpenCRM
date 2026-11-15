using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель представления главного окна админ-приложения. </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary> Клиент административной сессии. </summary>
    private readonly AdminSessionClient _sessionClient;

    /// <summary> Исходный адрес сервера, включая PathBase. </summary>
    private readonly Uri _serverAddress;

    /// <summary> Дополнительные разделы, заданные продуктом. </summary>
    private readonly IReadOnlyList<AdminProductSection> _productSections;

    /// <summary> Признак ошибки конфигурации приложения. </summary>
    private readonly bool _hasConfigurationError;

    /// <summary> Признак закрытия окна. </summary>
    private bool _isClosing;

    /// <summary> Версия навигации для отмены устаревших операций. </summary>
    private int _navigationVersion;

    /// <summary> Признак открытого дашборда. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileOpen))]
    [NotifyPropertyChangedFor(nameof(IsScrollablePageOpen))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    private bool _isDashboardOpen;

    /// <summary> Признак открытого раздела пользователей. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileOpen))]
    [NotifyPropertyChangedFor(nameof(IsScrollablePageOpen))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    private bool _isUsersOpen;

    /// <summary> Признак открытого раздела биллинга. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileOpen))]
    [NotifyPropertyChangedFor(nameof(IsScrollablePageOpen))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    private bool _isBillingOpen;

    /// <summary> Введённый email для входа. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _email = string.Empty;

    /// <summary> Введённый пароль для входа. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    /// <summary> Признак выполнения операции. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProfileCommand))]
    private bool _isBusy;

    /// <summary> Текст ошибки. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    /// <summary> Профиль администратора. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthenticated))]
    [NotifyPropertyChangedFor(nameof(ProfileEmail))]
    [NotifyPropertyChangedFor(nameof(ProfileId))]
    [NotifyPropertyChangedFor(nameof(RolesLabel))]
    [NotifyPropertyChangedFor(nameof(AvatarText))]
    [NotifyPropertyChangedFor(nameof(AccessLabel))]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshProfileCommand))]
    private AdminProfile? _profile;

    /// <summary> Создаёт модель представления главного окна. </summary>
    public MainWindowViewModel(
        AdminSessionClient sessionClient,
        Uri serverAddress,
        string? configurationError = null,
        int refreshSeconds = 60,
        PendingPaymentActionStore? pendingActionStore = null,
        IReadOnlyList<AdminProductSection>? productSections = null)
    {
        this._sessionClient = sessionClient;
        this._serverAddress = serverAddress;
        this._productSections = productSections ?? [];
        this._hasConfigurationError = configurationError != null;
        this.ServerAddress = serverAddress.GetLeftPart(UriPartial.Authority);
        this.ErrorMessage = configurationError ?? string.Empty;
        this.Dashboard = new DashboardViewModel(sessionClient, refreshSeconds);
        this.Dashboard.SessionExpired += () =>
        {
            this.SetProfile(null);
            this.ErrorMessage = "Срок действия сессии истёк. Войдите снова.";
        };
        this.Dashboard.AccessDenied += async () =>
        {
            this.IsDashboardOpen = false;
            this.Dashboard.Stop(true);
            await this.RefreshPermissionsAsync();

            if (this.IsAuthenticated)
            {
                this.ErrorMessage = "Нет доступа к дашборду. Права пользователя обновлены.";
            }
        };
        this.Dashboard.BillingAccessDenied += this.RefreshPermissionsAsync;
        this.Users = new UsersViewModel(sessionClient);
        this.Users.SessionExpired += () =>
        {
            this.SetProfile(null);
            this.ErrorMessage = "Срок действия сессии истёк. Войдите снова.";
        };
        this.Users.AccessDenied += async () =>
        {
            this.IsUsersOpen = false;
            this.Users.Stop(true);
            await this.RefreshPermissionsAsync();
            if (this.IsAuthenticated)
            {
                this.ErrorMessage = "Нет доступа к пользователям. Права пользователя обновлены.";
            }
        };
        this.InitializeSections();
        this.Billing = new BillingViewModel(sessionClient, pendingActionStore);
        this.Billing.PermissionsRefreshRequested += this.RefreshPermissionsAsync;
        this.Billing.SessionExpired += () =>
        {
            this.SetProfile(null);
            this.ErrorMessage = "Срок действия сессии истёк. Войдите снова.";
        };
        this.Billing.AccessDenied += async () =>
        {
            this.IsBillingOpen = false;
            this.Billing.Stop(true);
            await this.RefreshPermissionsAsync();
            if (this.IsAuthenticated)
            {
                this.ErrorMessage = "Нет доступа к платежам и подпискам. Права пользователя обновлены.";
            }
        };
        this.InitializeNavigationReadTracking();
        this.Users.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(UsersViewModel.IsCardOpen))
            {
                this.OnPropertyChanged(nameof(PageLabel));
            }
        };
    }

    /// <summary> Элементы навигационного меню. </summary>
    public ObservableCollection<NavigationItem> NavigationItems { get; } = [];

    /// <summary> Список разрешений администратора. </summary>
    public ObservableCollection<PermissionItem> Permissions { get; } = [];

    /// <summary> Доступные дополнительные ссылки продукта. </summary>
    public ObservableCollection<AdminProductSection> VisibleProductSections { get; } = [];

    /// <summary> Признак наличия доступных дополнительных ссылок продукта. </summary>
    public bool HasVisibleProductSections => this.VisibleProductSections.Count > 0;

    /// <summary> Адрес сервера. </summary>
    public string ServerAddress { get; }

    /// <summary> Модель представления дашборда. </summary>
    public DashboardViewModel Dashboard { get; }

    /// <summary> Модель представления пользователей. </summary>
    public UsersViewModel Users { get; }

    /// <summary> Модель представления биллинга. </summary>
    public BillingViewModel Billing { get; }

    /// <summary> Признак открытого профиля. </summary>
    public bool IsProfileOpen => !this.IsDashboardOpen && !this.IsUsersOpen && !this.IsBillingOpen
        && string.IsNullOrEmpty(this.AdditionalPage);

    /// <summary> Признак открытой прокручиваемой страницы. </summary>
    public bool IsScrollablePageOpen => this.IsProfileOpen || this.IsDashboardOpen;

    /// <summary> Доступ к разделу биллинга. </summary>
    public bool CanViewBilling => this.Profile is { CommercialEnabled: true } profile &&
        (profile.IsSystem || profile.Permissions.Contains("Admin.Billing.View"));

    /// <summary> Доступ к разделу пользователей. </summary>
    public bool CanViewUsers => this.Profile is { } profile &&
        (profile.IsSystem || profile.Permissions.Contains("Admin.Users.View"));

    /// <summary> Доступ к списку организаций. </summary>
    public bool CanViewOrganizations => this.CanViewUsers;

    /// <summary> Доступ к дашборду. </summary>
    public bool CanViewDashboard => this.Profile is { } profile &&
        (profile.IsSystem || profile.Permissions.Contains("Admin.Dashboard.View"));

    /// <summary> Подпись текущей страницы. </summary>
    public string PageLabel => !string.IsNullOrEmpty(this.AdditionalPage) ? this.AdditionalPage : this.IsUsersOpen
        ? this.Users.IsCardOpen ? "Пользователи / Карточка" : "Пользователи"
        : this.IsBillingOpen ? "Платежи и подписки" : this.IsDashboardOpen ? "Дашборд" : "Профиль";

    /// <summary> Открывает раздел дашборда. </summary>
    [RelayCommand]
    private async Task ShowDashboardAsync()
    {
        if (!this.CanViewDashboard || this._isClosing || this.IsDashboardOpen)
        {
            return;
        }

        var navigationVersion = this.AdvanceNavigationVersion();
        this.AdditionalPage = string.Empty;
        this.IsUsersOpen = false;
        this.IsDashboardOpen = true;
        this.IsBillingOpen = false;
        await Task.WhenAll(this.Users.DeactivateAsync(), this.Billing.DeactivateAsync(), this.DeactivateSectionsAsync());
        if (navigationVersion != this._navigationVersion || this._isClosing || !this.CanViewDashboard)
        {
            return;
        }

        await this.Dashboard.ActivateAsync(this.CanViewBilling);
    }

    /// <summary> Открывает раздел профиля. </summary>
    [RelayCommand]
    private async Task ShowProfileAsync()
    {
        this.AdvanceNavigationVersion();
        this.AdditionalPage = string.Empty;
        this.IsDashboardOpen = false;
        this.IsUsersOpen = false;
        this.IsBillingOpen = false;
        await Task.WhenAll(this.Dashboard.DeactivateAsync(), this.Users.DeactivateAsync(),
            this.Billing.DeactivateAsync(), this.DeactivateSectionsAsync());
    }

    /// <summary> Открывает раздел пользователей. </summary>
    [RelayCommand]
    private async Task ShowUsersAsync()
    {
        if (!this.CanViewUsers || this._isClosing || this.IsUsersOpen)
        {
            return;
        }

        var navigationVersion = this.AdvanceNavigationVersion();
        this.AdditionalPage = string.Empty;
        this.IsDashboardOpen = false;
        this.IsUsersOpen = true;
        this.IsBillingOpen = false;
        await Task.WhenAll(this.Dashboard.DeactivateAsync(), this.Billing.DeactivateAsync(), this.DeactivateSectionsAsync());
        if (navigationVersion != this._navigationVersion || this._isClosing || !this.CanViewUsers)
        {
            return;
        }

        await this.Users.ActivateAsync(this.CanViewBilling);
    }

    /// <summary> Открывает раздел биллинга. </summary>
    [RelayCommand]
    private async Task ShowBillingAsync()
    {
        if (!this.CanViewBilling || this._isClosing || this.IsBillingOpen)
        {
            return;
        }

        var navigationVersion = this.AdvanceNavigationVersion();
        this.AdditionalPage = string.Empty;
        this.IsDashboardOpen = false;
        this.IsUsersOpen = false;
        this.IsBillingOpen = true;
        await Task.WhenAll(this.Dashboard.DeactivateAsync(), this.Users.DeactivateAsync(), this.DeactivateSectionsAsync());
        if (navigationVersion != this._navigationVersion || this._isClosing || !this.CanViewBilling)
        {
            return;
        }

        await this.Billing.ActivateAsync();
    }

    /// <summary> Открывает дополнительный раздел продукта в системном браузере. </summary>
    [RelayCommand]
    private void OpenProductSection(AdminProductSection? section)
    {
        if (section is null || !this.VisibleProductSections.Contains(section))
        {
            return;
        }

        var sectionUri = section.BuildUri(this._serverAddress);
        using var browser = Process.Start(new ProcessStartInfo(sectionUri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary> Признак выполненной аутентификации. </summary>
    public bool IsAuthenticated => this.Profile != null;

    /// <summary> Признак наличия ошибки. </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    /// <summary> Email профиля. </summary>
    public string ProfileEmail => this.Profile?.Email ?? string.Empty;

    /// <summary> Идентификатор профиля администратора. </summary>
    public string ProfileId => this.Profile?.Id ?? string.Empty;

    /// <summary> Подпись ролей администратора. </summary>
    public string RolesLabel => this.Profile == null ? string.Empty : string.Join(", ", this.Profile.Roles);

    /// <summary> Текст аватара профиля. </summary>
    public string AvatarText => string.IsNullOrWhiteSpace(this.ProfileEmail) ? "M" : this.ProfileEmail[..1].ToUpperInvariant();

    /// <summary> Подпись уровня доступа. </summary>
    public string AccessLabel => this.Profile?.IsSystem == true ? "Системный администратор" : "Административный доступ";

    /// <summary> Отменяет текущие операции. </summary>
    public void CancelOperations()
    {
        this.StopNavigationUpdates();
        this.AdvanceNavigationVersion();
        this.AdditionalPage = string.Empty;
        this.LoginCommand.Cancel();
        this.RestoreSessionCommand.Cancel();
        this.RefreshProfileCommand.Cancel();
        this.LogoutCommand.Cancel();
        this.Dashboard.Stop();
        this.Users.Stop();
        this.Billing.Stop();
        this.StopSections();
    }

    /// <summary> Корректно завершает работу модели. </summary>
    public async Task ShutdownAsync()
    {
        this._isClosing = true;
        this.LoginCommand.NotifyCanExecuteChanged();
        this.RestoreSessionCommand.NotifyCanExecuteChanged();
        this.RefreshProfileCommand.NotifyCanExecuteChanged();
        this.LogoutCommand.NotifyCanExecuteChanged();
        this.CancelOperations();
        await Task.WhenAll(this.Dashboard.DeactivateAsync(true), this.Users.DeactivateAsync(true),
            this.Billing.DeactivateAsync(true), this.DeactivateSectionsAsync(true), this.NavigationUpdatesTask);

        var operations = new[]
        {
            this.LoginCommand.ExecutionTask,
            this.RestoreSessionCommand.ExecutionTask,
            this.RefreshProfileCommand.ExecutionTask,
            this.LogoutCommand.ExecutionTask
        }.OfType<Task>();

        await Task.WhenAll(operations);
    }

    /// <summary> Определяет доступность входа. </summary>
    private bool CanLogin() => !this._isClosing && !this.IsBusy && !this.IsAuthenticated && !this._hasConfigurationError &&
        !string.IsNullOrWhiteSpace(this.Email) && !string.IsNullOrWhiteSpace(this.Password);

    /// <summary> Определяет доступность операций с сессией. </summary>
    private bool CanUseSession() => !this._isClosing && this.IsAuthenticated && !this.IsBusy;

    /// <summary> Определяет доступность восстановления сессии. </summary>
    private bool CanRestoreSession() => !this._isClosing && !this.IsAuthenticated && !this.IsBusy && !this._hasConfigurationError;

    /// <summary> Восстанавливает вход после открытия приложения. </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreSession))]
    private async Task RestoreSessionAsync(CancellationToken cancellationToken)
    {
        this.IsBusy = true;
        this.ErrorMessage = string.Empty;

        try
        {
            this.SetProfile(await this._sessionClient.RestoreSessionAsync(cancellationToken));
            await this.ShowDashboardAsync();
        }
        catch (ApiException exception)
        {
            this.ErrorMessage = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Закрытие окна отменяет восстановление, сохраняя токен для следующего запуска.
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    /// <summary> Обновляет разрешения администратора. </summary>
    private async Task RefreshPermissionsAsync()
    {
        if (this.RefreshProfileCommand.IsRunning && this.RefreshProfileCommand.ExecutionTask is { } operation)
        {
            await operation;

            return;
        }

        await this.RefreshProfileCommand.ExecuteAsync(null);
    }

    /// <summary> Выполняет вход по email и паролю. </summary>
    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        this.IsBusy = true;
        this.ErrorMessage = string.Empty;
        var password = this.Password;
        this.Password = string.Empty;

        try
        {
            this.SetProfile(await this._sessionClient.LoginAsync(this.Email.Trim(), password, cancellationToken));
            await this.ShowDashboardAsync();
        }
        catch (ApiException exception)
        {
            this.ErrorMessage = exception.Message;
        }
        catch (HttpRequestException)
        {
            this.ErrorMessage = "Не удалось связаться с сервером. Проверьте подключение и повторите вход.";
        }
        catch (OperationCanceledException)
        {
            this.ErrorMessage = "Сервер не ответил вовремя. Повторите вход.";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    /// <summary> Обновляет профиль администратора. </summary>
    [RelayCommand(CanExecute = nameof(CanUseSession))]
    private async Task RefreshProfileAsync(CancellationToken cancellationToken)
    {
        this.IsBusy = true;
        this.ErrorMessage = string.Empty;

        try
        {
            this.SetProfile(await this._sessionClient.GetProfileAsync(cancellationToken));
        }
        catch (ApiException exception)
        {
            if (!this._sessionClient.HasSession || exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                this.SetProfile(null);
            }

            this.ErrorMessage = exception.Message;
        }
        catch (HttpRequestException)
        {
            this.ErrorMessage = "Не удалось обновить профиль и права. Проверьте подключение к серверу.";
        }
        catch (OperationCanceledException)
        {
            this.ErrorMessage = "Сервер не ответил вовремя. Не удалось обновить профиль.";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    /// <summary> Выполняет выход из сессии. </summary>
    [RelayCommand(CanExecute = nameof(CanUseSession))]
    private async Task LogoutAsync(CancellationToken cancellationToken)
    {
        this.StopNavigationUpdates();
        this.AdvanceNavigationVersion();
        this.AdditionalPage = string.Empty;
        this.IsBusy = true;
        this.ErrorMessage = string.Empty;
        await Task.WhenAll(this.Dashboard.DeactivateAsync(true), this.Users.DeactivateAsync(true),
            this.Billing.DeactivateAsync(true), this.DeactivateSectionsAsync(true), this.NavigationUpdatesTask);

        try
        {
            await this._sessionClient.LogoutAsync(cancellationToken);
        }
        catch (ApiException exception)
        {
            this.ErrorMessage = "Не удалось полностью завершить выход. " + exception.Message;
        }
        catch (HttpRequestException)
        {
            this.ErrorMessage = "Вы вышли из приложения. Сервер недоступен, отзыв сессии не подтверждён.";
        }
        catch (OperationCanceledException)
        {
            this.ErrorMessage = "Вы вышли из приложения. Сервер не подтвердил отзыв сессии вовремя.";
        }
        finally
        {
            this.SetProfile(null);
            this.Password = string.Empty;
            this.IsBusy = false;
        }
    }

    /// <summary> Устанавливает профиль и связанные данные. </summary>
    private void SetProfile(AdminProfile? profile)
    {
        this.Profile = profile;
        this.UpdateSectionAccess();
        this.Billing.RefreshAccess();
        this.OnPropertyChanged(nameof(CanViewDashboard));
        this.OnPropertyChanged(nameof(CanViewUsers));
        this.OnPropertyChanged(nameof(CanViewOrganizations));
        this.OnPropertyChanged(nameof(CanViewBilling));
        if (!this.CanViewDashboard)
        {
            this.IsDashboardOpen = false;
            this.Dashboard.Stop(true);
        }

        if (!this.CanViewUsers)
        {
            this.IsUsersOpen = false;
            this.Users.Stop(true);
        }

        if (!this.CanViewBilling)
        {
            this.IsBillingOpen = false;
            this.Billing.Stop(true);
        }

        this.Users.SetBillingAccess(this.CanViewBilling);
        this.Organizations.SetCommercialEnabled(profile?.CommercialEnabled == true);
        this.Dashboard.SetBillingAccess(this.CanViewBilling);
        this.NavigationItems.Clear();
        this.Permissions.Clear();
        this.VisibleProductSections.Clear();
        this.OnPropertyChanged(nameof(HasVisibleProductSections));
        this.StartNavigationUpdates();
        if (profile == null)
        {
            return;
        }

        NavigationItem[] items =
        [
            new("Дашборд", "Admin.Dashboard.View"),
            new("Пользователи", "Admin.Users.View"),
            new("Организации", "Admin.Users.View"),
            new("Платежи и подписки", "Admin.Billing.View"),
            new("Обращения", "Admin.Content.View"),
            new("История email", "Admin.Communications.View"),
            new("Тарифы", "Admin.Configuration.Manage"),
            new("Промокоды", "Admin.Configuration.Manage"),
            new("Роли и права", "Admin.Permissions.Manage")
        ];

        foreach (var item in items)
        {
            if ((item.Title is not ("Платежи и подписки" or "Тарифы" or "Промокоды") || profile.CommercialEnabled)
                && (profile.IsSystem || profile.Permissions.Contains(item.Permission)))
            {
                this.NavigationItems.Add(item);
            }
        }

        foreach (var section in this._productSections)
        {
            if (section.IsVisibleTo(profile))
            {
                this.VisibleProductSections.Add(section);
            }
        }

        foreach (var code in profile.Permissions.Order(StringComparer.Ordinal))
        {
            this.Permissions.Add(new PermissionItem(MainWindowViewModel.GetPermissionTitle(code), code));
        }
    }

    /// <summary> Возвращает название разрешения по коду. </summary>
    private static string GetPermissionTitle(string code) => code switch
    {
        "Admin.Access" => "Вход в административную панель",
        "Admin.Dashboard.View" => "Просмотр дашборда",
        "Admin.Users.View" => "Просмотр пользователей",
        "Admin.Users.Manage" => "Управление пользователями",
        "Admin.Users.Impersonate" => "Вход от имени пользователя",
        "Admin.Billing.View" => "Просмотр платежей и подписок",
        "Admin.Billing.Manage" => "Управление платежами и подписками",
        "Admin.Content.View" => "Просмотр обращений и контента",
        "Admin.Content.Manage" => "Управление контентом",
        "Admin.Communications.View" => "Просмотр коммуникаций",
        "Admin.Communications.Manage" => "Управление коммуникациями",
        "Admin.Configuration.Manage" => "Настройка продукта",
        "Admin.Permissions.Manage" => "Управление ролями и правами",
        _ => code
    };
}
