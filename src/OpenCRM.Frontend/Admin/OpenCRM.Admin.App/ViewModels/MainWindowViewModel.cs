using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

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

    /// <summary> Исходные подробности ошибки конфигурации сервера. </summary>
    private readonly string? _configurationErrorDetails;

    /// <summary> Признак закрытия окна. </summary>
    private bool _isClosing;

    /// <summary> Версия навигации для отмены устаревших операций. </summary>
    private int _navigationVersion;

    private string? _errorMessageTranslationKey;
    private object?[] _errorMessageArguments = [];

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

    partial void OnErrorMessageChanging(string value) => this._errorMessageTranslationKey = null;

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
        this.Permissions.CollectionChanged += (_, _) => this.OnPropertyChanged(nameof(this.PermissionCountLabel));
        this._sessionClient.UiLanguage = AdminLocalization.Instance.Language;
        this._serverAddress = serverAddress;
        this._productSections = productSections ?? [];
        this._hasConfigurationError = configurationError != null;
        this._configurationErrorDetails = configurationError;
        this.ServerAddress = serverAddress.GetLeftPart(UriPartial.Authority);
        this.ErrorMessage = configurationError == null
            ? string.Empty
            : AdminLocalization.Instance["error.configuration"] + " " + AdminLocalization.Instance[configurationError];
        this.Dashboard = new DashboardViewModel(sessionClient, refreshSeconds);
        this.Dashboard.SessionExpired += () =>
        {
            this.SetProfile(null);
            this.SetLocalizedError("error.sessionExpired");
        };
        this.Dashboard.AccessDenied += async () =>
        {
            this.IsDashboardOpen = false;
            this.Dashboard.Stop(true);
            await this.RefreshPermissionsAsync();

            if (this.IsAuthenticated)
            {
                this.SetLocalizedError("error.dashboardAccess");
            }
        };
        this.Dashboard.BillingAccessDenied += this.RefreshPermissionsAsync;
        this.Users = new UsersViewModel(sessionClient);
        this.Users.SessionExpired += () =>
        {
            this.SetProfile(null);
            this.SetLocalizedError("error.sessionExpired");
        };
        this.Users.AccessDenied += async () =>
        {
            this.IsUsersOpen = false;
            this.Users.Stop(true);
            await this.RefreshPermissionsAsync();
            if (this.IsAuthenticated)
            {
                this.SetLocalizedError("error.usersAccess");
            }
        };
        this.InitializeSections();
        this.Billing = new BillingViewModel(sessionClient, pendingActionStore);
        this.Billing.PermissionsRefreshRequested += this.RefreshPermissionsAsync;
        this.Billing.SessionExpired += () =>
        {
            this.SetProfile(null);
            this.SetLocalizedError("error.sessionExpired");
        };
        this.Billing.AccessDenied += async () =>
        {
            this.IsBillingOpen = false;
            this.Billing.Stop(true);
            await this.RefreshPermissionsAsync();
            if (this.IsAuthenticated)
            {
                this.SetLocalizedError("error.billingAccess");
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

    /// <summary> Подпись количества разрешений в текущем языке интерфейса. </summary>
    public string PermissionCountLabel => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        AdminLocalization.Instance["core.profile.permissionCount"], this.Permissions.Count);

    public IReadOnlyList<AdminUiLanguageOption> Languages => AdminLocalization.Instance.Languages;

    public string Language
    {
        get => AdminLocalization.Instance.Language;
        set
        {
            if (AdminLocalization.Instance.Language == value)
            {
                return;
            }

            AdminLocalization.Instance.Language = value;
            this._sessionClient.UiLanguage = AdminLocalization.Instance.Language;
            if (this._configurationErrorDetails != null)
            {
                this.ErrorMessage = AdminLocalization.Instance["error.configuration"] + " " + AdminLocalization.Instance[this._configurationErrorDetails];
            }

            if (this._errorMessageTranslationKey is { } errorKey)
            {
                this.SetLocalizedError(errorKey, this._errorMessageArguments);
            }

            this.OnPropertyChanged(nameof(this.Language));
            this.OnPropertyChanged(nameof(this.SelectedLanguage));
            for (var index = 0; index < this.Permissions.Count; index++)
            {
                var code = this.Permissions[index].Code;
                this.Permissions[index] = new PermissionItem(MainWindowViewModel.GetPermissionTitle(code), code);
            }

            this.OnPropertyChanged(nameof(this.PermissionCountLabel));
            this.OnPropertyChanged(nameof(this.PageLabel));
            this.OnPropertyChanged(nameof(this.AccessLabel));
            foreach (var item in this.NavigationItems)
            {
                item.RefreshTitle();
            }

            foreach (var section in this._operationSections)
            {
                section.RefreshTitle();
                section.Model.RefreshLocalizedValues();
            }

            this.Dashboard.RefreshLocalizedValues();
            this.Users.RefreshLocalizedValues();
            this.Organizations.RefreshLocalizedValues();
            this.UserActivity.RefreshLocalizedValues();
            this.Billing.RefreshLocalizedValues();
            this.SubscriptionPlans.RefreshLocalizedValues();
            this.PromoCodes.RefreshLocalizedValues();
            this.Roles.RefreshLocalizedValues();
            this.EmailHistory.RefreshLocalizedValues();
            this.SupportMessages.RefreshLocalizedValues();
        }
    }

    public AdminUiLanguageOption SelectedLanguage
    {
        get => AdminUiLanguage.Supported.First(language => language.Code == this.Language);
        set => this.Language = value.Code;
    }

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
    public string PageLabel => !string.IsNullOrEmpty(this.AdditionalPage)
        ? AdminLocalization.Instance[GetSectionTitleKey(this.AdditionalPage)] : this.IsUsersOpen
        ? this.Users.IsCardOpen ? AdminLocalization.Instance["users.cardPage"] : AdminLocalization.Instance["nav.users"]
        : this.IsBillingOpen ? AdminLocalization.Instance["nav.billing"]
        : this.IsDashboardOpen ? AdminLocalization.Instance["nav.dashboard"] : AdminLocalization.Instance["page.profile"];

    private void SetLocalizedError(string key, params object?[] arguments)
    {
        this.ErrorMessage = AdminLocalization.Instance.Format(key, arguments);
        this._errorMessageTranslationKey = key;
        this._errorMessageArguments = arguments;
    }

    private void SetApiError(ApiException exception)
    {
        if (AdminDisplayLabels.TransportErrorKey(exception) is { } transportKey)
        {
            this.SetLocalizedError(transportKey);

            return;
        }

        this._errorMessageTranslationKey = null;
        this.ErrorMessage = exception.Message;
    }

    private static string GetSectionTitleKey(string sectionId) => sectionId switch
    {
        "plans" => "nav.plans",
        "promo-codes" => "nav.promoCodes",
        "organizations" => "nav.organizations",
        "roles" => "nav.roles",
        "email-history" => "nav.emailHistory",
        "support" => "nav.support",
        "user-activity" => "operation.userActivity",
        "compose" => "operation.compose",
        "campaigns" => "operation.campaigns",
        "sms-history" => "operation.smsHistory",
        "site-notifications" => "operation.siteNotifications",
        "vehicle-catalog" => "operation.vehicleCatalog",
        "catalog-suggestions" => "operation.catalogSuggestions",
        "test-data" => "operation.testData",
        "crm-import" => "operation.crmImport",
        "assistant" => "operation.assistant",
        _ => sectionId
    };

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
    public string AccessLabel => this.Profile?.IsSystem == true
        ? AdminLocalization.Instance["profile.systemAdmin"] : AdminLocalization.Instance["profile.adminAccess"];

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
            this.SetApiError(exception);
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
            this.SetApiError(exception);
        }
        catch (HttpRequestException)
        {
            this.SetLocalizedError("error.network");
        }
        catch (OperationCanceledException)
        {
            this.SetLocalizedError("error.timeout");
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

            this.SetApiError(exception);
        }
        catch (HttpRequestException)
        {
            this.SetLocalizedError("error.profileRefreshNetwork");
        }
        catch (OperationCanceledException)
        {
            this.SetLocalizedError("error.profileRefreshTimeout");
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
            if (AdminDisplayLabels.TransportErrorKey(exception) is { } transportKey)
            {
                this.SetLocalizedError(transportKey);
            }
            else
            {
                this.SetLocalizedError("core.session.logoutFailed", exception.Message);
            }
        }
        catch (HttpRequestException)
        {
            this.SetLocalizedError("error.logoutNetwork");
        }
        catch (OperationCanceledException)
        {
            this.SetLocalizedError("error.logoutTimeout");
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
            new("dashboard", "nav.dashboard", "Admin.Dashboard.View"),
            new("users", "nav.users", "Admin.Users.View"),
            new("organizations", "nav.organizations", "Admin.Users.View"),
            new("billing", "nav.billing", "Admin.Billing.View"),
            new("support", "nav.support", "Admin.Content.View"),
            new("email-history", "nav.emailHistory", "Admin.Communications.View"),
            new("plans", "nav.plans", "Admin.Configuration.Manage"),
            new("promo-codes", "nav.promoCodes", "Admin.Configuration.Manage"),
            new("roles", "nav.roles", "Admin.Permissions.Manage")
        ];

        foreach (var item in items)
        {
            if ((item.Id is not ("billing" or "plans" or "promo-codes") || profile.CommercialEnabled)
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
        "Admin.Access" => AdminLocalization.Instance["core.permission.Admin.Access"],
        "Admin.Dashboard.View" => AdminLocalization.Instance["core.permission.Admin.Dashboard.View"],
        "Admin.Users.View" => AdminLocalization.Instance["core.permission.Admin.Users.View"],
        "Admin.Users.Manage" => AdminLocalization.Instance["core.permission.Admin.Users.Manage"],
        "Admin.Users.Impersonate" => AdminLocalization.Instance["core.permission.Admin.Users.Impersonate"],
        "Admin.Billing.View" => AdminLocalization.Instance["core.permission.Admin.Billing.View"],
        "Admin.Billing.Manage" => AdminLocalization.Instance["core.permission.Admin.Billing.Manage"],
        "Admin.Content.View" => AdminLocalization.Instance["core.permission.Admin.Content.View"],
        "Admin.Content.Manage" => AdminLocalization.Instance["core.permission.Admin.Content.Manage"],
        "Admin.Communications.View" => AdminLocalization.Instance["core.permission.Admin.Communications.View"],
        "Admin.Communications.Manage" => AdminLocalization.Instance["core.permission.Admin.Communications.Manage"],
        "Admin.Configuration.Manage" => AdminLocalization.Instance["core.permission.Admin.Configuration.Manage"],
        "Admin.Permissions.Manage" => AdminLocalization.Instance["core.permission.Admin.Permissions.Manage"],
        _ => code
    };
}
