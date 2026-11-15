using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Управляет организационными и регистрационными промокодами. </summary>
public partial class PromoCodesViewModel(AdminSessionClient sessionClient) : ObservableObject
{
    private const string BonusDaysCode = "bonus_days";
    private readonly AdminSessionClient _sessionClient = sessionClient;
    private bool _isActive;
    private CancellationTokenSource? _lifetime;
    private Task _operation = Task.CompletedTask;
    private long _version;
    private bool _isMutationUncertain;
    private IReadOnlyList<AdminPromoCode> _promoCodes = [];
    private IReadOnlyList<AdminSubscriptionPlan> _plans = [];
    private int? _selectedId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanEditScope))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredRows))]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredRows))]
    private string _statusFilter = "all";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredRows))]
    private IReadOnlyList<PromoCodeRow> _rows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestDeleteCommand))]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    [NotifyPropertyChangedFor(nameof(IsLegacySelected))]
    [NotifyPropertyChangedFor(nameof(HasApplications))]
    [NotifyPropertyChangedFor(nameof(CanEditScope))]
    [NotifyPropertyChangedFor(nameof(AvailablePaidPlans))]
    [NotifyPropertyChangedFor(nameof(SelectedPlanNames))]
    [NotifyPropertyChangedFor(nameof(UsageRows))]
    [NotifyPropertyChangedFor(nameof(SelectedPromoCodeIsActive))]
    [NotifyPropertyChangedFor(nameof(StatusActionLabel))]
    [NotifyPropertyChangedFor(nameof(HasUsages))]
    [NotifyCanExecuteChangedFor(nameof(ToggleStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestDeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    private AdminPromoCode? _selectedPromoCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    [NotifyPropertyChangedFor(nameof(IsLegacySelected))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyCanExecuteChangedFor(nameof(RequestDeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    private bool _isCreating;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(HasApplications))]
    [NotifyPropertyChangedFor(nameof(CanEditScope))]
    [NotifyPropertyChangedFor(nameof(SelectedPlanNames))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidFromLocal))]
    private string _validFromText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidUntilLocal))]
    private string _validUntilText = string.Empty;

    [ObservableProperty]
    private string _maximumRedemptionsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGift))]
    [NotifyPropertyChangedFor(nameof(IsDiscount))]
    [NotifyPropertyChangedFor(nameof(CanEditScope))]
    [NotifyPropertyChangedFor(nameof(AvailablePaidPlans))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private PromoCodeOption _selectedBenefit = new(BonusDaysCode, "Подарочные дни");

    [ObservableProperty]
    private string _bonusDaysText = "30";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailablePaidPlans))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private AdminSubscriptionPlan? _giftPlan;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _discountValueText = string.Empty;

    [ObservableProperty]
    private string _discountValidityDaysText = string.Empty;

    [ObservableProperty]
    private string _organizationLimitText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditScope))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _appliesToAllScenarios = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _detailingScenario;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _serviceStationScenario;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _fleetScenario;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPlanNames))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private IReadOnlyList<AdminSubscriptionPlan> _selectedPlans = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedPlanCommand))]
    private AdminSubscriptionPlan? _selectedPlanToAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageRows))]
    [NotifyPropertyChangedFor(nameof(HasUsages))]
    [NotifyPropertyChangedFor(nameof(UsageCountLabel))]
    private IReadOnlyList<AdminPromoCodeUsage> _usages = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDeleteCommand))]
    private bool _isConfirmationOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _confirmationMessage = string.Empty;

    public event Action? SessionExpired;

    public event Func<Task>? AccessDenied;

    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    public bool HasMessage => !string.IsNullOrWhiteSpace(this.SuccessMessage);

    public bool IsEditorOpen => this.IsCreating || this.SelectedPromoCode != null;

    public bool IsLegacySelected => !this.IsCreating && this.SelectedPromoCode is { IsOrganizationCode: false };

    public bool HasApplications => !this.IsCreating && this.SelectedPromoCode is { IsOrganizationCode: true, OrganizationApplicationCount: > 0 };

    public bool CanEditScope => this.CanEdit && !this.IsLegacySelected && !this.HasApplications;

    public bool IsGift => this.SelectedBenefit.Code == BonusDaysCode;

    public bool IsDiscount => !this.IsGift;

    public bool CanEdit => this.CanManage() && !this.IsBusy && !this._isMutationUncertain;

    public bool IsMutationUncertain => this._isMutationUncertain;

    public bool IsEmpty => !this.IsBusy && this.Rows.Count == 0 && !this.HasError;

    public bool CanDelete => this.SelectedPromoCode is { RedemptionCount: 0, OrganizationApplicationCount: 0 };

    public bool SelectedPromoCodeIsActive => this.SelectedPromoCode?.IsActive == true;

    public string StatusActionLabel => this.SelectedPromoCodeIsActive ? "Отключить" : "Включить";

    public bool HasUsages => this.Usages.Count > 0;

    public string UsageCountLabel => $"Применений: {this.Usages.Count}";

    public string LegacyBenefit => this.SelectedPromoCode?.DemoDurationDays is int days
        ? $"Исторический регистрационный бонус: {days} дн."
        : this.SelectedPromoCode?.DiscountTypeCode switch
        {
            "percentage" => $"Скидка: {this.SelectedPromoCode.DiscountValue}%",
            "fixed_amount" or "fixed_price" => $"Скидка: {this.SelectedPromoCode.DiscountValue:N2} ₽",
            _ => "Без бонуса"
        };

    public IReadOnlyList<PromoCodeOption> BenefitOptions { get; } =
    [
        new(BonusDaysCode, "Подарочные дни"),
        new("percentage", "Скидка в процентах"),
        new("fixed_amount", "Скидка в рублях")
    ];

    public IReadOnlyList<PromoCodeOption> StatusOptions { get; } =
    [new("all", "Все статусы"), new("active", "Активные"), new("inactive", "Отключённые")];

    // HIDDEN_FLEET: служебный редактор сохраняет область старых промокодов. См. docs/hidden-fleet-scenario.md.
    public IReadOnlyList<PromoCodeOption> ScenarioOptions { get; } =
    [new("detailing", "Детейлинг"), new("service-station", "СТО"), new("fleet", "Автопарк")];

    public IReadOnlyList<AdminSubscriptionPlan> AvailablePaidPlans => this._plans
        .Where(plan => plan.IsActive && plan.PlanTypeCode == "business" && !plan.IsDemoPlan && !plan.IsDefaultPlan)
        .OrderBy(plan => plan.Order)
        .ThenBy(plan => plan.Id)
        .ToArray();

    public IReadOnlyList<PromoCodeRow> FilteredRows
    {
        get
        {
            var query = this.Search.Trim();

            return this.Rows.Where(row =>
                    (this.StatusFilter == "all" || row.PromoCode.IsActive == (this.StatusFilter == "active"))
                    && (query.Length == 0
                        || row.Code.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }
    }

    public string SelectedPlanNames => string.Join(", ", this.SelectedPlans.Select(plan => plan.Name));

    public IReadOnlyList<PromoCodeUsageRow> UsageRows => this.Usages
        .Select(usage => new PromoCodeUsageRow(
            usage,
            this.IsLegacySelected,
            this.SelectedPromoCode?.DiscountTypeCode ?? this.SelectedBenefit.Code))
        .ToArray();

    /// <summary> Активирует экран и загружает промокоды, тарифы и историю выбранной записи. </summary>
    public async Task ActivateAsync()
    {
        await this.DeactivateAsync(clear: true);
        this._lifetime = new CancellationTokenSource();
        this._isActive = true;
        this.NotifyEditState();
        await this.RunAsync(this.LoadAsync);
    }

    /// <summary> Отменяет текущую работу и при необходимости очищает экран. </summary>
    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this._version++;
        this._lifetime?.Cancel();
        this.IsBusy = false;
        this.NotifyEditState();
        if (clear)
        {
            this.ClearData();
        }
    }

    /// <summary> Деактивирует экран и дожидается завершения отменённых запросов. </summary>
    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        try
        {
            await this._operation;
        }
        catch (OperationCanceledException)
        {
            // Уход с экрана отменяет текущий запрос.
        }

        this._lifetime?.Dispose();
        this._lifetime = null;
    }

    private Task LoadAsync(CancellationToken token) => this.LoadDataAsync(this._selectedId, token);

    private async Task LoadDataAsync(int? selectedId, CancellationToken cancellationToken)
    {
        var promoCodesTask = this._sessionClient.GetPromoCodesAsync(cancellationToken);
        var plansTask = this._sessionClient.GetSubscriptionPlansAsync(cancellationToken);
        await Task.WhenAll(promoCodesTask, plansTask);
        cancellationToken.ThrowIfCancellationRequested();
        this._promoCodes = await promoCodesTask;
        this._plans = await plansTask;
        this.Rows = this._promoCodes.Select(promoCode => new PromoCodeRow(promoCode)).ToArray();

        var selected = selectedId is int id ? this._promoCodes.FirstOrDefault(item => item.Id == id) : null;
        if (selected != null)
        {
            this.SetSelectedPromoCode(selected);
            this.Usages = await this._sessionClient.GetPromoCodeUsagesAsync(selected.Id, cancellationToken);
        }
        else
        {
            this._selectedId = null;
            this.SelectedPromoCode = null;
            this.Usages = [];
            if (!this.IsCreating)
            {
                this.CloseEditor();
            }
        }

        this.OnPropertyChanged(nameof(AvailablePaidPlans));
        this.OnPropertyChanged(nameof(FilteredRows));
        this.SetMutationUncertain(false);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenRowAsync(PromoCodeRow? row)
    {
        if (row == null)
        {
            return;
        }

        await this.RunAsync(async token =>
        {
            var current = this._promoCodes.FirstOrDefault(item => item.Id == row.Id);
            if (current == null)
            {
                return;
            }

            this.SetSelectedPromoCode(current);
            this.IsCreating = false;
            this.Usages = await this._sessionClient.GetPromoCodeUsagesAsync(current.Id, token);
        });
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Create()
    {
        this.SelectedPromoCode = null;
        this._selectedId = null;
        this.IsCreating = true;
        this.ClearForm();
        this.Usages = [];
        this.ClearMessages();
    }

    private void SetSelectedPromoCode(AdminPromoCode promoCode)
    {
        this._selectedId = promoCode.Id;
        this.SelectedPromoCode = promoCode;
        this.IsCreating = false;
        this.Code = promoCode.Code;
        this.Name = promoCode.Name;
        this.Description = promoCode.Description ?? string.Empty;
        this.IsEnabled = promoCode.IsActive;
        this.ValidFromText = FormatLocalDate(promoCode.ValidFrom);
        this.ValidUntilText = FormatLocalDate(promoCode.ValidUntil);
        this.MaximumRedemptionsText = promoCode.MaximumRedemptions?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        this.SelectedBenefit = this.BenefitOptions.FirstOrDefault(option => option.Code == promoCode.DiscountTypeCode)
            ?? this.BenefitOptions[0];
        this.BonusDaysText = promoCode.BonusDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        this.GiftPlan = this._plans.FirstOrDefault(plan => plan.Id == promoCode.GiftTemplatePlanId);
        this.DiscountValueText = promoCode.DiscountValue?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        this.DiscountValidityDaysText = promoCode.DiscountValidityDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        this.OrganizationLimitText = promoCode.MaximumOrganizationApplications?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        this.AppliesToAllScenarios = promoCode.AppliesToAllScenarios;
        this.DetailingScenario = promoCode.ScenarioCodes.Contains("detailing");
        this.ServiceStationScenario = promoCode.ScenarioCodes.Contains("service-station");
        this.FleetScenario = promoCode.ScenarioCodes.Contains("fleet");
        this.SelectedPlans = promoCode.SubscriptionPlanIds
            .Select(id => this._plans.FirstOrDefault(plan => plan.Id == id))
            .OfType<AdminSubscriptionPlan>()
            .ToArray();
        this.IsConfirmationOpen = false;
        this.ClearMessages();
    }

    private void CloseEditor()
    {
        this.SelectedPromoCode = null;
        this._selectedId = null;
        this.IsCreating = false;
        this.IsConfirmationOpen = false;
        this.Usages = [];
    }

    [RelayCommand]
    private Task CloseEditorAsync()
    {
        this.CloseEditor();
        return Task.CompletedTask;
    }

    private void AddPlanCore()
    {
        var plan = this.SelectedPlanToAdd;
        if (plan == null || this.SelectedPlans.Any(item => item.Id == plan.Id))
        {
            return;
        }

        this.SelectedPlans = [.. this.SelectedPlans, plan];
        this.SelectedPlanToAdd = null;
    }

    private void RemovePlanCore(AdminSubscriptionPlan? plan)
    {
        if (plan == null)
        {
            return;
        }

        this.SelectedPlans = this.SelectedPlans.Where(item => item.Id != plan.Id).ToArray();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var validationError = this.ValidateForm();
        if (validationError.Length > 0)
        {
            this.ErrorMessage = validationError;
            return;
        }

        var request = this.CreateRequest();
        var selectedId = this.SelectedPromoCode?.Id;
        await this.RunAsync(async token =>
        {
            if (this.IsCreating)
            {
                var created = await this.RunMutationAsync(() => this._sessionClient.CreatePromoCodeAsync(request, token));
                this._selectedId = created.Id;
                this.SetSelectedPromoCode(created);
            }
            else if (selectedId is int id)
            {
                await this.RunMutationAsync(() => this._sessionClient.UpdatePromoCodeAsync(id, request, token));
                this._selectedId = id;
                this.SetSelectedPromoCode(this.ApplyRequest(this.SelectedPromoCode!, request));
            }

            await this.LoadDataAsync(this._selectedId, token);
            this.SuccessMessage = "Промокод сохранён.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanToggleStatus))]
    private async Task ToggleStatusAsync()
    {
        var promoCode = this.SelectedPromoCode;
        if (promoCode == null)
        {
            return;
        }

        await this.RunAsync(async token =>
        {
            await this.RunMutationAsync(() => this._sessionClient.SetPromoCodeStatusAsync(promoCode.Id, !promoCode.IsActive, token));
            this._selectedId = promoCode.Id;
            await this.LoadDataAsync(this._selectedId, token);
            this.SuccessMessage = promoCode.IsActive ? "Промокод отключён." : "Промокод включён.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRequestDelete))]
    private void RequestDelete()
    {
        if (!this.CanDelete)
        {
            this.ErrorMessage = "Промокод уже использован. Его можно отключить, но нельзя удалить.";
            return;
        }

        this.ConfirmationMessage = $"Удалить промокод «{this.SelectedPromoCode!.Code}»? Это действие нельзя отменить.";
        this.IsConfirmationOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmDelete))]
    private async Task ConfirmDeleteAsync()
    {
        var promoCode = this.SelectedPromoCode;
        if (promoCode == null || !this.CanDelete)
        {
            return;
        }

        await this.RunAsync(async token =>
        {
            await this.RunMutationAsync(() => this._sessionClient.DeletePromoCodeAsync(promoCode.Id, token));
            this._selectedId = null;
            this.CloseEditor();
            this.SuccessMessage = "Промокод удалён.";
            await this.LoadDataAsync(null, token);
        });
    }

    private string ValidateForm()
    {
        if (this.Code.Trim().Length is < 1 or > 64)
        {
            return "Код промокода должен содержать от 1 до 64 символов.";
        }

        if (this.Name.Trim().Length is < 1 or > 160)
        {
            return "Название должно содержать от 1 до 160 символов.";
        }

        if (this.Description.Length > 1000)
        {
            return "Описание не должно превышать 1000 символов.";
        }

        if (!TryParseLocalDate(this.ValidFromText, out var validFrom)
            || !TryParseLocalDate(this.ValidUntilText, out var validUntil))
        {
            return "Введите дату и время в формате ГГГГ-ММ-ДД ЧЧ:ММ.";
        }

        if (validFrom.HasValue && validUntil.HasValue && validFrom.Value >= validUntil.Value)
        {
            return "Дата окончания должна быть позже даты начала.";
        }

        if (!TryParseOptionalPositiveInt(this.MaximumRedemptionsText, out var maximumRedemptions))
        {
            return "Лимит регистраций должен быть целым числом не меньше 1.";
        }

        if (this.IsLegacySelected)
        {
            return string.Empty;
        }

        if (this.IsGift)
        {
            if (this.GiftPlan is not { IsActive: true, IsDemoPlan: false, IsDefaultPlan: false, PlanTypeCode: "business" })
            {
                return "Выберите активный платный бизнес-тариф для подарочного доступа.";
            }

            if (!TryParseBoundedInt(this.BonusDaysText, 1, 3650, out _))
            {
                return "Количество подарочных дней должно быть от 1 до 3650.";
            }
        }
        else
        {
            if (!decimal.TryParse(this.DiscountValueText, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
                || value <= 0
                || value > 100_000_000
                || (this.SelectedBenefit.Code == "percentage" && value > 99))
            {
                return this.SelectedBenefit.Code == "percentage"
                    ? "Процент скидки должен быть больше 0 и не превышать 99."
                    : "Укажите положительный размер скидки.";
            }

            if (this.SelectedPlans.Count == 0)
            {
                return "Выберите хотя бы один платный бизнес-тариф.";
            }

            if (!TryParseOptionalBoundedInt(this.DiscountValidityDaysText, 1, 3650, out _))
            {
                return "Срок скидки должен быть целым числом от 1 до 3650 дней.";
            }
        }

        if (!this.AppliesToAllScenarios && !this.HasSelectedScenario)
        {
            return "Выберите хотя бы одно направление или включите все направления.";
        }

        if (!TryParseOptionalPositiveInt(this.OrganizationLimitText, out _))
        {
            return "Лимит применений организациями должен быть целым числом не меньше 1.";
        }

        return string.Empty;
    }

    private AdminPromoCodeSaveRequest CreateRequest()
    {
        if (this.IsLegacySelected)
        {
            var source = this.SelectedPromoCode!;
            return new AdminPromoCodeSaveRequest(
                this.Code.Trim().ToUpperInvariant(),
                this.Name.Trim(),
                NullIfWhiteSpace(this.Description),
                this.IsEnabled,
                ParseLocalDate(this.ValidFromText),
                ParseLocalDate(this.ValidUntilText),
                source.MaximumRedemptions,
                source.DemoDurationDays,
                source.DiscountTypeCode,
                source.DiscountValue,
                source.DiscountValidityDays,
                source.DiscountPaymentLimitPerUser,
                source.AppliesToAllPaidPlans,
                source.SubscriptionPlanIds,
                source.BonusDays,
                source.MaximumOrganizationApplications,
                source.AppliesToAllScenarios,
                source.ScenarioCodes,
                source.GiftTemplatePlanId);
        }

        var isGift = this.IsGift;
        var scenarioCodes = this.AppliesToAllScenarios ? [] : this.SelectedScenarios.ToArray();
        return new AdminPromoCodeSaveRequest(
            this.Code.Trim().ToUpperInvariant(),
            this.Name.Trim(),
            NullIfWhiteSpace(this.Description),
            this.IsEnabled,
            ParseLocalDate(this.ValidFromText),
            ParseLocalDate(this.ValidUntilText),
            null,
            null,
            this.SelectedBenefit.Code,
            isGift ? null : decimal.Parse(this.DiscountValueText, CultureInfo.CurrentCulture),
            isGift ? null : ParseOptionalInt(this.DiscountValidityDaysText),
            null,
            isGift,
            isGift ? [] : this.SelectedPlans.Select(plan => plan.Id).ToArray(),
            isGift ? int.Parse(this.BonusDaysText, CultureInfo.InvariantCulture) : null,
            ParseOptionalInt(this.OrganizationLimitText),
            this.AppliesToAllScenarios,
            scenarioCodes,
            isGift ? this.GiftPlan!.Id : null);
    }

    private bool HasSelectedScenario => this.SelectedScenarios.Any();

    private IEnumerable<string> SelectedScenarios
    {
        get
        {
            if (this.DetailingScenario)
            {
                yield return "detailing";
            }

            if (this.ServiceStationScenario)
            {
                yield return "service-station";
            }

            if (this.FleetScenario)
            {
                yield return "fleet";
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync() => await this.RunAsync(this.LoadAsync);

    private Task RunMutationAsync(Func<Task> mutation) => this.RunMutationAsync(async () =>
    {
        await mutation();

        return true;
    });

    private async Task<T> RunMutationAsync<T>(Func<Task<T>> mutation)
    {
        try
        {
            var result = await mutation();
            this.SetMutationUncertain(true);

            return result;
        }
        catch (Exception exception) when (
            exception is ApiException apiException && PromoCodesViewModel.HasUnknownMutationOutcome(apiException)
            || exception is HttpRequestException
            || exception is OperationCanceledException && this._lifetime?.IsCancellationRequested != true)
        {
            this.SetMutationUncertain(true);
            if (this.IsCreating)
            {
                this.IsCreating = false;
            }

            this.ErrorMessage = "Не удалось подтвердить результат операции. Обновите список перед следующими изменениями.";
            throw;
        }
    }

    private static bool HasUnknownMutationOutcome(ApiException exception) => exception.StatusCode is null
        || (int)exception.StatusCode >= 500
        || (int)exception.StatusCode is >= 200 and < 300;

    private AdminPromoCode ApplyRequest(AdminPromoCode promoCode, AdminPromoCodeSaveRequest request) => new()
    {
        Id = promoCode.Id,
        Code = request.Code,
        Name = request.Name,
        Description = request.Description,
        IsActive = request.IsActive,
        ValidFrom = request.ValidFrom,
        ValidUntil = request.ValidUntil,
        MaximumRedemptions = request.MaximumRedemptions,
        RedemptionCount = promoCode.RedemptionCount,
        RemainingRedemptions = request.MaximumRedemptions is int maximum ? maximum - promoCode.RedemptionCount : null,
        DemoDurationDays = request.DemoDurationDays,
        DiscountTypeCode = request.DiscountTypeCode,
        DiscountValue = request.DiscountValue,
        DiscountValidityDays = request.DiscountValidityDays,
        DiscountPaymentLimitPerUser = request.DiscountPaymentLimitPerUser,
        AppliesToAllPaidPlans = request.AppliesToAllPaidPlans,
        SubscriptionPlanIds = [.. request.SubscriptionPlanIds],
        SubscriptionPlanNames = request.SubscriptionPlanIds
            .Select(id => this._plans.FirstOrDefault(plan => plan.Id == id)?.Name ?? string.Empty)
            .ToList(),
        BonusDays = request.BonusDays,
        MaximumOrganizationApplications = request.MaximumOrganizationApplications,
        OrganizationApplicationCount = promoCode.OrganizationApplicationCount,
        RemainingOrganizationApplications = request.MaximumOrganizationApplications is int limit
            ? limit - promoCode.OrganizationApplicationCount
            : null,
        AppliesToAllScenarios = request.AppliesToAllScenarios,
        ScenarioCodes = [.. request.ScenarioCodes],
        GiftTemplatePlanId = request.GiftTemplatePlanId,
        IsOrganizationCode = promoCode.IsOrganizationCode,
        CreatedAt = promoCode.CreatedAt,
        UpdatedAt = promoCode.UpdatedAt
    };

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (!this.CanManage() || this.IsBusy || this._lifetime == null)
        {
            return;
        }

        var version = this._version;
        var token = this._lifetime.Token;
        this._operation = this.ExecuteOperationAsync(action, version, token);
        await this._operation;
    }

    private async Task ExecuteOperationAsync(Func<CancellationToken, Task> action, long version, CancellationToken token)
    {
        this.IsBusy = true;
        this.ClearMessages();
        try
        {
            await action(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Уход с экрана отменяет загрузку и не показывает ошибку.
        }
        catch (ApiException exception) when (version == this._version && this._isActive)
        {
            this.ErrorMessage = this.IsMutationUncertain
                ? "Не удалось подтвердить результат операции. Обновите список перед следующими изменениями."
                : exception.Message;
            if (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                this.SessionExpired?.Invoke();
            }
            else if (exception.StatusCode == HttpStatusCode.Forbidden && this.AccessDenied != null)
            {
                await this.AccessDenied.Invoke();
            }
        }
        catch (ApiException) when (version != this._version || !this._isActive)
        {
            // Ответ устаревшей сессии не меняет состояние экрана.
        }
        catch (HttpRequestException) when (version == this._version && this._isActive)
        {
            this.ErrorMessage = "Не удалось связаться с сервером. Проверьте подключение.";
        }
        finally
        {
            if (version == this._version)
            {
                this.IsBusy = false;
            }
        }
    }

    private void ClearData()
    {
        this._promoCodes = [];
        this._plans = [];
        this._selectedId = null;
        this.Rows = [];
        this.Usages = [];
        this.SelectedPromoCode = null;
        this.IsCreating = false;
        this.IsConfirmationOpen = false;
        this.ClearForm();
        this.ClearMessages();
        this.SetMutationUncertain(false);
    }

    private void ClearForm()
    {
        this.Code = string.Empty;
        this.Name = string.Empty;
        this.Description = string.Empty;
        this.IsEnabled = true;
        this.ValidFromText = string.Empty;
        this.ValidUntilText = string.Empty;
        this.MaximumRedemptionsText = string.Empty;
        this.SelectedBenefit = this.BenefitOptions[0];
        this.BonusDaysText = "30";
        this.GiftPlan = null;
        this.DiscountValueText = string.Empty;
        this.DiscountValidityDaysText = string.Empty;
        this.OrganizationLimitText = string.Empty;
        this.AppliesToAllScenarios = true;
        this.DetailingScenario = false;
        this.ServiceStationScenario = false;
        this.FleetScenario = false;
        this.SelectedPlans = [];
    }

    private void ClearMessages()
    {
        this.ErrorMessage = string.Empty;
        this.SuccessMessage = string.Empty;
    }

    /// <summary> Начало действия в местном времени для календаря. </summary>
    public DateTime? ValidFromLocal
    {
        get => ParseLocalDate(this.ValidFromText)?.ToLocalTime();
        set => this.ValidFromText = value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary> Окончание действия в местном времени для календаря. </summary>
    public DateTime? ValidUntilLocal
    {
        get => ParseLocalDate(this.ValidUntilText)?.ToLocalTime();
        set => this.ValidUntilText = value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatLocalDate(DateTime? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    private static bool TryParseLocalDate(string value, out DateTime? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return false;
        }

        result = parsed.ToUniversalTime();
        return true;
    }

    private static DateTime? ParseLocalDate(string value)
    {
        TryParseLocalDate(value, out var result);
        return result;
    }

    private static bool TryParseBoundedInt(string value, int minimum, int maximum, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= minimum && result <= maximum;

    private static bool TryParseOptionalPositiveInt(string value, out int? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
        {
            return false;
        }

        result = parsed;
        return true;
    }

    private static bool TryParseOptionalBoundedInt(string value, int minimum, int maximum, out int? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!TryParseBoundedInt(value, minimum, maximum, out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    private static int? ParseOptionalInt(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    partial void OnSearchChanged(string value) => this.OnPropertyChanged(nameof(FilteredRows));

    partial void OnStatusFilterChanged(string value) => this.OnPropertyChanged(nameof(FilteredRows));

    partial void OnIsBusyChanged(bool value)
    {
        this.OnPropertyChanged(nameof(CanEdit));
        this.OnPropertyChanged(nameof(CanEditScope));
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.SaveCommand.NotifyCanExecuteChanged();
        this.ToggleStatusCommand.NotifyCanExecuteChanged();
        this.ConfirmDeleteCommand.NotifyCanExecuteChanged();
        this.RequestDeleteCommand.NotifyCanExecuteChanged();
        this.CreateCommand.NotifyCanExecuteChanged();
        this.OpenRowCommand.NotifyCanExecuteChanged();
        this.AddSelectedPlanCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefresh() => this.CanManage() && !this.IsBusy;

    private bool CanManage() => this._isActive
        && this._sessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Configuration.Manage", StringComparer.Ordinal));

    private void NotifyEditState()
    {
        this.OnPropertyChanged(nameof(CanEdit));
        this.OnPropertyChanged(nameof(CanEditScope));
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.SaveCommand.NotifyCanExecuteChanged();
        this.ToggleStatusCommand.NotifyCanExecuteChanged();
        this.ConfirmDeleteCommand.NotifyCanExecuteChanged();
        this.RequestDeleteCommand.NotifyCanExecuteChanged();
        this.CreateCommand.NotifyCanExecuteChanged();
        this.OpenRowCommand.NotifyCanExecuteChanged();
        this.AddSelectedPlanCommand.NotifyCanExecuteChanged();
    }

    private void SetMutationUncertain(bool value)
    {
        if (this._isMutationUncertain == value)
        {
            return;
        }

        this._isMutationUncertain = value;
        this.OnPropertyChanged(nameof(IsMutationUncertain));
        this.OnPropertyChanged(nameof(CanEdit));
        this.OnPropertyChanged(nameof(CanEditScope));
        this.NotifyEditState();
    }

    [RelayCommand]
    private void CancelDelete() => this.IsConfirmationOpen = false;

    [RelayCommand(CanExecute = nameof(CanAddPlan))]
    private void AddSelectedPlan() => this.AddPlanCore();

    [RelayCommand]
    private void RemovePlan(AdminSubscriptionPlan? plan) => this.RemovePlanCore(plan);

    private bool CanSave() => this.CanEdit && this.IsEditorOpen;

    private bool CanToggleStatus() => this.CanEdit && this.SelectedPromoCode != null;

    private bool CanRequestDelete() => this.CanEdit && this.SelectedPromoCode != null && this.CanDelete;

    private bool CanConfirmDelete() => this.CanEdit && this.IsConfirmationOpen && this.CanDelete;

    private bool CanAddPlan() => this.CanEdit && this.SelectedPlanToAdd != null && this.CanEditScope;
}
