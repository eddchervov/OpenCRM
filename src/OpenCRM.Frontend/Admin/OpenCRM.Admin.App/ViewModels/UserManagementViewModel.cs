using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Подтверждённые действия управления аккаунтом и подписками пользователя. </summary>
public partial class UserManagementViewModel(AdminSessionClient sessionClient) : ObservableObject
{
    private readonly AdminSessionClient _sessionClient = sessionClient;
    private AdminUserOverview? _overview;
    private string? _userId;
    private bool _canViewBilling;
    private bool _isActive;
    private bool _refreshRequired;
    private int _version;
    private int _overviewRevision;
    private string? _pendingAction;
    private IReadOnlyList<AdminAvailablePlan> _availablePlans = [];
    private bool _lastCanImpersonate;
    private bool _lastCanManageTests;
    private bool _lastCanManageBilling;
    private string? _errorDetails;
    private string? _errorSuffixKey;
    private string? _errorDetailsTranslationKey;
    private string? _successTranslationKey;
    private bool _isRefreshingLocalizedValues;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuccess))]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _reason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewEndDate))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private int _days = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewEndDate))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private AdminAvailablePlan? _selectedPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewEndDate))]
    [NotifyCanExecuteChangedFor(nameof(BeginActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private UserManagementTarget? _selectedTarget;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isConfirmationOpen;

    [ObservableProperty]
    private string _confirmationTitle = string.Empty;

    [ObservableProperty]
    private string _confirmationText = string.Empty;

    [ObservableProperty]
    private bool _needsReason;

    [ObservableProperty]
    private bool _needsPlan;

    [ObservableProperty]
    private bool _needsDays;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargets))]
    private IReadOnlyList<UserManagementTarget> _targets = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPlans))]
    private IReadOnlyList<AdminAvailablePlan> _plans = [];

    /// <summary> Событие истечения сессии. </summary>
    public event Action? SessionExpired;

    /// <summary> Событие отказа в доступе. </summary>
    public event Func<Task>? AccessDenied;

    /// <summary> Просит родительскую модель обновить карточку пользователя. </summary>
    public event Func<CancellationToken, Task>? UserChanged;

    /// <summary> Просит открыть карточку пользователя в браузерной админке. </summary>
    public event Func<string, CancellationToken, Task>? ImpersonationRequested;

    /// <summary> Признак ошибки управления. </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    /// <summary> Признак успешной операции управления. </summary>
    public bool HasSuccess => !string.IsNullOrWhiteSpace(this.SuccessMessage);

    /// <summary> Признак доступности любых действий управления. </summary>
    public bool IsVisible => this._overview != null && (this.CanImpersonate || this.CanManageTests || this.CanManageBilling);

    /// <summary> Признак доступности входа от имени пользователя. </summary>
    public bool CanImpersonate => this._isActive && this._overview is { Profile.IsDeleted: false } overview
        && !string.IsNullOrWhiteSpace(this._userId)
        && this._sessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Users.Impersonate", StringComparer.Ordinal))
        && !string.Equals(overview.Profile.Id, profile.Id, StringComparison.Ordinal);

    /// <summary> Признак доступности управления тестовыми подписками. </summary>
    public bool CanManageTests => this._isActive && this._overview is { Profile.IsDeleted: false }
        && this.HasPermission("Admin.Users.Manage");

    /// <summary> Признак доступности действий над аккаунтом. </summary>
    public bool CanManageAccount => this.CanImpersonate || this.CanManageTests;

    /// <summary> Признак доступности управления тарифами и подписками. </summary>
    public bool CanManageBilling => this._isActive && this._canViewBilling
        && this._overview is { Profile.IsDeleted: false }
        && this.HasPermission("Admin.Billing.Manage");

    /// <summary> Подпись текущего доступа к тестовым подпискам. </summary>
    public string TestSubscriptionsLabel => AdminLocalization.Instance[this._overview?.Profile.CanUseTestSubscriptions == true
        ? "userManagement.enabled"
        : "userManagement.disabled"];

    /// <summary> Признак наличия организаций для управления подписками. </summary>
    public bool HasTargets => this.Targets.Count > 0;

    /// <summary> Сводка выбранной цели и её активного тарифа. </summary>
    public string CurrentTargetSummary => this.SelectedTarget == null
        ? AdminLocalization.Instance["userManagement.chooseTarget"]
        : string.IsNullOrWhiteSpace(this.SelectedTarget.CurrentPlanLabel)
            ? this.SelectedTarget.DisplayName
            : $"{this.SelectedTarget.DisplayName} · {this.SelectedTarget.CurrentPlanLabel}";

    /// <summary> Подпись выдачи тарифа с учётом действующей модели организации. </summary>
    public string GrantPlanButtonLabel => this.SelectedTarget?.UsesFreeAfterSubscription == true
        ? AdminLocalization.Instance["userManagement.grantPro"]
        : AdminLocalization.Instance["userManagement.grantPlan"];

    /// <summary> Тарифы, подходящие для выбранной организации. </summary>
    public IReadOnlyList<AdminAvailablePlan> TargetPlans => this.Plans;

    /// <summary> Признак возможности выдать демо. </summary>
    public bool CanGrantDemo => this.CanBeginAction("grant-demo");

    /// <summary> Признак возможности отозвать демо. </summary>
    public bool CanRevokeDemo => this.CanBeginAction("revoke-demo");

    /// <summary> Признак возможности выдать тариф. </summary>
    public bool CanGrantPlan => this.CanBeginAction("grant-plan");

    /// <summary> Признак возможности продлить подписку. </summary>
    public bool CanExtend => this.CanBeginAction("extend");

    /// <summary> Признак возможности остановить подписки. </summary>
    public bool CanStop => this.CanBeginAction("stop");

    /// <summary> Дата окончания, рассчитанная для выдачи или продления тарифа. </summary>
    public DateTime? PreviewEndDate
    {
        get
        {
            if (this.Days is < 1 or > 366)
            {
                return null;
            }

            if (this._pendingAction == "extend")
            {
                var currentEndDate = this.SelectedTarget?.EndDate;
                if (currentEndDate == null)
                {
                    return null;
                }

                var startDate = currentEndDate.Value > DateTime.UtcNow ? currentEndDate.Value : DateTime.UtcNow;
                return startDate.AddDays(this.Days);
            }

            if (this._pendingAction != "grant-plan" || this.SelectedPlan == null
                || !this.AvailablePlansForTarget().Any(plan => plan.Id == this.SelectedPlan.Id))
            {
                return null;
            }

            return DateTime.UtcNow.AddDays(this.Days);
        }
    }

    /// <summary> Текстовое представление рассчитанной даты окончания тарифа. </summary>
    public string PreviewEndDateLabel => this.PreviewEndDate is { } endDate
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, AdminLocalization.Instance["userManagement.newEndDate"],
            endDate.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture))
        : AdminLocalization.Instance["userManagement.planAndDaysRequired"];

    /// <summary> Устанавливает обзор и сохраняет выбранную цель для той же карточки. </summary>
    public void SetOverview(AdminUserOverview? overview, bool canViewBilling)
    {
        this._overviewRevision++;
        var userId = overview?.Profile.Id;
        var isNewUser = !string.Equals(this._userId, userId, StringComparison.Ordinal);
        if (isNewUser)
        {
            this.Stop(true);
            this._version++;
            this._userId = userId;
        }

        var selectedScope = this.SelectedTarget?.Scope;
        var selectedTargetId = this.SelectedTarget?.TargetId;
        if (!isNewUser && this.IsBusy && this._pendingAction != null)
        {
            this._version++;
            this.BeginActionCommand.Cancel();
            this.IsBusy = false;
        }

        this._overview = overview;
        this._canViewBilling = canViewBilling;
        this._isActive = overview != null;
        this.InvalidateConfirmation();
        this._refreshRequired = false;
        this.Targets = overview == null ? [] : this.CreateTargets(overview, canViewBilling);
        this.SelectedTarget = this.Targets.FirstOrDefault(target => target.Scope == selectedScope
                && target.TargetId == selectedTargetId)
            ?? this.Targets.FirstOrDefault();
        this.RefreshAccess();
    }

    /// <summary> Обновляет доступ текущей сессии и закрывает подтверждение при смене прав. </summary>
    public void RefreshAccess()
    {
        var canImpersonate = this.CanImpersonate;
        var canManageTests = this.CanManageTests;
        var canManageBilling = this.CanManageBilling;
        var accessChanged = canImpersonate != this._lastCanImpersonate
            || canManageTests != this._lastCanManageTests
            || canManageBilling != this._lastCanManageBilling;
        this._lastCanImpersonate = canImpersonate;
        this._lastCanManageTests = canManageTests;
        this._lastCanManageBilling = canManageBilling;
        if (accessChanged && this.IsBusy)
        {
            this._version++;
            this.BeginActionCommand.Cancel();
            this.ConfirmCommand.Cancel();
            this.IsBusy = false;
        }

        if (this._pendingAction != null && !this.CanPerformAction(this._pendingAction))
        {
            this.InvalidateConfirmation();
        }

        this.NotifyState();
    }

    /// <summary> Останавливает действия карточки и при необходимости очищает данные. </summary>
    public void RefreshLocalizedValues()
    {
        var selectedScope = this.SelectedTarget?.Scope;
        var selectedTargetId = this.SelectedTarget?.TargetId;
        if (this._overview != null)
        {
            this._isRefreshingLocalizedValues = true;
            this.Targets = this.CreateTargets(this._overview, this._canViewBilling);
            this.SelectedTarget = this.Targets.FirstOrDefault(target => target.Scope == selectedScope
                && target.TargetId == selectedTargetId);
            this._isRefreshingLocalizedValues = false;
        }

        this.OnPropertyChanged(nameof(this.TestSubscriptionsLabel));
        this.OnPropertyChanged(nameof(this.CurrentTargetSummary));
        this.OnPropertyChanged(nameof(this.GrantPlanButtonLabel));
        this.OnPropertyChanged(nameof(this.PreviewEndDateLabel));
        this.UpdateErrorMessage();
        if (this._successTranslationKey != null)
        {
            this.SuccessMessage = AdminLocalization.Instance[this._successTranslationKey];
        }
        this.UpdateConfirmationText();
        this.OnPropertyChanged(nameof(this.TargetPlans));
    }

    private void UpdateErrorMessage()
    {
        if (this._errorSuffixKey == null)
        {
            return;
        }

        var details = this._errorDetailsTranslationKey == null
            ? this._errorDetails
            : AdminLocalization.Instance[this._errorDetailsTranslationKey];

        this.ErrorMessage = this._errorSuffixKey == "userManagement.error.browser"
            ? AdminLocalization.Instance.Format(this._errorSuffixKey, details ?? string.Empty)
            : string.IsNullOrEmpty(details)
                ? AdminLocalization.Instance[this._errorSuffixKey]
                : $"{details} {AdminLocalization.Instance[this._errorSuffixKey]}";
    }

    private void SetErrorKey(string key, string? details = null)
    {
        this._errorDetails = details;
        this._errorDetailsTranslationKey = null;
        this._errorSuffixKey = key;
        this.UpdateErrorMessage();
    }

    private void AppendErrorTranslation(string key)
    {
        var detailsKey = this._errorDetailsTranslationKey ?? (this._errorDetails == null ? this._errorSuffixKey : null);
        this.SetErrorKey(key, this._errorDetails ?? this.ErrorMessage);
        this._errorDetailsTranslationKey = detailsKey;
        this.UpdateErrorMessage();
    }

    private void SetSuccessKey(string key)
    {
        this._successTranslationKey = key;
        this.SuccessMessage = AdminLocalization.Instance[key];
    }

    partial void OnErrorMessageChanging(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            this._errorDetails = null;
            this._errorDetailsTranslationKey = null;
            this._errorSuffixKey = null;
        }
    }

    partial void OnSuccessMessageChanging(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            this._successTranslationKey = null;
        }
    }

    public void Stop(bool clear = true)
    {
        this._version++;
        this._isActive = !clear;
        this.BeginActionCommand.Cancel();
        this.ConfirmCommand.Cancel();
        this.InvalidateConfirmation();
        this.IsBusy = false;
        if (clear)
        {
            this._overview = null;
            this._userId = null;
            this._canViewBilling = false;
            this._refreshRequired = false;
            this._lastCanImpersonate = false;
            this._lastCanManageTests = false;
            this._lastCanManageBilling = false;
            this.Targets = [];
            this.SelectedTarget = null;
            this._availablePlans = [];
            this.Plans = [];
            this.SelectedPlan = null;
            this.Reason = string.Empty;
            this.Days = 30;
            this.ErrorMessage = string.Empty;
            this.SuccessMessage = string.Empty;
            this.ConfirmationTitle = string.Empty;
            this.ConfirmationText = string.Empty;
        }

        this.NotifyState();
    }

    /// <summary> Останавливает и дожидается отменённых команд управления. </summary>
    public async Task DeactivateAsync(bool clear = true)
    {
        this.Stop(clear);
        await Task.WhenAll(new[] { this.BeginActionCommand.ExecutionTask, this.ConfirmCommand.ExecutionTask }
            .OfType<Task>());
    }

    /// <summary> Формирует запрос подтверждения выбранного действия. </summary>
    [RelayCommand(CanExecute = nameof(CanBeginAction))]
    private async Task BeginActionAsync(string action, CancellationToken cancellationToken)
    {
        if (!this.CanBeginAction(action))
        {
            return;
        }

        this.ErrorMessage = string.Empty;
        this.SuccessMessage = string.Empty;
        this._pendingAction = action;
        if (action == "grant-plan")
        {
            var version = this._version;
            var userId = this._userId;
            this.IsBusy = true;
            try
            {
                var plans = await this._sessionClient.GetAvailablePlansAsync(cancellationToken);
                if (!this.IsCurrent(userId, version, cancellationToken) || !this.CanManageBilling)
                {
                    return;
                }

                this._availablePlans = plans;
                this.Plans = this.AvailablePlansForTarget();
                this.SelectedPlan = this.AvailablePlansForTarget().FirstOrDefault();
                if (this.SelectedPlan == null)
                {
                    this.SetErrorKey("userManagement.error.noPlans");
                    this._pendingAction = null;
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                if (this.IsCurrent(userId, version, cancellationToken))
                {
                    this.SetErrorKey("userManagement.error.planTimeout");
                }

                return;
            }
            catch (ApiException exception)
            {
                if (this.IsCurrent(userId, version, cancellationToken))
                {
                    await this.HandleApiErrorAsync(exception, cancellationToken, false);
                }

                return;
            }
            catch (HttpRequestException)
            {
                if (this.IsCurrent(userId, version, cancellationToken))
                {
                    this.SetErrorKey("userManagement.error.planLoad");
                }

                return;
            }
            finally
            {
                if (version == this._version)
                {
                    this.IsBusy = false;
                }
            }
        }

        this.OpenConfirmation(action);
    }

    /// <summary> Закрывает подтверждение без выполнения действия. </summary>
    [RelayCommand]
    private void Cancel()
    {
        this.InvalidateConfirmation();
    }

    /// <summary> Выполняет подтверждённое действие с неизменяемым снимком введённых данных. </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        if (!this.CanConfirm())
        {
            return;
        }

        var operation = this.CreateOperationSnapshot();
        var version = this._version;
        this.IsConfirmationOpen = false;
        this.IsBusy = true;
        this.ErrorMessage = string.Empty;
        this.SuccessMessage = string.Empty;
        this._pendingAction = null;
        try
        {
            if (operation.Action == "impersonate")
            {
                if (this.ImpersonationRequested == null)
                {
                    this.SetErrorKey("userManagement.error.impersonationUnavailable");
                    return;
                }

                await this.ImpersonationRequested.Invoke(operation.UserId, cancellationToken);
                return;
            }

            this._refreshRequired = true;
            var result = await this.ExecuteMutationAsync(operation, cancellationToken);
            if (!this.IsCurrent(operation.UserId, version, cancellationToken))
            {
                return;
            }

            this._refreshRequired = true;
            this.SuccessMessage = result.Message;
            if (this.UserChanged == null)
            {
                this.SetSuccessKey("userManagement.success.refreshRequired");
                return;
            }

            var overviewRevision = this._overviewRevision;
            try
            {
                await this.UserChanged.Invoke(cancellationToken);
                if (this.IsCurrent(operation.UserId, version, cancellationToken))
                {
                    if (this._overviewRevision > overviewRevision)
                    {
                        this._refreshRequired = false;
                    }
                    else
                    {
                        this.SetSuccessKey("userManagement.success.refreshMissing");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                if (this.IsCurrent(operation.UserId, version, cancellationToken))
                {
                    this.SetSuccessKey("userManagement.success.refreshFailed");
                }
            }
            catch (ApiException)
            {
                if (this.IsCurrent(operation.UserId, version, cancellationToken))
                {
                    this.SetSuccessKey("userManagement.success.refreshFailed");
                }
            }
            catch (HttpRequestException)
            {
                if (this.IsCurrent(operation.UserId, version, cancellationToken))
                {
                    this.SetSuccessKey("userManagement.success.refreshFailed");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Смена карточки или выход отменяет выполнение и обновление её данных.
        }
        catch (ApiException exception)
        {
            if (this.IsCurrent(operation.UserId, version, cancellationToken))
            {
                await this.HandleApiErrorAsync(exception, cancellationToken, true);
            }
        }
        catch (HttpRequestException)
        {
            if (this.IsCurrent(operation.UserId, version, cancellationToken))
            {
                this.SetErrorKey("userManagement.error.connectionRefresh");
                await this.RefreshAfterUncertainFailureAsync(operation.UserId, version, cancellationToken);
            }
        }
        catch (Exception exception) when (operation.Action == "impersonate"
            && exception is (TimeoutException or Win32Exception or IOException or SocketException or InvalidOperationException))
        {
            if (this.IsCurrent(operation.UserId, version, cancellationToken))
            {
                this.SetErrorKey("userManagement.error.browser", exception.Message);
            }
        }
        catch (OperationCanceledException)
        {
            if (this.IsCurrent(operation.UserId, version, cancellationToken))
            {
                this.SetErrorKey("userManagement.error.timeoutRefresh");
                await this.RefreshAfterUncertainFailureAsync(operation.UserId, version, cancellationToken);
            }
        }
        finally
        {
            if (version == this._version)
            {
                this.IsBusy = false;
                this.NotifyState();
            }
        }
    }

    private bool CanBeginAction(string? action)
    {
        if (!this._isActive || this._overview == null || this.IsBusy || this.IsConfirmationOpen
            || this._refreshRequired || action == null)
        {
            return false;
        }

        return this.CanPerformAction(action);
    }

    private bool CanPerformAction(string action)
    {
        return action switch
        {
            "test-subscriptions" => this.CanManageTests,
            "impersonate" => this.CanImpersonate,
            "grant-demo" => this.CanManageBilling && this.SelectedTarget is { IsAvailable: true, HasActiveSubscription: false, CanGrantDemo: true },
            "revoke-demo" => this.CanManageBilling && this.SelectedTarget is { IsAvailable: true, HasActiveSubscription: true, IsDemo: true },
            "grant-plan" => this.CanManageBilling && this.SelectedTarget is { IsAvailable: true, HasActiveSubscription: false },
            "extend" => this.CanManageBilling && this.SelectedTarget is { IsAvailable: true, CanExtendSubscription: true, SubscriptionId: not null, EndDate: not null },
            "stop" => this.CanManageBilling && this.SelectedTarget is { IsAvailable: true, HasActiveSubscription: true, SubscriptionId: not null, EndDate: not null },
            _ => false
        };
    }

    private bool CanConfirm()
    {
        if (!this.IsConfirmationOpen || this.IsBusy || this._pendingAction == null
            || !this._isActive || this._overview == null || this._refreshRequired
            || !this.CanPerformAction(this._pendingAction))
        {
            return false;
        }

        if (this._pendingAction is "test-subscriptions" or "impersonate")
        {
            return true;
        }

        if (this.Reason.Trim().Length is < 1 or > 500)
        {
            return false;
        }

        if ((this._pendingAction is "grant-plan" or "extend") && this.Days is < 1 or > 366)
        {
            return false;
        }

        return this._pendingAction != "grant-plan" || this.SelectedPlan != null
            && this.AvailablePlansForTarget().Any(plan => plan.Id == this.SelectedPlan.Id);
    }

    private void OpenConfirmation(string action)
    {
        this.NeedsReason = action is not ("test-subscriptions" or "impersonate");
        this.NeedsPlan = action == "grant-plan";
        this.NeedsDays = action is "grant-plan" or "extend";
        this.Reason = string.Empty;
        this.Days = 30;
        this.UpdateConfirmationText();
        this.IsConfirmationOpen = true;
        this.NotifyState();
    }

    private void UpdateConfirmationText()
    {
        if (this._pendingAction == null || this._overview == null)
        {
            return;
        }

        var target = this.SelectedTarget;
        var targetName = target?.DisplayName ?? string.Empty;
        var userName = this._overview.Profile.Email ?? this._overview.Profile.Phone ?? this._overview.Profile.Id;
        var isFreeTarget = target?.UsesFreeAfterSubscription == true;
        var keySuffix = this._pendingAction switch
        {
            "test-subscriptions" => this._overview.Profile.CanUseTestSubscriptions ? "tests.disable" : "tests.enable",
            "impersonate" => "impersonate",
            "grant-demo" => "grantDemo",
            "revoke-demo" => isFreeTarget ? "revokeDemoFree" : "revokeDemo",
            "grant-plan" => isFreeTarget ? "grantPro" : "grantPlan",
            "extend" => "extend",
            "stop" => isFreeTarget ? "stopFree" : "stop",
            _ => string.Empty
        };

        this.ConfirmationTitle = AdminLocalization.Instance[$"userManagement.confirm.{keySuffix}.title"];
        var template = AdminLocalization.Instance[$"userManagement.confirm.{keySuffix}.text"];
        this.ConfirmationText = this._pendingAction switch
        {
            "test-subscriptions" => string.Format(System.Globalization.CultureInfo.CurrentCulture, template, userName),
            "impersonate" => string.Format(System.Globalization.CultureInfo.CurrentCulture, template, userName),
            "grant-demo" or "revoke-demo" or "grant-plan" or "stop" => string.Format(System.Globalization.CultureInfo.CurrentCulture, template, targetName),
            "extend" => string.Format(System.Globalization.CultureInfo.CurrentCulture, template, targetName, target?.SubscriptionId),
            _ => string.Empty
        };
    }

    private PendingOperation CreateOperationSnapshot()
    {
        var action = this._pendingAction!;
        return new PendingOperation(action, this._userId!, this.SelectedTarget, this.SelectedPlan?.Id,
            this.Days, this.Reason.Trim(), this._overview!.Profile.CanUseTestSubscriptions);
    }

    private async Task<AdminOperationResponse> ExecuteMutationAsync(PendingOperation operation, CancellationToken cancellationToken)
    {
        if (operation.Action == "test-subscriptions")
        {
            return await this._sessionClient.UpdateTestSubscriptionsAsync(
                operation.UserId, !operation.CanUseTestSubscriptions, cancellationToken);
        }

        var target = operation.Target ?? throw new InvalidOperationException("Организация не выбрана.");

        return operation.Action switch
        {
            "grant-demo" when target.Scope == "users" => await this._sessionClient.GrantUserDemoAsync(
                operation.UserId, operation.Reason, cancellationToken),
            "grant-demo" => await this._sessionClient.GrantOrganizationDemoAsync(
                operation.UserId, target.TargetId, operation.Reason, cancellationToken),
            "revoke-demo" when target.Scope == "users" => await this._sessionClient.RevokeUserDemoAsync(
                operation.UserId, operation.Reason, cancellationToken),
            "revoke-demo" => await this._sessionClient.RevokeOrganizationDemoAsync(
                operation.UserId, target.TargetId, operation.Reason, cancellationToken),
            "grant-plan" => await this._sessionClient.GrantPlanAsync(
                target.Scope, target.TargetId, operation.PlanId!.Value, operation.Days, operation.Reason, cancellationToken),
            "extend" => await this._sessionClient.ExtendSubscriptionAsync(
                target.Scope, target.SubscriptionId!.Value, operation.Days, operation.Reason, operation.ExpectedEndDate, cancellationToken),
            "stop" => await this._sessionClient.StopSubscriptionAsync(
                target.Scope, target.SubscriptionId!.Value, operation.Reason, operation.ExpectedEndDate, cancellationToken),
            _ => throw new InvalidOperationException("Неизвестное действие управления.")
        };
    }

    private async Task HandleApiErrorAsync(ApiException exception, CancellationToken cancellationToken, bool refreshOnConflict)
    {
        if (AdminDisplayLabels.TransportErrorKey(exception) is { } transportKey)
        {
            this.SetErrorKey(transportKey);
        }
        else
        {
            this._errorSuffixKey = null;
            this._errorDetails = null;
            this._errorDetailsTranslationKey = null;
            this.ErrorMessage = exception.Message;
        }

        if (exception.StatusCode == HttpStatusCode.Unauthorized || !this._sessionClient.HasSession)
        {
            this.SessionExpired?.Invoke();
            return;
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            if (this.AccessDenied != null)
            {
                await this.AccessDenied.Invoke();
            }

            this.RefreshAccess();
            return;
        }

        if (refreshOnConflict && exception.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            var overviewRevision = this._overviewRevision;
            if (this.UserChanged == null)
            {
                this.AppendErrorTranslation("userManagement.errorSuffix.refreshBeforeActions");
                return;
            }

            try
            {
                await this.UserChanged.Invoke(cancellationToken);
                if (this._overviewRevision > overviewRevision)
                {
                    this._refreshRequired = false;
                    this.AppendErrorTranslation(exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict
                        ? "userManagement.errorSuffix.refreshTarget"
                        : "userManagement.errorSuffix.checkTarget");
                }
                else
                {
                    this.AppendErrorTranslation("userManagement.errorSuffix.cardNotUpdated");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                this.AppendErrorTranslation("userManagement.errorSuffix.retryAfterRefresh");
            }
            catch (ApiException)
            {
                this.AppendErrorTranslation("userManagement.errorSuffix.retryAfterRefresh");
            }
            catch (HttpRequestException)
            {
                this.AppendErrorTranslation("userManagement.errorSuffix.retryAfterRefresh");
            }
        }
    }

    private async Task RefreshAfterUncertainFailureAsync(
        string userId,
        int version,
        CancellationToken cancellationToken)
    {
        if (this.UserChanged == null)
        {
            return;
        }

        var overviewRevision = this._overviewRevision;
        try
        {
            await this.UserChanged.Invoke(cancellationToken);
            if (this.IsCurrent(userId, version, cancellationToken) && this._overviewRevision > overviewRevision)
            {
                this._refreshRequired = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiException)
        {
        }
        catch (HttpRequestException)
        {
        }
    }

    private IReadOnlyList<UserManagementTarget> CreateTargets(AdminUserOverview overview, bool canViewBilling)
    {
        return overview.Organizations.Select(organization =>
                UserManagementTarget.Organization(overview, organization, canViewBilling))
            .Append(UserManagementTarget.User(overview, canViewBilling))
            .ToArray();
    }

    private IReadOnlyList<AdminAvailablePlan> AvailablePlansForTarget()
    {
        var expectedPlanType = this.SelectedTarget?.Scope == "users" ? "personal" : "business";

        return this._availablePlans.Where(plan => string.Equals(plan.PlanTypeCode, expectedPlanType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private bool HasPermission(string permission) => this._sessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains(permission, StringComparer.Ordinal));

    private bool IsCurrent(string? userId, int version, CancellationToken cancellationToken) =>
        this._isActive && version == this._version && !cancellationToken.IsCancellationRequested
        && string.Equals(this._userId, userId, StringComparison.Ordinal);

    private void InvalidateConfirmation()
    {
        this.IsConfirmationOpen = false;
        this._pendingAction = null;
        this.NeedsReason = false;
        this.NeedsPlan = false;
        this.NeedsDays = false;
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    private void NotifyState()
    {
        this.OnPropertyChanged(nameof(IsVisible));
        this.OnPropertyChanged(nameof(CanImpersonate));
        this.OnPropertyChanged(nameof(CanManageTests));
        this.OnPropertyChanged(nameof(CanManageAccount));
        this.OnPropertyChanged(nameof(CanManageBilling));
        this.OnPropertyChanged(nameof(TestSubscriptionsLabel));
        this.OnPropertyChanged(nameof(CurrentTargetSummary));
        this.OnPropertyChanged(nameof(GrantPlanButtonLabel));
        this.OnPropertyChanged(nameof(TargetPlans));
        this.OnPropertyChanged(nameof(CanGrantDemo));
        this.OnPropertyChanged(nameof(CanRevokeDemo));
        this.OnPropertyChanged(nameof(CanGrantPlan));
        this.OnPropertyChanged(nameof(CanExtend));
        this.OnPropertyChanged(nameof(CanStop));
        this.OnPropertyChanged(nameof(PreviewEndDate));
        this.OnPropertyChanged(nameof(PreviewEndDateLabel));
        this.BeginActionCommand.NotifyCanExecuteChanged();
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }

    private sealed record PendingOperation(
        string Action,
        string UserId,
        UserManagementTarget? Target,
        int? PlanId,
        int Days,
        string Reason,
        bool CanUseTestSubscriptions)
    {
        public DateTime? ExpectedEndDate => this.Target?.EndDate;
    }

    partial void OnSelectedTargetChanged(UserManagementTarget? value)
    {
        if (this._isRefreshingLocalizedValues)
        {
            this.OnPropertyChanged(nameof(this.CurrentTargetSummary));
            this.UpdateConfirmationText();
            return;
        }

        if (this._pendingAction != null)
        {
            this.InvalidateConfirmation();
            if (this.IsBusy)
            {
                this._version++;
                this.BeginActionCommand.Cancel();
                this.IsBusy = false;
            }
        }

        this.Plans = this.AvailablePlansForTarget();
        this.SelectedPlan = this.AvailablePlansForTarget().FirstOrDefault();
        this.NotifyState();
    }

    partial void OnSelectedPlanChanged(AdminAvailablePlan? value)
    {
        this.OnPropertyChanged(nameof(PreviewEndDate));
        this.OnPropertyChanged(nameof(PreviewEndDateLabel));
    }

    partial void OnDaysChanged(int value)
    {
        this.OnPropertyChanged(nameof(PreviewEndDate));
        this.OnPropertyChanged(nameof(PreviewEndDateLabel));
    }

    partial void OnReasonChanged(string value) => this.ConfirmCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => this.NotifyState();

    partial void OnIsConfirmationOpenChanged(bool value)
    {
        this.BeginActionCommand.NotifyCanExecuteChanged();
        this.ConfirmCommand.NotifyCanExecuteChanged();
    }
}
