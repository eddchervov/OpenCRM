using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenCRM.Admin.App.ViewModels;

public partial class MainWindowViewModel
{
    private string? _activityUserId;

    /// <summary> Дополнительный открытый раздел административной панели. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubscriptionPlansOpen))]
    [NotifyPropertyChangedFor(nameof(IsOrganizationsOpen))]
    [NotifyPropertyChangedFor(nameof(IsPromoCodesOpen))]
    [NotifyPropertyChangedFor(nameof(IsRolesOpen))]
    [NotifyPropertyChangedFor(nameof(IsEmailHistoryOpen))]
    [NotifyPropertyChangedFor(nameof(IsSupportMessagesOpen))]
    [NotifyPropertyChangedFor(nameof(IsUserActivityOpen))]
    [NotifyPropertyChangedFor(nameof(IsProfileOpen))]
    [NotifyPropertyChangedFor(nameof(IsScrollablePageOpen))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    private string _additionalPage = string.Empty;

    /// <summary> Управление тарифами и их функциями. </summary>
    public SubscriptionPlansViewModel SubscriptionPlans { get; private set; } = null!;

    /// <summary> Управление промокодами и историей применений. </summary>
    public PromoCodesViewModel PromoCodes { get; private set; } = null!;

    /// <summary> Просмотр списка и карточек организаций. </summary>
    public OrganizationsViewModel Organizations { get; private set; } = null!;

    /// <summary> Признак открытого списка организаций. </summary>
    public bool IsOrganizationsOpen => this.AdditionalPage == "Организации";

    /// <summary> Признак открытого раздела тарифов. </summary>
    public bool IsSubscriptionPlansOpen => this.AdditionalPage == "Тарифы";

    /// <summary> Признак открытого раздела промокодов. </summary>
    public bool IsPromoCodesOpen => this.AdditionalPage == "Промокоды";

    /// <summary> Доступ к настройке тарифов и промокодов. </summary>
    public bool CanManageConfiguration => this.HasPermission("Admin.Configuration.Manage");

    /// <summary> Доступ к коммерческим настройкам платформы. </summary>
    public bool CanManageCommercialConfiguration => this.Profile?.CommercialEnabled == true && this.CanManageConfiguration;

    /// <summary> Модель управления ролями и правами. </summary>
    public RolesViewModel Roles { get; private set; } = null!;

    /// <summary> Модель истории электронной почты. </summary>
    public EmailHistoryViewModel EmailHistory { get; private set; } = null!;

    /// <summary> Модель обращений в поддержку. </summary>
    public SupportMessagesViewModel SupportMessages { get; private set; } = null!;

    /// <summary> Журнал активности пользователей. </summary>
    public UserActivityViewModel UserActivity { get; private set; } = null!;

    /// <summary> Признак открытого раздела ролей. </summary>
    public bool IsRolesOpen => this.AdditionalPage == "Роли и права";

    /// <summary> Признак открытой истории электронной почты. </summary>
    public bool IsEmailHistoryOpen => this.AdditionalPage == "История email";

    /// <summary> Признак открытого раздела обращений. </summary>
    public bool IsSupportMessagesOpen => this.AdditionalPage == "Обращения";

    /// <summary> Признак открытого журнала активности. </summary>
    public bool IsUserActivityOpen => this.AdditionalPage == "Активность";

    /// <summary> Доступ к управлению ролями. </summary>
    public bool CanViewRoles => this.HasPermission("Admin.Permissions.Manage");

    /// <summary> Доступ к истории электронной почты. </summary>
    public bool CanViewEmailHistory => this.HasPermission("Admin.Communications.View");

    /// <summary> Доступ к обращениям. </summary>
    public bool CanViewSupportMessages => this.HasPermission("Admin.Content.View");

    /// <summary> Доступ к журналу активности. </summary>
    public bool CanViewUserActivity => this.HasPermission("Admin.Users.View");

    /// <summary> Подключает модели дополнительных разделов к сессии оболочки. </summary>
    private void InitializeSections()
    {
        this.InitializeOperations();
        this.SubscriptionPlans = new SubscriptionPlansViewModel(this._sessionClient);
        this.PromoCodes = new PromoCodesViewModel(this._sessionClient);
        this.Organizations = new OrganizationsViewModel(this._sessionClient);
        this.SubscriptionPlans.SessionExpired += this.OnSectionSessionExpired;
        this.PromoCodes.SessionExpired += this.OnSectionSessionExpired;
        this.SubscriptionPlans.AccessDenied += this.OnSectionAccessDeniedAsync;
        this.PromoCodes.AccessDenied += this.OnSectionAccessDeniedAsync;
        this.Organizations.SessionExpired += this.OnSectionSessionExpired;
        this.Organizations.AccessDenied += this.OnSectionAccessDeniedAsync;
        this.Roles = new RolesViewModel(this._sessionClient);
        this.EmailHistory = new EmailHistoryViewModel(this._sessionClient);
        this.SupportMessages = new SupportMessagesViewModel(this._sessionClient);
        this.UserActivity = new UserActivityViewModel(this._sessionClient);
        this.Roles.SessionExpired += this.OnSectionSessionExpired;
        this.EmailHistory.SessionExpired += this.OnSectionSessionExpired;
        this.SupportMessages.SessionExpired += this.OnSectionSessionExpired;
        this.Roles.AccessDenied += this.OnSectionAccessDeniedAsync;
        this.EmailHistory.AccessDenied += this.OnSectionAccessDeniedAsync;
        this.SupportMessages.AccessDenied += this.OnSectionAccessDeniedAsync;
        this.UserActivity.SessionExpired += this.OnSectionSessionExpired;
        this.UserActivity.AccessDenied += this.OnSectionAccessDeniedAsync;
        this.Users.UserActivityRequested += userId =>
        {
            this._activityUserId = userId;
            this.ShowUserActivityCommand.Execute(null);
        };
    }

    /// <summary> Возвращает пользователя на экран входа после истечения сессии. </summary>
    private void OnSectionSessionExpired()
    {
        this.SetProfile(null);
        this.ErrorMessage = "Срок действия сессии истёк. Войдите снова.";
    }

    /// <summary> Обновляет права после отказа сервера в доступе. </summary>
    private async Task OnSectionAccessDeniedAsync()
    {
        this.AdditionalPage = string.Empty;
        this.StopSections(true);
        await this.RefreshPermissionsAsync();
        if (this.IsAuthenticated)
        {
            this.ErrorMessage = "Нет доступа к операции. Права пользователя обновлены.";
        }
    }

    /// <summary> Проверяет право с учётом системного администратора. </summary>
    private bool HasPermission(string permission) => this.Profile is { } profile &&
        (profile.IsSystem || profile.Permissions.Contains(permission));

    /// <summary> Открывает управление тарифами. </summary>
    [RelayCommand]
    private Task ShowSubscriptionPlansAsync() =>
        this.ShowSectionAsync("Тарифы", this.CanManageCommercialConfiguration, this.SubscriptionPlans.ActivateAsync);

    /// <summary> Открывает управление промокодами. </summary>
    [RelayCommand]
    private Task ShowPromoCodesAsync() =>
        this.ShowSectionAsync("Промокоды", this.CanManageCommercialConfiguration, this.PromoCodes.ActivateAsync);

    /// <summary> Открывает список организаций. </summary>
    [RelayCommand]
    private Task ShowOrganizationsAsync() =>
        this.ShowSectionAsync("Организации", this.CanViewOrganizations, this.Organizations.ActivateAsync);

    /// <summary> Открывает управление ролями. </summary>
    [RelayCommand]
    private Task ShowRolesAsync() => this.ShowSectionAsync("Роли и права", this.CanViewRoles, this.Roles.ActivateAsync);

    /// <summary> Открывает историю электронной почты. </summary>
    [RelayCommand]
    private Task ShowEmailHistoryAsync() =>
        this.ShowSectionAsync("История email", this.CanViewEmailHistory, this.EmailHistory.ActivateAsync);

    /// <summary> Открывает обращения в поддержку. </summary>
    [RelayCommand]
    private Task ShowSupportMessagesAsync() =>
        this.ShowSectionAsync("Обращения", this.CanViewSupportMessages, this.SupportMessages.ActivateAsync);

    /// <summary> Открывает журнал активности всех пользователей. </summary>
    [RelayCommand]
    private Task ShowUserActivityAsync()
    {
        var userId = this._activityUserId;
        this._activityUserId = null;

        return this.ShowSectionAsync("Активность", this.CanViewUserActivity, () => this.UserActivity.ActivateAsync(userId));
    }

    /// <summary> Переключает раздел с отменой незавершённых загрузок. </summary>
    private async Task ShowSectionAsync(string title, bool canView, Func<Task> activate)
    {
        if (!canView || this._isClosing || this.AdditionalPage == title)
        {
            return;
        }

        var navigationVersion = this.AdvanceNavigationVersion();
        this.IsDashboardOpen = false;
        this.IsUsersOpen = false;
        this.IsBillingOpen = false;
        this.AdditionalPage = title;
        await Task.WhenAll(this.Dashboard.DeactivateAsync(), this.Users.DeactivateAsync(),
            this.Billing.DeactivateAsync(), this.DeactivateSectionsAsync());
        if (navigationVersion != this._navigationVersion || this._isClosing
            || this.AdditionalPage != title || !this.CanViewSection(title))
        {
            return;
        }

        await activate();
    }

    /// <summary> Проверяет актуальный доступ к выбранному разделу. </summary>
    private bool CanViewSection(string title) => title switch
    {
        "Тарифы" or "Промокоды" => this.CanManageCommercialConfiguration,
        "Роли и права" => this.CanViewRoles,
        "Организации" => this.CanViewOrganizations,
        "История email" => this.CanViewEmailHistory,
        "Обращения" => this.CanViewSupportMessages,
        "Активность" => this.CanViewUserActivity,
        _ => this.CanViewOperation(title)
    };

    /// <summary> Останавливает операции дополнительных разделов. </summary>
    private void StopSections(bool clear = false)
    {
        this.StopOperations(clear);
        this.SubscriptionPlans.Stop(clear);
        this.PromoCodes.Stop(clear);
        this.Organizations.Stop(clear);
        this.Roles.Stop(clear);
        this.EmailHistory.Stop(clear);
        this.SupportMessages.Stop(clear);
        this.UserActivity.Stop(clear);
    }

    /// <summary> Ожидает завершения отменённых операций дополнительных разделов. </summary>
    private Task DeactivateSectionsAsync(bool clear = false) => Task.WhenAll(
        this.SubscriptionPlans.DeactivateAsync(clear), this.PromoCodes.DeactivateAsync(clear), this.Organizations.DeactivateAsync(clear),
        this.Roles.DeactivateAsync(clear), this.EmailHistory.DeactivateAsync(clear), this.SupportMessages.DeactivateAsync(clear),
        this.UserActivity.DeactivateAsync(clear), this.DeactivateOperationsAsync(clear));

    /// <summary> Обновляет доступ к дополнительным разделам после обновления профиля. </summary>
    private void UpdateSectionAccess()
    {
        this.UpdateOperationAccess();
        this.Organizations.RefreshAccess();
        this.OnPropertyChanged(nameof(CanManageConfiguration));
        this.OnPropertyChanged(nameof(CanManageCommercialConfiguration));
        this.OnPropertyChanged(nameof(CanViewOrganizations));
        if (!this.CanViewOrganizations)
        {
            this.Organizations.Stop(true);
            if (this.IsOrganizationsOpen)
            {
                this.AdditionalPage = string.Empty;
            }
        }
        if (!this.CanManageCommercialConfiguration)
        {
            this.SubscriptionPlans.Stop(true);
            this.PromoCodes.Stop(true);
            if (this.IsSubscriptionPlansOpen || this.IsPromoCodesOpen)
            {
                this.AdditionalPage = string.Empty;
            }
        }

        this.SupportMessages.RefreshAccess();
        this.OnPropertyChanged(nameof(CanViewRoles));
        this.OnPropertyChanged(nameof(CanViewEmailHistory));
        this.OnPropertyChanged(nameof(CanViewSupportMessages));
        this.OnPropertyChanged(nameof(CanViewUserActivity));
        if (!this.CanViewRoles)
        {
            this.Roles.Stop(true);
            if (this.IsRolesOpen)
            {
                this.AdditionalPage = string.Empty;
            }
        }

        if (!this.CanViewEmailHistory)
        {
            this.EmailHistory.Stop(true);
            if (this.IsEmailHistoryOpen)
            {
                this.AdditionalPage = string.Empty;
            }
        }

        if (!this.CanViewSupportMessages)
        {
            this.SupportMessages.Stop(true);
            if (this.IsSupportMessagesOpen)
            {
                this.AdditionalPage = string.Empty;
            }
        }

        if (!this.CanViewUserActivity)
        {
            this.UserActivity.Stop(true);
            if (this.IsUserActivityOpen)
            {
                this.AdditionalPage = string.Empty;
            }
        }
    }
}
