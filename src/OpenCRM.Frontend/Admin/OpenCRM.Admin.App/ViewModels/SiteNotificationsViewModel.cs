using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public partial class SiteNotificationsViewModel : CommunicationsViewModel
{
    private readonly Dictionary<string, SiteNotificationUserOption> _usersById = new(StringComparer.Ordinal);
    private readonly List<SiteNotificationUserOption> _selectedUsers = [];
    private readonly IReadOnlyList<SiteNotificationAudienceOption> _audienceOptions =
    [
        new("all", "Все пользователи", true), new("admins", "Администраторы"),
        new("organization-owners", "Владельцы организаций")
    ];

    public SiteNotificationsViewModel(AdminSessionClient sessionClient) : base(sessionClient)
    {
        this._sessionClient = sessionClient;
        this.PageSize = 20;
        foreach (var audience in this._audienceOptions)
        {
            audience.SelectionChanged += () => this.OnAudienceSelectionChanged(audience);
        }
    }

    private readonly AdminSessionClient _sessionClient;
    private bool _isUpdatingAudiences;
    private bool _isSendAttempted;
    private bool _isSendCompleted;

    [ObservableProperty]
    private IReadOnlyList<AdminSiteNotification> _templates = [];

    [ObservableProperty]
    private IReadOnlyList<AdminSiteNotification> _historyItems = [];

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private AdminSiteNotification? _editingTemplate;

    [ObservableProperty]
    private string _templateTitle = string.Empty;

    [ObservableProperty]
    private string _templateBody = string.Empty;

    [ObservableProperty]
    private string _sendTitle = string.Empty;

    [ObservableProperty]
    private string _sendBody = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedUsers))]
    private CommunicationOption _recipientScope = new("all", "Все пользователи");

    [ObservableProperty]
    private IReadOnlyList<SiteNotificationUserOption> _foundUsers = [];

    [ObservableProperty]
    private string _userSearch = string.Empty;

    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    private bool _isSendConfirmationOpen;

    [ObservableProperty]
    private string _confirmDeleteTitle = string.Empty;

    [ObservableProperty]
    private AdminSiteNotification? _pendingDelete;

    public IReadOnlyList<CommunicationOption> RecipientScopes { get; } =
    [new("all", "Все пользователи"), new("selected", "Выбранные пользователи")];

    public IReadOnlyList<SiteNotificationAudienceOption> AudienceOptions => this._audienceOptions;

    public IReadOnlyList<SiteNotificationAudienceOption> SelectedAudiences => this._audienceOptions.Where(item => item.IsSelected).ToArray();

    public bool HasSentNotifications => this.TotalCount != 0;

    public bool IsHistoryEmpty => !this.IsBusy && this.TotalCount == 0 && string.IsNullOrWhiteSpace(this.ErrorMessage);

    public IReadOnlyList<SiteNotificationUserOption> SelectedUsers => this._selectedUsers.ToArray();

    public bool IsSendAttempted => this._isSendAttempted;

    public string SendWarning => this._isSendAttempted
        && !this._isSendCompleted
        ? "Запрос отправлен, но результат не подтверждён. Проверьте историю уведомлений перед повторной отправкой."
        : string.Empty;

    public bool IsSelectedUsers => this.RecipientScope.Code == "selected";

    public bool CanManage => this._sessionClient.CurrentProfile?.IsSystem == true
        || this._sessionClient.CurrentProfile?.Permissions.Contains("Admin.Communications.Manage", StringComparer.Ordinal) == true;

    public bool IsEmpty => !this.IsBusy && this.SelectedTabIndex == 2 && this.Templates.Count == 0
        && string.IsNullOrWhiteSpace(this.ErrorMessage);

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var selectedTabIndex = this.SelectedTabIndex;
        await this.LoadTabContentAsync(selectedTabIndex, cancellationToken);
    }

    private async Task LoadTabContentAsync(int tabIndex, CancellationToken cancellationToken)
    {
        if (tabIndex == 1)
        {
            await this.LoadHistoryPageAsync(this.Page, cancellationToken);
            return;
        }

        if (tabIndex == 2)
        {
            var templates = await this.SessionClient.GetSiteNotificationTemplatesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (tabIndex == this.SelectedTabIndex)
            {
                this.Templates = templates;
            }
        }
    }

    protected override void ApplyFilters() { }
    protected override void ResetFilters() { }

    protected override void ClearData()
    {
        this.Templates = [];
        this.HistoryItems = [];
        this.TotalCount = 0;
        this.EditingTemplate = null;
        this.PendingDelete = null;
        this.Notice = string.Empty;
        this.TemplateTitle = string.Empty;
        this.TemplateBody = string.Empty;
        this.SendTitle = string.Empty;
        this.SendBody = string.Empty;
        this.UserSearch = string.Empty;
        this.FoundUsers = [];
        this._selectedUsers.Clear();
        this._usersById.Clear();
        this.OnPropertyChanged(nameof(SelectedUsers));
        if (this._isSendCompleted)
        {
            this._isSendAttempted = false;
            this._isSendCompleted = false;
        }

        this.NotifySendAttemptStateChanged();
        this.RecipientScope = this.RecipientScopes[0];
        this.IsSendConfirmationOpen = false;
        foreach (var audience in this._audienceOptions)
        {
            audience.IsSelected = audience.Code == "all";
        }
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.SearchUsersCommand.NotifyCanExecuteChanged();
        this.SaveTemplateCommand.NotifyCanExecuteChanged();
        this.NotifySendCanExecuteChanged();
        this.DeleteCommand.NotifyCanExecuteChanged();
        this.NextHistoryPageCommand.NotifyCanExecuteChanged();
        this.PreviousHistoryPageCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(IsEmpty));
        this.OnPropertyChanged(nameof(IsHistoryEmpty));
    }

    public void RefreshAccess()
    {
        this.OnPropertyChanged(nameof(CanManage));
        this.SaveTemplateCommand.NotifyCanExecuteChanged();
        this.SearchUsersCommand.NotifyCanExecuteChanged();
        this.NotifySendCanExecuteChanged();
        this.DeleteCommand.NotifyCanExecuteChanged();
    }

    public Task LoadSelectedTabAsync() => this.CanLoad()
        ? this.RunAsync(this.LoadPageAsync)
        : this.LoadLatestTabAfterCurrentOperationAsync();

    private async Task LoadLatestTabAfterCurrentOperationAsync()
    {
        if (!this.IsBusy)
        {
            return;
        }

        var selectedTabIndex = this.SelectedTabIndex;
        await this.CurrentOperation;
        if (!this.CanLoad() || selectedTabIndex != this.SelectedTabIndex)
        {
            return;
        }

        await this.RunAsync(cancellationToken => this.LoadTabContentAsync(selectedTabIndex, cancellationToken));
    }

    [RelayCommand]
    private void EditTemplate(AdminSiteNotification template)
    {
        this.ErrorMessage = string.Empty;
        this.Notice = string.Empty;
        this.EditingTemplate = template;
        this.TemplateTitle = template.Title;
        this.TemplateBody = template.Body;
    }

    [RelayCommand]
    private void CancelTemplateEdit() => this.EditingTemplate = null;

    private bool CanSaveTemplate() => this.CanManage && this.CanLoad() && this.EditingTemplate != null
        && !string.IsNullOrWhiteSpace(this.TemplateTitle) && !string.IsNullOrWhiteSpace(this.TemplateBody);

    [RelayCommand(CanExecute = nameof(CanSaveTemplate))]
    private Task SaveTemplateAsync() => this.RunAsync(async cancellationToken =>
    {
        var template = this.EditingTemplate!;
        await this.SessionClient.UpdateSiteNotificationTemplateAsync(template.Id, new AdminSiteNotificationTemplateRequest
        {
            Title = this.TemplateTitle.Trim(),
            Body = this.TemplateBody.Trim()
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.EditingTemplate = null;
        this.Notice = "Шаблон сохранён.";
        await this.LoadPageAsync(cancellationToken);
    });

    private bool CanSearchUsers() => this.CanManage && this.CanLoad() && !string.IsNullOrWhiteSpace(this.UserSearch);

    [RelayCommand(CanExecute = nameof(CanSearchUsers))]
    private Task SearchUsersAsync() => this.RunAsync(async cancellationToken =>
    {
        var users = await this.SessionClient.GetSiteNotificationUsersAsync(this.UserSearch.Trim(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.FoundUsers = users.Select(user =>
        {
            if (!this._usersById.TryGetValue(user.Id, out var option))
            {
                option = new SiteNotificationUserOption(user);
                option.SelectionChanged += this.OnUserSelectionChanged;
                this._usersById.Add(user.Id, option);
            }

            return option;
        }).ToArray();
    });

    private bool CanSend() => this.CanManage && this.CanLoad() && !string.IsNullOrWhiteSpace(this.SendTitle)
        && !string.IsNullOrWhiteSpace(this.SendBody) && this.SelectedAudiences.Count != 0
        && !this._isSendAttempted
        && (!this.IsSelectedUsers || this.SelectedUsers.Count is > 0 and <= 500);

    private bool CanConfirmSend() => this.IsSendConfirmationOpen && this.CanSend();

    private bool CanCheckHistoryAndStartNew() => this.CanManage && this.CanLoad() && this._isSendAttempted;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void RequestSend() => this.IsSendConfirmationOpen = true;

    [RelayCommand]
    private void CancelSend() => this.IsSendConfirmationOpen = false;

    [RelayCommand]
    private void RemoveSelectedUser(SiteNotificationUserOption user) => user.IsSelected = false;

    [RelayCommand(CanExecute = nameof(CanConfirmSend))]
    private Task ConfirmSendAsync()
    {
        if (!this.CanConfirmSend())
        {
            return Task.CompletedTask;
        }

        return this.SendAsync();
    }

    [RelayCommand(CanExecute = nameof(CanCheckHistoryAndStartNew))]
    private Task CheckHistoryAndStartNewAsync() => this.RunAsync(async cancellationToken =>
    {
        var history = await this.SessionClient.GetSiteNotificationHistoryAsync(this.Page, this.PageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.HistoryItems = history.Items;
        this.TotalCount = history.TotalCount;
        this.Page = history.Page;
        this.PageSize = history.PageSize;
        this.NotifyHistoryStateChanged();
        this._isSendAttempted = false;
        this._isSendCompleted = false;
        this.SendTitle = string.Empty;
        this.SendBody = string.Empty;
        this.IsSendConfirmationOpen = false;
        this.Notice = "История уведомлений обновлена. Можно начать новое уведомление.";
        this.NotifySendAttemptStateChanged();
    });

    private Task SendAsync() => this.RunAsync(async cancellationToken =>
    {
        this._isSendAttempted = true;
        this.NotifySendAttemptStateChanged();

        await this.SessionClient.SendSiteNotificationAsync(new AdminSiteNotificationSendRequest
        {
            Title = this.SendTitle.Trim(),
            Body = this.SendBody.Trim(),
            RecipientScope = this.RecipientScope.Code,
            UserIds = this.IsSelectedUsers ? this.SelectedUsers.Select(user => user.Id).ToArray() : [],
            Audiences = this.SelectedAudiences.Select(audience => audience.Code).ToArray()
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this._isSendCompleted = true;
        this._isSendAttempted = false;
        this.NotifySendAttemptStateChanged();
        this.SendTitle = string.Empty;
        this.SendBody = string.Empty;
        this.IsSendConfirmationOpen = false;
        foreach (var user in this._selectedUsers.ToArray())
        {
            user.IsSelected = false;
        }

        this.Notice = "Уведомление отправлено.";
        if (this.SelectedTabIndex == 1)
        {
            await this.LoadHistoryPageAsync(this.Page, cancellationToken);
        }
    });

    [RelayCommand]
    private void RequestDelete(AdminSiteNotification notification) => this.PendingDelete = notification;

    [RelayCommand]
    private void CancelDelete() => this.PendingDelete = null;

    private bool CanDelete() => this.CanManage && this.CanLoad() && this.PendingDelete != null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => this.RunAsync(async cancellationToken =>
    {
        await this.SessionClient.DeleteSiteNotificationAsync(this.PendingDelete!.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.PendingDelete = null;
        this.Notice = "Уведомление удалено.";
        await this.LoadHistoryPageAsync(this.Page, cancellationToken);
    });

    private async Task LoadHistoryPageAsync(int page, CancellationToken cancellationToken)
    {
        var result = await this.SessionClient.GetSiteNotificationHistoryAsync(page, this.PageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (this.SelectedTabIndex != 1)
        {
            return;
        }

        this.HistoryItems = result.Items;
        this.TotalCount = result.TotalCount;
        this.Page = result.Page;
        this.PageSize = result.PageSize;
        this.NotifyHistoryStateChanged();
    }

    private bool CanPreviousHistoryPage() => this.CanLoad() && this.SelectedTabIndex == 1 && this.Page > 1;

    [RelayCommand(CanExecute = nameof(CanPreviousHistoryPage))]
    private Task PreviousHistoryPageAsync() => this.ChangeHistoryPageAsync(this.Page - 1);

    private bool CanNextHistoryPage() => this.CanLoad() && this.SelectedTabIndex == 1
        && (long)this.Page * this.PageSize < this.TotalCount;

    [RelayCommand(CanExecute = nameof(CanNextHistoryPage))]
    private Task NextHistoryPageAsync() => this.ChangeHistoryPageAsync(this.Page + 1);

    private Task ChangeHistoryPageAsync(int page)
    {
        this.Page = page;
        this.HistoryItems = [];

        return this.RunAsync(cancellationToken => this.LoadHistoryPageAsync(page, cancellationToken));
    }

    partial void OnTemplatesChanged(IReadOnlyList<AdminSiteNotification> value) => this.OnPropertyChanged(nameof(IsEmpty));
    partial void OnHistoryItemsChanged(IReadOnlyList<AdminSiteNotification> value) => this.OnPropertyChanged(nameof(IsHistoryEmpty));
    partial void OnSelectedTabIndexChanged(int value)
    {
        this.OnPropertyChanged(nameof(IsEmpty));
        this.Page = 1;
        this.HistoryItems = [];
        this.TotalCount = 0;
        this.NotifyHistoryStateChanged();
        this.NextHistoryPageCommand.NotifyCanExecuteChanged();
        this.PreviousHistoryPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditingTemplateChanged(AdminSiteNotification? value) => this.SaveTemplateCommand.NotifyCanExecuteChanged();
    partial void OnTemplateTitleChanged(string value) => this.SaveTemplateCommand.NotifyCanExecuteChanged();
    partial void OnTemplateBodyChanged(string value) => this.SaveTemplateCommand.NotifyCanExecuteChanged();
    partial void OnSendTitleChanged(string value) => this.NotifySendCanExecuteChanged();
    partial void OnSendBodyChanged(string value) => this.NotifySendCanExecuteChanged();
    partial void OnRecipientScopeChanged(CommunicationOption? oldValue, CommunicationOption newValue)
    {
        this.OnPropertyChanged(nameof(IsSelectedUsers));
        this.NotifySendCanExecuteChanged();
    }
    partial void OnUserSearchChanged(string value) => this.SearchUsersCommand.NotifyCanExecuteChanged();
    partial void OnPendingDeleteChanged(AdminSiteNotification? value) => this.DeleteCommand.NotifyCanExecuteChanged();
    partial void OnIsSendConfirmationOpenChanged(bool value) => this.ConfirmSendCommand.NotifyCanExecuteChanged();

    private void OnAudienceSelectionChanged(SiteNotificationAudienceOption selected)
    {
        if (this._isUpdatingAudiences)
        {
            return;
        }

        this._isUpdatingAudiences = true;
        try
        {
            if (selected.IsSelected)
            {
                if (selected.Code == "all")
                {
                    foreach (var audience in this._audienceOptions.Skip(1))
                    {
                        audience.IsSelected = false;
                    }
                }
                else
                {
                    this._audienceOptions[0].IsSelected = false;
                }
            }
        }
        finally
        {
            this._isUpdatingAudiences = false;
        }

        this.NotifySendCanExecuteChanged();
    }

    private void NotifySendCanExecuteChanged()
    {
        this.RequestSendCommand.NotifyCanExecuteChanged();
        this.ConfirmSendCommand.NotifyCanExecuteChanged();
        this.CheckHistoryAndStartNewCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(SelectedUsers));
    }

    private void NotifySendAttemptStateChanged()
    {
        this.OnPropertyChanged(nameof(IsSendAttempted));
        this.OnPropertyChanged(nameof(SendWarning));
        this.NotifySendCanExecuteChanged();
    }

    private void NotifyHistoryStateChanged()
    {
        this.OnPropertyChanged(nameof(HasSentNotifications));
        this.OnPropertyChanged(nameof(IsHistoryEmpty));
        this.NextHistoryPageCommand.NotifyCanExecuteChanged();
        this.PreviousHistoryPageCommand.NotifyCanExecuteChanged();
    }

    private void OnUserSelectionChanged(SiteNotificationUserOption user)
    {
        if (user.IsSelected && !this._selectedUsers.Contains(user))
        {
            this._selectedUsers.Add(user);
        }
        else if (!user.IsSelected)
        {
            this._selectedUsers.Remove(user);
        }

        this.NotifySendCanExecuteChanged();
    }
}
