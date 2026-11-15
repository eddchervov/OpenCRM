using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Управление настройками, доступом и расходами ИИ-помощника. </summary>
public partial class AssistantViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    private string _organizationSearch = string.Empty;
    private int _organizationPage = 1;
    private bool _hasAppliedUsageFilters;
    private DateTime _appliedUsageFrom;
    private DateTime _appliedUsageTo;
    private string _appliedChannelFilter = string.Empty;
    private string _appliedModelFilter = string.Empty;
    private string _appliedStatusFilter = string.Empty;

    [ObservableProperty]
    private AdminAssistantOverview? _overview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _webEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _telegramEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _speechRepliesEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private string _textModel = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private string _speechModel = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private string _voiceModel = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private string _voiceName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private decimal _dailyBudgetRub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private decimal _monthlyBudgetRub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private decimal _textReservationRub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private decimal _audioReservationRub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private int _maximumOutputTokens;

    [ObservableProperty]
    private IReadOnlyList<AssistantOrganizationRow> _organizations = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOrganizationAccessCommand))]
    private AssistantOrganizationRow? _selectedOrganization;

    [ObservableProperty]
    private bool _organizationIsEnabled;

    [ObservableProperty]
    private bool _organizationWebEnabled;

    [ObservableProperty]
    private bool _organizationTelegramEnabled;

    [ObservableProperty]
    private DateTime? _organizationEnabledUntilLocal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOrganizationAccessCommand))]
    private decimal _organizationDailyBudgetRub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveOrganizationAccessCommand))]
    private decimal _organizationMonthlyBudgetRub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FilterUsageCommand))]
    private DateTime _usageFrom = DateTime.Today.AddDays(-6);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FilterUsageCommand))]
    private DateTime _usageTo = DateTime.Today;

    [ObservableProperty]
    private IReadOnlyList<AdminAssistantUsageItem> _usageItems = [];

    [ObservableProperty]
    private AdminAssistantUsageResponse? _usage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SettleUsageCommand))]
    private AdminAssistantUsageItem? _selectedUsage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SettleUsageCommand))]
    private decimal _settlementCostRub;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SettleUsageCommand))]
    private string _settlementReason = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousUsagePageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextUsagePageCommand))]
    private int _usagePage = 1;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    public bool CanManage => this.SessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Configuration.Manage", StringComparer.Ordinal));
    public bool CanViewBilling => this.SessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Billing.View", StringComparer.Ordinal));
    public bool CanSettle => this.CanManage;
    public bool HasUsage => this.CanViewBilling && this.Usage != null;
    public bool HasOrganizations => this.Organizations.Count > 0;
    public bool HasAudits => this.Overview?.Audits.Count > 0;
    public bool HasUnknownSelectedUsage => this.SelectedUsage?.Status == "unknown";
    public string ProviderLabel => this.Overview == null ? string.Empty
        : $"RouterAI: {(this.Overview.Provider.RouterConfigured ? "настроен" : "не настроен")}; Telegram: "
            + (this.Overview.Provider.TelegramConfigured ? "настроен" : "не настроен")
            + (string.IsNullOrWhiteSpace(this.Overview.Provider.TelegramBotUsername) ? string.Empty : $" (@{this.Overview.Provider.TelegramBotUsername})");
    public string OrganizationPageLabel => this.Overview == null ? string.Empty
        : $"Страница {this.Overview.Page} · найдено {this.Overview.TotalCount:N0}";
    public string UsagePageLabel => this.Usage == null ? string.Empty
        : $"Страница {this.Usage.Page} · найдено {this.Usage.TotalCount:N0}";

    public IReadOnlyList<CommunicationOption> ChannelOptions { get; } =
    [new("", "Все каналы"), new("web", "Сайт"), new("telegram", "Telegram")];

    [ObservableProperty]
    private CommunicationOption _selectedChannel = new("", "Все каналы");

    [ObservableProperty]
    private string _modelFilter = string.Empty;

    public string BudgetLimitsLabel => this.Overview == null ? string.Empty
        : $"Максимальный бюджет: {this.Overview.Limits.MaximumBudgetRub:N2} ₽; резерв: {this.Overview.Limits.MaximumReservationRub:N2} ₽; "
            + $"токены: {this.Overview.Limits.MaximumOutputTokensAllowed:N0}; часовой пояс: {this.Overview.Limits.BudgetTimeZone}.";

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        if (!this._hasAppliedUsageFilters)
        {
            this.ApplyUsageFilters();
        }

        if (!this.CanManage && !this.CanViewBilling)
        {
            this.ErrorMessage = "Нет права на просмотр настроек или расходов ИИ-помощника.";
            return;
        }

        if (this.CanManage)
        {
            await this.LoadOverviewAsync(cancellationToken);
        }

        if (this.CanViewBilling)
        {
            await this.LoadUsageAsync(cancellationToken);
        }
    }

    protected override void ApplyFilters() => this._organizationSearch = this.Search.Trim();

    protected override void ResetFilters()
    {
        this._organizationSearch = string.Empty;
        this.Search = string.Empty;
        this.SelectedChannel = this.ChannelOptions[0];
        this.ModelFilter = string.Empty;
        this.SelectedStatus = this.StatusOptions[0];
    }

    protected override void ClearData()
    {
        this.Overview = null;
        this.Organizations = [];
        this.SelectedOrganization = null;
        this.Usage = null;
        this.UsageItems = [];
        this.SelectedUsage = null;
        this.UsagePage = 1;
        this._hasAppliedUsageFilters = false;
        this.SuccessMessage = string.Empty;
    }

    public void RefreshAccess()
    {
        this.OnPropertyChanged(nameof(CanManage));
        this.OnPropertyChanged(nameof(CanViewBilling));
        this.OnPropertyChanged(nameof(CanSettle));
        this.OnPropertyChanged(nameof(HasUsage));
        this.NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.SaveSettingsCommand.NotifyCanExecuteChanged();
        this.SaveOrganizationAccessCommand.NotifyCanExecuteChanged();
        this.SettleUsageCommand.NotifyCanExecuteChanged();
        this.PreviousOrganizationPageCommand.NotifyCanExecuteChanged();
        this.NextOrganizationPageCommand.NotifyCanExecuteChanged();
        this.PreviousUsagePageCommand.NotifyCanExecuteChanged();
        this.NextUsagePageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    private async Task SaveSettingsAsync()
    {
        await this.RunAsync(async cancellationToken =>
        {
            this.SuccessMessage = string.Empty;
            var settings = this.Overview!.Settings;
            try
            {
                await this.SessionClient.SaveAssistantSettingsAsync(new AdminAssistantSettingsRequest
                {
                    IsEnabled = this.IsEnabled,
                    WebEnabled = this.WebEnabled,
                    TelegramEnabled = this.TelegramEnabled,
                    SpeechRepliesEnabled = this.SpeechRepliesEnabled,
                    TextModel = this.TextModel.Trim(),
                    SpeechModel = this.SpeechModel.Trim(),
                    VoiceModel = this.VoiceModel.Trim(),
                    VoiceName = this.VoiceName.Trim(),
                    DailyBudgetRub = this.DailyBudgetRub,
                    MonthlyBudgetRub = this.MonthlyBudgetRub,
                    TextReservationRub = this.TextReservationRub,
                    AudioReservationRub = this.AudioReservationRub,
                    MaximumOutputTokens = this.MaximumOutputTokens,
                    Revision = settings.Revision
                }, cancellationToken);
            }
            catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                this.ErrorMessage = "Настройки изменились на сервере. Данные обновлены; проверьте их и сохраните ещё раз.";
                await this.LoadOverviewAsync(cancellationToken);
                return;
            }

            await this.LoadOverviewAsync(cancellationToken);
            this.SuccessMessage = "Настройки помощника сохранены.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task SearchOrganizationsAsync() => this.RunAsync(async cancellationToken =>
    {
        this._organizationSearch = this.Search.Trim();
        this._organizationPage = 1;
        await this.LoadOverviewAsync(cancellationToken);
    });

    [RelayCommand(CanExecute = nameof(CanPreviousOrganizationPage))]
    private Task PreviousOrganizationPageAsync() => this.LoadOrganizationPageAsync(this._organizationPage - 1);

    [RelayCommand(CanExecute = nameof(CanNextOrganizationPage))]
    private Task NextOrganizationPageAsync() => this.LoadOrganizationPageAsync(this._organizationPage + 1);

    [RelayCommand(CanExecute = nameof(CanSaveOrganizationAccess))]
    private Task SaveOrganizationAccessAsync() => this.RunAsync(async cancellationToken =>
    {
        var organization = this.SelectedOrganization!.Organization;
        this.SuccessMessage = string.Empty;
        await this.SessionClient.SaveAssistantOrganizationAccessAsync(organization.OrganizationId,
            new AdminAssistantOrganizationAccessRequest
            {
                IsEnabled = this.OrganizationIsEnabled,
                WebEnabled = this.OrganizationWebEnabled,
                TelegramEnabled = this.OrganizationTelegramEnabled,
                EnabledUntil = this.OrganizationEnabledUntilLocal is { } enabledUntil
                    ? new DateTimeOffset(DateTime.SpecifyKind(enabledUntil, DateTimeKind.Local)).ToUniversalTime()
                    : null,
                DailyBudgetRub = this.OrganizationDailyBudgetRub,
                MonthlyBudgetRub = this.OrganizationMonthlyBudgetRub
            }, cancellationToken);
        await this.LoadOverviewAsync(cancellationToken);
        this.SuccessMessage = "Доступ организации сохранён и записан в аудит.";
    });

    [RelayCommand(CanExecute = nameof(CanFilterUsage))]
    private Task LoadUsagePageAsync() => this.RunAsync(async cancellationToken =>
    {
        this.UsagePage = 1;
        await this.LoadUsageAsync(cancellationToken);
    });

    [RelayCommand(CanExecute = nameof(CanPreviousUsagePage))]
    private Task PreviousUsagePageAsync() => this.RunAsync(async cancellationToken =>
    {
        this.UsagePage--;
        await this.LoadUsageAsync(cancellationToken);
    });

    [RelayCommand(CanExecute = nameof(CanNextUsagePage))]
    private Task NextUsagePageAsync() => this.RunAsync(async cancellationToken =>
    {
        this.UsagePage++;
        await this.LoadUsageAsync(cancellationToken);
    });

    [RelayCommand(CanExecute = nameof(CanSettleUsage))]
    private Task SettleUsageAsync() => this.RunAsync(async cancellationToken =>
    {
        await this.SessionClient.SettleAssistantUsageAsync(this.SelectedUsage!.Id, this.SettlementCostRub,
            this.SettlementReason.Trim(), cancellationToken);
        this.SettlementReason = string.Empty;
        await this.LoadUsageAsync(cancellationToken);
        this.SuccessMessage = "Фактическая стоимость сохранена в аудите.";
    });

    [RelayCommand(CanExecute = nameof(CanFilterUsage))]
    private Task FilterUsageAsync() => this.RunAsync(async cancellationToken =>
    {
        this.UsagePage = 1;
        this.ApplyUsageFilters();
        await this.LoadUsageAsync(cancellationToken);
    });

    partial void OnSelectedOrganizationChanged(AssistantOrganizationRow? value)
    {
        var organization = value?.Organization;
        this.OrganizationIsEnabled = organization?.IsEnabled ?? false;
        this.OrganizationWebEnabled = organization?.WebEnabled ?? false;
        this.OrganizationTelegramEnabled = organization?.TelegramEnabled ?? false;
        this.OrganizationEnabledUntilLocal = organization?.EnabledUntil?.LocalDateTime;
        this.OrganizationDailyBudgetRub = organization?.DailyBudgetRub ?? 0;
        this.OrganizationMonthlyBudgetRub = organization?.MonthlyBudgetRub ?? 0;
        this.SaveOrganizationAccessCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedUsageChanged(AdminAssistantUsageItem? value)
    {
        this.OnPropertyChanged(nameof(HasUnknownSelectedUsage));
        this.SettlementCostRub = value?.CostRub ?? value?.ReservedCostRub ?? 0;
        this.SettleUsageCommand.NotifyCanExecuteChanged();
    }

    partial void OnUsageChanged(AdminAssistantUsageResponse? value)
    {
        this.UsageItems = value?.Items ?? [];
        this.OnPropertyChanged(nameof(HasUsage));
        this.OnPropertyChanged(nameof(UsagePageLabel));
        this.PreviousUsagePageCommand.NotifyCanExecuteChanged();
        this.NextUsagePageCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadOverviewAsync(CancellationToken cancellationToken)
    {
        var overview = await this.SessionClient.GetAssistantAsync(this._organizationSearch, this._organizationPage,
            20, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Overview = overview;
        this.ApplySettings(overview.Settings);
        this.Organizations = overview.Organizations.Select(organization => new AssistantOrganizationRow(organization)).ToArray();
        this.OnPropertyChanged(nameof(ProviderLabel));
        this.OnPropertyChanged(nameof(OrganizationPageLabel));
        this.OnPropertyChanged(nameof(BudgetLimitsLabel));
        this.OnPropertyChanged(nameof(HasOrganizations));
        if (this.SelectedOrganization != null)
        {
            this.SelectedOrganization = this.Organizations.FirstOrDefault(row => row.OrganizationId == this.SelectedOrganization.OrganizationId);
        }

        this.OnPropertyChanged(nameof(HasAudits));
    }

    private async Task LoadUsageAsync(CancellationToken cancellationToken)
    {
        var response = await this.SessionClient.GetAssistantUsageAsync(DateOnly.FromDateTime(this._appliedUsageFrom),
            DateOnly.FromDateTime(this._appliedUsageTo), channel: string.IsNullOrWhiteSpace(this._appliedChannelFilter) ? null : this._appliedChannelFilter,
            model: string.IsNullOrWhiteSpace(this._appliedModelFilter) ? null : this._appliedModelFilter,
            status: string.IsNullOrWhiteSpace(this._appliedStatusFilter) ? null : this._appliedStatusFilter,
            page: this.UsagePage, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Usage = response;
        if (this.SelectedUsage != null)
        {
            this.SelectedUsage = this.UsageItems.FirstOrDefault(item => item.Id == this.SelectedUsage.Id);
        }
    }

    private Task LoadOrganizationPageAsync(int page) => this.RunAsync(async cancellationToken =>
    {
        this._organizationPage = page;
        await this.LoadOverviewAsync(cancellationToken);
    });

    private void ApplyUsageFilters()
    {
        this._appliedUsageFrom = this.UsageFrom.Date;
        this._appliedUsageTo = this.UsageTo.Date;
        this._appliedChannelFilter = this.SelectedChannel.Code;
        this._appliedModelFilter = this.ModelFilter.Trim();
        this._appliedStatusFilter = this.SelectedStatus.Code;
        this._hasAppliedUsageFilters = true;
    }

    private void ApplySettings(AdminAssistantSettings settings)
    {
        this.IsEnabled = settings.IsEnabled;
        this.WebEnabled = settings.WebEnabled;
        this.TelegramEnabled = settings.TelegramEnabled;
        this.SpeechRepliesEnabled = settings.SpeechRepliesEnabled;
        this.TextModel = settings.TextModel;
        this.SpeechModel = settings.SpeechModel;
        this.VoiceModel = settings.VoiceModel;
        this.VoiceName = settings.VoiceName;
        this.DailyBudgetRub = settings.DailyBudgetRub;
        this.MonthlyBudgetRub = settings.MonthlyBudgetRub;
        this.TextReservationRub = settings.TextReservationRub;
        this.AudioReservationRub = settings.AudioReservationRub;
        this.MaximumOutputTokens = settings.MaximumOutputTokens;
    }

    private bool CanSaveSettings() => this.CanLoad() && this.CanManage && this.Overview != null
        && this.DailyBudgetRub >= 0 && this.DailyBudgetRub <= this.Overview.Limits.MaximumBudgetRub
        && this.MonthlyBudgetRub >= 0 && this.MonthlyBudgetRub <= this.Overview.Limits.MaximumBudgetRub
        && this.TextReservationRub > 0 && this.TextReservationRub <= this.Overview.Limits.MaximumReservationRub
        && this.AudioReservationRub > 0 && this.AudioReservationRub <= this.Overview.Limits.MaximumReservationRub
        && this.MaximumOutputTokens > 0 && this.MaximumOutputTokens <= this.Overview.Limits.MaximumOutputTokensAllowed
        && !string.IsNullOrWhiteSpace(this.TextModel) && !string.IsNullOrWhiteSpace(this.SpeechModel)
        && !string.IsNullOrWhiteSpace(this.VoiceModel) && !string.IsNullOrWhiteSpace(this.VoiceName);
    private bool CanSaveOrganizationAccess() => this.CanLoad() && this.CanManage && this.SelectedOrganization != null
        && this.Overview is { } overview
        && this.OrganizationDailyBudgetRub >= 0 && this.OrganizationDailyBudgetRub <= overview.Limits.MaximumBudgetRub
        && this.OrganizationMonthlyBudgetRub >= 0 && this.OrganizationMonthlyBudgetRub <= overview.Limits.MaximumBudgetRub;
    private bool CanPreviousOrganizationPage() => this.CanLoad() && this.CanManage && this._organizationPage > 1;
    private bool CanNextOrganizationPage() => this.CanLoad() && this.CanManage && this.Overview != null
        && this._organizationPage * this.Overview.PageSize < this.Overview.TotalCount;
    private bool CanPreviousUsagePage() => this.CanLoad() && this.Usage?.Page > 1;
    private bool CanNextUsagePage() => this.CanLoad() && this.Usage != null && this.Usage.Page * this.Usage.PageSize < this.Usage.TotalCount;
    private bool CanSettleUsage() => this.CanLoad() && this.CanSettle && this.HasUnknownSelectedUsage
        && this.SettlementCostRub >= 0 && !string.IsNullOrWhiteSpace(this.SettlementReason);

    private bool CanFilterUsage() => this.CanLoad() && this.CanViewBilling && DateOnly.FromDateTime(this.UsageFrom)
        <= DateOnly.FromDateTime(this.UsageTo);

    [ObservableProperty]
    private CommunicationOption _selectedStatus = new("", "Все статусы");

    public IReadOnlyList<CommunicationOption> StatusOptions { get; } =
    [new("", "Все статусы"), new("completed", "Завершённые"), new("unknown", "Неизвестная стоимость"),
        new("failed", "Ошибки"), new("reserved", "Резерв")];
}
