using CommunityToolkit.Mvvm.Input;

namespace OpenCRM.Admin.App.ViewModels;

public partial class MainWindowViewModel
{
    private IReadOnlyList<AdminOperationSection> _operationSections = [];

    public IReadOnlyList<AdminOperationSection> VisibleOperationSections =>
        this._operationSections.Where(section => section.Model is not EmailComposeViewModel
            && this.CanViewOperationSection(section)).ToArray();

    public CommunicationsViewModel? CurrentOperation => this._operationSections
        .FirstOrDefault(section => section.Title == this.AdditionalPage && this.CanViewOperationSection(section))?.Model;

    public bool IsOperationsOpen => this.CurrentOperation != null;

    private void InitializeOperations()
    {
        this._operationSections =
        [
            new("Написать письмо", "Admin.Communications.Manage", new EmailComposeViewModel(this._sessionClient)),
            new("Email-рассылки", "Admin.Communications.Manage", new EmailCampaignsViewModel(this._sessionClient)),
            new("История SMS", "Admin.Communications.View", new SmsHistoryViewModel(this._sessionClient)),
            new("Уведомления на сайте", "Admin.Communications.View", new SiteNotificationsViewModel(this._sessionClient)),
            new("Каталог техники", "Admin.Content.Manage", new VehicleCatalogViewModel(this._sessionClient)),
            new("Предложения каталога", "Admin.Content.View", new CatalogSuggestionsViewModel(this._sessionClient)),
            new("Тестовые данные", "Admin.Permissions.Manage", new TestDataViewModel(this._sessionClient)),
            new("Импорт CRM", "Admin.Permissions.Manage", new CrmImportViewModel(this._sessionClient)),
            new("ИИ-помощник", "Admin.Configuration.Manage", new AssistantViewModel(this._sessionClient))
        ];

        foreach (var section in this._operationSections)
        {
            section.Model.SessionExpired += this.OnSectionSessionExpired;
            section.Model.AccessDenied += this.OnSectionAccessDeniedAsync;
            if (section.Model is CatalogSuggestionsViewModel suggestions)
            {
                suggestions.PendingCountUpdated += this.SetPendingCatalogSuggestionsCount;
                suggestions.SuggestionReviewed += () => section.BadgeCount = Math.Max(0, section.BadgeCount - 1);
            }
        }

        var compose = this._operationSections.Single(section => section.Model is EmailComposeViewModel);
        var campaigns = this._operationSections.Single(section => section.Model is EmailCampaignsViewModel);
        ((EmailComposeViewModel)compose.Model).CloseRequested += () => this.ShowOperationCommand.Execute(campaigns);
        ((EmailCampaignsViewModel)campaigns.Model).ComposeRequested += () => this.ShowOperationCommand.Execute(compose);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ShowOperationAsync(AdminOperationSection? section) => section == null ? Task.CompletedTask
        : this.ShowSectionAsync(section.Title, this.CanViewOperationSection(section), section.Model.ActivateAsync);

    private bool CanViewOperation(string title) => this._operationSections
        .Any(section => section.Title == title && this.CanViewOperationSection(section));

    private bool CanViewOperationSection(AdminOperationSection section) => section.Model is AssistantViewModel assistant
        ? assistant.CanManage || assistant.CanViewBilling
        : this.HasPermission(section.Permission);

    private void StopOperations(bool clear)
    {
        foreach (var section in this._operationSections)
        {
            section.Model.Stop(clear);
        }
    }

    private Task DeactivateOperationsAsync(bool clear) =>
        Task.WhenAll(this._operationSections.Select(section => section.Model.DeactivateAsync(clear)));

    private void UpdateOperationAccess()
    {
        foreach (var section in this._operationSections)
        {
            if (section.Model is SiteNotificationsViewModel notifications)
            {
                notifications.RefreshAccess();
            }
            else if (section.Model is CatalogSuggestionsViewModel suggestions)
            {
                suggestions.RefreshAccess();
            }
            else if (section.Model is AssistantViewModel assistant)
            {
                assistant.RefreshAccess();
            }

            if (this.CanViewOperationSection(section))
            {
                continue;
            }

            section.Model.Stop(true);
            section.BadgeCount = 0;
            if (this.AdditionalPage == section.Title)
            {
                this.AdditionalPage = string.Empty;
            }
        }

        this.OnPropertyChanged(nameof(VisibleOperationSections));
        this.OnPropertyChanged(nameof(CurrentOperation));
        this.OnPropertyChanged(nameof(IsOperationsOpen));
    }

    partial void OnAdditionalPageChanged(string value)
    {
        foreach (var section in this._operationSections)
        {
            section.IsSelected = section.Title == value
                || (section.Model is EmailCampaignsViewModel && value == "Написать письмо");
        }

        this.OnPropertyChanged(nameof(CurrentOperation));
        this.OnPropertyChanged(nameof(IsOperationsOpen));
    }
}
