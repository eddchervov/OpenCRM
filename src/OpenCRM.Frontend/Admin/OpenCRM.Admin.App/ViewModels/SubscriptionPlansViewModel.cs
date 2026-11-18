using System.Net;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель управления тарифами и их функциями. </summary>
public partial class SubscriptionPlansViewModel(AdminSessionClient sessionClient) : ObservableObject
{
    private readonly AdminSessionClient _sessionClient = sessionClient;
    private bool _isActive;
    private bool _requiresRefresh;
    private int _requestVersion;
    private int _featureVersion;
    private string? _errorTranslationKey;
    private bool _isSettingLocalizedError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveFeaturesCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    [NotifyPropertyChangedFor(nameof(BusinessRows))]
    [NotifyPropertyChangedFor(nameof(PersonalRows))]
    [NotifyPropertyChangedFor(nameof(OtherRows))]
    [NotifyPropertyChangedFor(nameof(CanDeletePlan))]
    [NotifyPropertyChangedFor(nameof(CanSaveFeatures))]
    [NotifyPropertyChangedFor(nameof(IsFeatureEditorVisible))]
    [NotifyPropertyChangedFor(nameof(CanEditActiveFlag))]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveFeaturesCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    [NotifyPropertyChangedFor(nameof(BusinessRows))]
    [NotifyPropertyChangedFor(nameof(PersonalRows))]
    [NotifyPropertyChangedFor(nameof(OtherRows))]
    [NotifyPropertyChangedFor(nameof(CanDeletePlan))]
    [NotifyPropertyChangedFor(nameof(CanSaveFeatures))]
    [NotifyPropertyChangedFor(nameof(IsFeatureEditorVisible))]
    [NotifyPropertyChangedFor(nameof(CanEditActiveFlag))]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BusinessPlans))]
    [NotifyPropertyChangedFor(nameof(BusinessRows))]
    [NotifyPropertyChangedFor(nameof(PersonalPlans))]
    [NotifyPropertyChangedFor(nameof(PersonalRows))]
    [NotifyPropertyChangedFor(nameof(OtherPlans))]
    [NotifyPropertyChangedFor(nameof(OtherRows))]
    [NotifyPropertyChangedFor(nameof(HasPlans))]
    [NotifyPropertyChangedFor(nameof(HasBusinessPlans))]
    [NotifyPropertyChangedFor(nameof(HasPersonalPlans))]
    [NotifyPropertyChangedFor(nameof(BusinessPlanCountLabel))]
    [NotifyPropertyChangedFor(nameof(PersonalPlanCountLabel))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    private IReadOnlyList<AdminSubscriptionPlan> _plans = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(CanDeletePlan))]
    [NotifyPropertyChangedFor(nameof(CanSaveFeatures))]
    [NotifyPropertyChangedFor(nameof(IsFeatureEditorVisible))]
    [NotifyPropertyChangedFor(nameof(CanEditDefaultFlag))]
    [NotifyPropertyChangedFor(nameof(CanEditActiveFlag))]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsBusinessPlanSelected))]
    [NotifyCanExecuteChangedFor(nameof(SavePlanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveFeaturesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    private AdminSubscriptionPlan? _selectedPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(CanDeletePlan))]
    [NotifyPropertyChangedFor(nameof(CanSaveFeatures))]
    [NotifyPropertyChangedFor(nameof(IsFeatureEditorVisible))]
    [NotifyPropertyChangedFor(nameof(CanEditDefaultFlag))]
    [NotifyPropertyChangedFor(nameof(CanEditActiveFlag))]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    [NotifyPropertyChangedFor(nameof(IsBusinessPlanSelected))]
    [NotifyCanExecuteChangedFor(nameof(SavePlanCommand))]
    private bool _isCreating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeatures))]
    [NotifyPropertyChangedFor(nameof(CanSaveFeatures))]
    private IReadOnlyList<PlanFeatureEditor> _features = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    private bool _isDeleteConfirmationOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePlanCommand))]
    private SubscriptionPlanEditor _editor = new();

    public event Action? SessionExpired;

    public event Func<Task>? AccessDenied;

    public IReadOnlyList<AdminSubscriptionPlan> BusinessPlans => this.Plans
        .Where(plan => string.Equals(plan.PlanTypeCode, "business", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(plan => plan.Group == "workspace.free.v1")
        .ThenBy(plan => plan.Order)
        .ThenBy(plan => plan.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public IReadOnlyList<AdminSubscriptionPlan> PersonalPlans => this.Plans
        .Where(plan => string.Equals(plan.PlanTypeCode, "personal", StringComparison.OrdinalIgnoreCase))
        .OrderBy(plan => plan.Order)
        .ThenBy(plan => plan.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public IReadOnlyList<AdminSubscriptionPlan> OtherPlans => this.Plans
        .Where(plan => !string.Equals(plan.PlanTypeCode, "business", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(plan.PlanTypeCode, "personal", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    public IReadOnlyList<SubscriptionPlanListItem> BusinessRows => this.CreateRows(this.BusinessPlans);

    public IReadOnlyList<SubscriptionPlanListItem> PersonalRows => this.CreateRows(this.PersonalPlans);

    public IReadOnlyList<SubscriptionPlanListItem> OtherRows => this.CreateRows(this.OtherPlans);

    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    public bool HasMessage => !string.IsNullOrWhiteSpace(this.Message);

    public bool HasPlans => this.Plans.Count > 0;

    public bool HasBusinessPlans => this.BusinessPlans.Count > 0;

    public bool HasPersonalPlans => this.PersonalPlans.Count > 0;

    public string BusinessPlanCountLabel => SubscriptionPlansViewModel.FormatPlanCount(this.BusinessPlans.Count);

    public string PersonalPlanCountLabel => SubscriptionPlansViewModel.FormatPlanCount(this.PersonalPlans.Count);

    public bool IsEmpty => !this.IsLoading && !this.HasPlans && !this.IsEditorOpen && !this.HasError;

    public bool HasWorkspace => this.HasPlans || this.IsEditorOpen;

    public bool IsEditorOpen => this.IsCreating || this.SelectedPlan != null;

    public bool IsReadOnly => !this.IsCreating && this.SelectedPlan is { PlanTypeCode: not "business" };

    public bool HasFeatures => this.Features.Count > 0;

    public void RefreshLocalizedValues()
    {
        this.RefreshLocalizedError();
        this.Editor.RefreshLocalizedValues();
        foreach (var row in this.BusinessRows.Concat(this.PersonalRows).Concat(this.OtherRows))
        {
            row.RefreshLocalizedValues();
        }

        this.OnPropertyChanged((string?)null);
    }

    public bool IsBusinessPlanSelected => this.IsCreating || this.SelectedPlan is { PlanTypeCode: "business" };

    public bool CanSaveFeatures => this._isActive && !this.IsLoading && !this.IsSaving && !this._requiresRefresh
        && this.SelectedPlan is { PlanTypeCode: "business" } && this.Features.Count > 0;

    public bool IsFeatureEditorVisible => this.SelectedPlan is { PlanTypeCode: "business" };

    public bool CanDeletePlan => this._isActive && !this.IsLoading && !this.IsSaving && !this._requiresRefresh
        && !this.IsReadOnly && this.SelectedPlan is { CanDelete: true, Group: not "workspace.free.v1" };

    public bool CanEditDefaultFlag => !this.Editor.IsSystemFree && (this.IsCreating || this.SelectedPlan is { IsDefaultPlan: false });

    public bool CanEditActiveFlag => !this.Editor.IsSystemFree && !this.Editor.IsDefaultPlanLocked && !this.Editor.IsDemoPlan;

    public bool CanEditDemoFlag => !this.Editor.IsSystemFree && !this.Editor.IsDefaultPlanLocked;

    /// <summary> Активирует экран и загружает тарифы. </summary>
    public async Task ActivateAsync()
    {
        this._isActive = true;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Отменяет выполняемые запросы и при необходимости очищает экран. </summary>
    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this._requestVersion++;
        this._featureVersion++;
        this.RefreshCommand.Cancel();
        this.SavePlanCommand.Cancel();
        this.SaveFeaturesCommand.Cancel();
        this.TogglePlanCommand.Cancel();
        this.ConfirmDeleteCommand.Cancel();
        this.OpenPlanCommand.Cancel();
        this.IsDeleteConfirmationOpen = false;
        if (!clear)
        {
            return;
        }

        this.Plans = [];
        this.SelectedPlan = null;
        this.Features = [];
        this.IsCreating = false;
        this.ClearMessages();
    }

    /// <summary> Деактивирует экран и дожидается отмены запросов. </summary>
    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        var tasks = new[]
        {
            this.RefreshCommand.ExecutionTask,
            this.SavePlanCommand.ExecutionTask,
            this.SaveFeaturesCommand.ExecutionTask,
            this.TogglePlanCommand.ExecutionTask,
            this.ConfirmDeleteCommand.ExecutionTask,
            this.OpenPlanCommand.ExecutionTask
        }.OfType<Task>().ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Запросы отменены при деактивации экрана.
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync(CancellationToken cancellationToken) => this.LoadPlansAsync(null, cancellationToken);

    private async Task LoadPlansAsync(int? selectedId, CancellationToken cancellationToken)
    {
        var requestVersion = ++this._requestVersion;
        selectedId ??= this.SelectedPlan?.Id;
        this.IsLoading = true;
        this.ClearMessages();
        try
        {
            var plans = await this._sessionClient.GetSubscriptionPlansAsync(cancellationToken);
            if (!this.IsCurrent(requestVersion, cancellationToken))
            {
                return;
            }

            this.Plans = plans;
            this._requiresRefresh = false;
            if (selectedId is int id)
            {
                var selected = plans.FirstOrDefault(plan => plan.Id == id);
                if (selected != null)
                {
                    this.SelectedPlan = selected;
                    this.IsCreating = false;
                    this.Editor = new SubscriptionPlanEditor();
                    this.Editor.Load(selected);
                    await this.LoadFeaturesAsync(selected, cancellationToken);
                    return;
                }
            }

            this.SelectedPlan = null;
            this.Features = [];
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && this.IsCurrent(requestVersion, cancellationToken))
            {
                this.SetLocalizedError("native.plans.zagruzka.tarifov.zanyala.slishkom.mnogo.vremeni.povtorite.popytk");
            }
        }
        catch (ApiException exception)
        {
            await this.HandleErrorAsync(exception);
        }
        catch (HttpRequestException)
        {
            this.SetLocalizedError("native.plans.net.svyazi.s.serverom.ne.udalos.zagruzit.tarify");
        }
        finally
        {
            if (requestVersion == this._requestVersion)
            {
                this.IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private void CreatePlan()
    {
        if (!this.CanManage())
        {
            return;
        }

        this.SelectedPlan = null;
        this.Features = [];
        this.IsCreating = true;
        this.IsDeleteConfirmationOpen = false;
        this.Editor = new SubscriptionPlanEditor();
        this.ClearMessages();
        this.NotifyEditorCommands();
    }

    [RelayCommand]
    private async Task OpenPlanAsync(SubscriptionPlanListItem? item, CancellationToken cancellationToken)
    {
        if (!this._isActive || item == null || this.IsLoading || this.IsSaving)
        {
            return;
        }

        var plan = this.Plans.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (plan == null)
        {
            return;
        }

        this.SelectedPlan = plan;
        this.IsCreating = false;
        this.IsDeleteConfirmationOpen = false;
        this.Editor = new SubscriptionPlanEditor();
        this.Editor.Load(plan);
        this.Features = [];
        this.ClearMessages();
        this.NotifyEditorCommands();
        await this.LoadFeaturesAsync(plan, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanSavePlan))]
    private async Task SavePlanAsync(CancellationToken cancellationToken)
    {
        var planId = this.SelectedPlan?.Id;
        var isCreating = this.IsCreating;
        var request = this.Editor.ToRequest();
        this.IsSaving = true;
        this.ClearMessages();
        try
        {
            var saved = isCreating
                ? await this._sessionClient.CreateSubscriptionPlanAsync(request, cancellationToken)
                : await this._sessionClient.UpdateSubscriptionPlanAsync(planId!.Value, request, cancellationToken);
            this.SelectedPlan = saved;
            this.IsCreating = false;
            this.Editor = new SubscriptionPlanEditor();
            this.Editor.Load(saved);
            this.Message = isCreating ? AdminLocalization.Instance["native.plans.tarif.sozdan"] : AdminLocalization.Instance["native.plans.tarif.sokhranyon"];
            this._requiresRefresh = true;
            await this.RefreshKeepingMessageAsync(saved.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                this._requiresRefresh = true;
                this.SetLocalizedError("native.plans.sokhranenie.tarifa.zanyalo.slishkom.mnogo.vremeni.obnovite.spiso");
            }
        }
        catch (ApiException exception)
        {
            await this.HandleMutationErrorAsync(exception);
        }
        catch (HttpRequestException)
        {
            this._requiresRefresh = true;
            this.SetLocalizedError("native.plans.net.svyazi.s.serverom.sokhranenie.ne.podtverzhdeno.obnovite.spis");
        }
        finally
        {
            this.IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveFeatureValues))]
    private async Task SaveFeaturesAsync(CancellationToken cancellationToken)
    {
        var selected = this.SelectedPlan;
        if (selected == null)
        {
            return;
        }

        var features = this.Features.Select(feature => feature.ToRequest()).ToArray();
        this.IsSaving = true;
        this.ClearMessages();
        try
        {
            var result = await this._sessionClient.SavePlanFeaturesAsync(selected.Id, features, cancellationToken);
            this.Message = string.IsNullOrWhiteSpace(result.Message)
                ? AdminLocalization.Instance["native.plans.vozmozhnosti.tarifa.sokhraneny.dlya.vsekh.srokov.oplaty"]
                : result.Message;
            this._requiresRefresh = true;
            await this.RefreshKeepingMessageAsync(selected.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                this._requiresRefresh = true;
                this.SetLocalizedError("native.plans.sokhranenie.vozmozhnostey.zanyalo.slishkom.mnogo.vremeni.obnovit");
            }
        }
        catch (ApiException exception)
        {
            await this.HandleMutationErrorAsync(exception);
        }
        catch (HttpRequestException)
        {
            this._requiresRefresh = true;
            this.SetLocalizedError("native.plans.net.svyazi.s.serverom.sokhranenie.ne.podtverzhdeno.obnovite.tari");
        }
        finally
        {
            this.IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanTogglePlan))]
    private async Task TogglePlanAsync(SubscriptionPlanListItem? item, CancellationToken cancellationToken)
    {
        if (item == null || !this.CanToggle(item.Plan))
        {
            return;
        }

        this.IsSaving = true;
        this.ClearMessages();
        try
        {
            var result = await this._sessionClient.ToggleSubscriptionPlanAsync(item.Id, cancellationToken);
            this.Message = result.Message;
            this._requiresRefresh = true;
            await this.RefreshKeepingMessageAsync(item.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                this._requiresRefresh = true;
                this.SetLocalizedError("native.plans.izmenenie.statusa.zanyalo.slishkom.mnogo.vremeni.obnovite.spisok");
            }
        }
        catch (ApiException exception)
        {
            await this.HandleMutationErrorAsync(exception);
        }
        catch (HttpRequestException)
        {
            this._requiresRefresh = true;
            this.SetLocalizedError("native.plans.net.svyazi.s.serverom.izmenenie.statusa.ne.podtverzhdeno.obnovit");
        }
        finally
        {
            this.IsSaving = false;
        }
    }

    [RelayCommand]
    private void RequestDelete()
    {
        this.IsDeleteConfirmationOpen = this.CanDeletePlan;
        this.NotifyEditorCommands();
    }

    [RelayCommand(CanExecute = nameof(CanConfirmDelete))]
    private async Task ConfirmDeleteAsync(CancellationToken cancellationToken)
    {
        var selected = this.SelectedPlan;
        if (!this.IsDeleteConfirmationOpen || selected is not { CanDelete: true })
        {
            return;
        }

        this.IsSaving = true;
        this.ClearMessages();
        try
        {
            var result = await this._sessionClient.DeleteSubscriptionPlanAsync(selected.Id, cancellationToken);
            this.SelectedPlan = null;
            this.Features = [];
            this.IsDeleteConfirmationOpen = false;
            this.Message = string.IsNullOrWhiteSpace(result.Message) ? AdminLocalization.Instance["native.plans.tarif.udalyon"] : result.Message;
            this._requiresRefresh = true;
            await this.RefreshKeepingMessageAsync(null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                this._requiresRefresh = true;
                this.SetLocalizedError("native.plans.udalenie.zanyalo.slishkom.mnogo.vremeni.obnovite.spisok.pered.po");
            }
        }
        catch (ApiException exception)
        {
            await this.HandleMutationErrorAsync(exception);
        }
        catch (HttpRequestException)
        {
            this._requiresRefresh = true;
            this.SetLocalizedError("native.plans.net.svyazi.s.serverom.udalenie.ne.podtverzhdeno.obnovite.spisok.");
        }
        finally
        {
            this.IsSaving = false;
        }
    }

    [RelayCommand]
    private void CancelDelete()
    {
        this.IsDeleteConfirmationOpen = false;
        this.NotifyEditorCommands();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        this.SelectedPlan = null;
        this.IsCreating = false;
        this.Features = [];
        this.IsDeleteConfirmationOpen = false;
        this.ClearMessages();
        this.NotifyEditorCommands();
    }

    private async Task LoadFeaturesAsync(AdminSubscriptionPlan plan, CancellationToken cancellationToken)
    {
        if (!string.Equals(plan.PlanTypeCode, "business", StringComparison.OrdinalIgnoreCase) || !this.CanManage())
        {
            this.Features = [];
            return;
        }

        var featureVersion = ++this._featureVersion;
        try
        {
            var features = await this._sessionClient.GetPlanFeaturesAsync(plan.Id, cancellationToken);
            if (this._isActive && featureVersion == this._featureVersion && this.SelectedPlan?.Id == plan.Id)
            {
                this.Features = features.Select(feature => new PlanFeatureEditor(feature)).ToArray();
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && featureVersion == this._featureVersion)
            {
                this.SetLocalizedError("native.plans.zagruzka.vozmozhnostey.tarifa.zanyala.slishkom.mnogo.vremeni");
            }
        }
        catch (ApiException exception)
        {
            await this.HandleErrorAsync(exception);
        }
        catch (HttpRequestException)
        {
            this.SetLocalizedError("native.plans.net.svyazi.s.serverom.ne.udalos.zagruzit.vozmozhnosti.tarifa");
        }
    }

    private async Task RefreshKeepingMessageAsync(int? selectedId, CancellationToken cancellationToken)
    {
        var message = this.Message;
        await this.LoadPlansAsync(selectedId, cancellationToken);
        if (this.HasPlans || !this.HasError)
        {
            this.Message = message;
        }
    }

    private async Task HandleMutationErrorAsync(ApiException exception)
    {
        var status = (int?)exception.StatusCode;
        var isUnconfirmed = status == null || status >= 500 || status is >= 200 and < 300;
        this._requiresRefresh |= isUnconfirmed;
        await this.HandleErrorAsync(exception);
        if (isUnconfirmed && this._isActive)
        {
            this.SetLocalizedError("native.plans.rezultat.operatsii.ne.podtverzhdyon.serverom.obnovite.spisok.per");
        }
    }

    private async Task HandleErrorAsync(ApiException exception)
    {
        if (!this._isActive)
        {
            return;
        }

        if (!this._sessionClient.HasSession || exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            this.SessionExpired?.Invoke();
            return;
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden && this.AccessDenied != null)
        {
            await this.AccessDenied.Invoke();
            return;
        }

        if (AdminDisplayLabels.TransportErrorKey(exception) is { } transportErrorKey)
        {
            this.SetLocalizedError(transportErrorKey);
            return;
        }

        this.ErrorMessage = exception.Message;
    }

    private IReadOnlyList<SubscriptionPlanListItem> CreateRows(IReadOnlyList<AdminSubscriptionPlan> plans) =>
        plans.Select(plan => new SubscriptionPlanListItem(plan, this.CanToggle(plan))).ToArray();

    private static string FormatPlanCount(int count)
    {
        var lastTwoDigits = count % 100;
        var lastDigit = count % 10;
        var suffix = lastTwoDigits is >= 11 and <= 14
            ? AdminLocalization.Instance["native.plans.tarifov"]
            : lastDigit switch
            {
                1 => AdminLocalization.Instance["native.plans.fallbackName"],
                >= 2 and <= 4 => AdminLocalization.Instance["native.plans.tarifa"],
                _ => AdminLocalization.Instance["native.plans.tarifov"]
            };

        return $"{count} {suffix}";
    }

    private bool CanToggle(AdminSubscriptionPlan plan)
    {
        return this.CanManage() && !plan.IsDefaultPlan && plan.Group != "workspace.free.v1" && !this.IsLoading && !this.IsSaving && !this._requiresRefresh
            && string.Equals(plan.PlanTypeCode, "business", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanManage() => this._isActive && this._sessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Configuration.Manage", StringComparer.Ordinal));

    private bool CanRefresh() => this.CanManage() && !this.IsLoading && !this.IsSaving;

    private bool CanSavePlan() => this.CanManage() && !this.IsLoading && !this.IsSaving && !this._requiresRefresh && !this.IsReadOnly
        && (this.IsCreating || this.SelectedPlan != null) && this.IsValidForm();

    private bool CanSaveFeatureValues() => this.CanManage() && !this.IsLoading && !this.IsSaving && !this._requiresRefresh
        && this.SelectedPlan is { PlanTypeCode: "business" } && this.Features.Count > 0;

    private bool CanConfirmDelete() => this.CanDeletePlan && this.IsDeleteConfirmationOpen;

    private bool CanTogglePlan(SubscriptionPlanListItem? item) => item != null && this.CanToggle(item.Plan);

    private bool IsValidForm()
    {
        var editor = this.Editor;
        return editor.Name.Trim().Length is > 0 and <= 100
            && editor.Description.Length <= 1000
            && editor.Price >= 0
            && editor.DurationDays >= 1
            && editor.VehicleLimit >= 0
            && !(editor.IsDefaultPlan && editor.IsDemoPlan);
    }

    partial void OnEditorChanged(SubscriptionPlanEditor? oldValue, SubscriptionPlanEditor newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= this.OnEditorPropertyChanged;
        }

        newValue.PropertyChanged += this.OnEditorPropertyChanged;
        this.SavePlanCommand.NotifyCanExecuteChanged();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        this.OnPropertyChanged(nameof(CanEditActiveFlag));
        this.OnPropertyChanged(nameof(CanEditDemoFlag));
        this.OnPropertyChanged(nameof(CanEditDefaultFlag));
        if (this.Editor.IsSystemFree && eventArgs.PropertyName is nameof(SubscriptionPlanEditor.IsActive)
            or nameof(SubscriptionPlanEditor.IsDefaultPlan) or nameof(SubscriptionPlanEditor.IsDemoPlan))
        {
            this.Editor.IsActive = true;
            this.Editor.IsDefaultPlan = this.SelectedPlan?.IsDefaultPlan == true;
            this.Editor.IsDemoPlan = false;
        }

        if (eventArgs.PropertyName == nameof(SubscriptionPlanEditor.IsDemoPlan)
            && this.Editor.IsDefaultPlanLocked && this.Editor.IsDemoPlan)
        {
            this.Editor.IsDemoPlan = false;
            this.SetLocalizedError("native.plans.snachala.naznachte.drugoy.tarif.po.umolchaniyu");
            return;
        }

        if (eventArgs.PropertyName == nameof(SubscriptionPlanEditor.IsDefaultPlan) && this.Editor.IsDefaultPlan)
        {
            this.Editor.IsDemoPlan = false;
            this.Editor.IsActive = true;
        }
        else if (eventArgs.PropertyName == nameof(SubscriptionPlanEditor.IsDemoPlan) && this.Editor.IsDemoPlan)
        {
            this.Editor.IsDefaultPlan = false;
            this.Editor.IsActive = true;
        }

        this.SavePlanCommand.NotifyCanExecuteChanged();
    }

    private bool IsCurrent(int requestVersion, CancellationToken cancellationToken) =>
        this._isActive && requestVersion == this._requestVersion && !cancellationToken.IsCancellationRequested;

    private void NotifyEditorCommands()
    {
        this.SavePlanCommand.NotifyCanExecuteChanged();
        this.SaveFeaturesCommand.NotifyCanExecuteChanged();
        this.ConfirmDeleteCommand.NotifyCanExecuteChanged();
    }

    private void SetLocalizedError(string key)
    {
        this._errorTranslationKey = key;
        this._isSettingLocalizedError = true;
        this.ErrorMessage = AdminLocalization.Instance[key];
        this._isSettingLocalizedError = false;
    }

    private void RefreshLocalizedError()
    {
        if (this._errorTranslationKey == null)
        {
            return;
        }

        this._isSettingLocalizedError = true;
        this.ErrorMessage = AdminLocalization.Instance[this._errorTranslationKey];
        this._isSettingLocalizedError = false;
    }

    partial void OnErrorMessageChanged(string value)
    {
        if (!this._isSettingLocalizedError)
        {
            this._errorTranslationKey = null;
        }
    }

    private void ClearMessages()
    {
        this.ErrorMessage = string.Empty;
        this.Message = string.Empty;
    }
}
