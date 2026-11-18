using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Обращения, переписка и внутренние заметки поддержки. </summary>
public partial class SupportMessagesViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    private string _appliedSearch = string.Empty;
    private string _appliedStatus = string.Empty;
    private string _appliedSource = string.Empty;
    private bool _isResettingFilters;
    private int _newCount;
    private int _inProgressCount;
    private int _closedCount;
    private int _spamCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<SupportMessageRow> _rows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    private SupportMessageRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReply))]
    [NotifyPropertyChangedFor(nameof(DetailContact))]
    [NotifyPropertyChangedFor(nameof(DetailDate))]
    [NotifyPropertyChangedFor(nameof(DetailStatus))]
    private AdminSupportMessage? _detail;

    [ObservableProperty]
    private IReadOnlyList<SupportEntryRow> _entries = [];

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private string _adminComment = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendReplyCommand))]
    private string _replyText = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private CommunicationOption _selectedStatus = CommunicationOption.Localized("", "native.promoCodes.vse.statusy");

    [ObservableProperty]
    private CommunicationOption _selectedSource = CommunicationOption.Localized("", "native.support.vse.obrashcheniya");

    [ObservableProperty]
    private CommunicationOption _nextStatus = CommunicationOption.Localized("new", "native.support.novoe");

    public IReadOnlyList<CommunicationOption> StatusOptions { get; } =
    [CommunicationOption.Localized("", "native.promoCodes.vse.statusy"), CommunicationOption.Localized("new", "native.support.novye"), CommunicationOption.Localized("in_progress", "native.support.v.rabote"), CommunicationOption.Localized("closed", "native.support.zakrytye"), CommunicationOption.Localized("spam", "native.support.spam")];

    public IReadOnlyList<CommunicationOption> SourceOptions { get; } =
    [CommunicationOption.Localized("", "native.support.vse.obrashcheniya"), CommunicationOption.Localized("users", "native.support.ot.polzovateley"), CommunicationOption.Localized("guests", "native.support.ot.gostey")];

    public IReadOnlyList<CommunicationOption> EditStatusOptions { get; } =
    [CommunicationOption.Localized("new", "native.support.novoe"), CommunicationOption.Localized("in_progress", "native.support.v.rabote"), CommunicationOption.Localized("closed", "native.support.zakryto"), CommunicationOption.Localized("spam", "native.support.spam")];

    public bool CanManage => this.SessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Content.Manage", StringComparer.Ordinal));

    public bool CanReply => this.CanManage && this.Detail != null
        && this.Detail.StatusCode != "spam"
        && (!string.IsNullOrWhiteSpace(this.Detail.UserId) || !string.IsNullOrWhiteSpace(this.Detail.Email));

    public void RefreshAccess() => this.NotifyCommands();

    public bool IsEmpty => !this.IsBusy && this.Rows.Count == 0 && string.IsNullOrWhiteSpace(this.ErrorMessage);

    public override void RefreshLocalizedValues()
    {
        base.RefreshLocalizedValues();
        CommunicationsViewModel.RefreshLocalizedOptions(this.StatusOptions, this.SourceOptions, this.EditStatusOptions);
        foreach (var row in this.Rows)
        {
            row.RefreshLocalizedValues();
        }

        foreach (var entry in this.Entries)
        {
            entry.RefreshLocalizedValues();
        }

        this.UpdateSummary();
    }

    public string DetailContact => this.Detail == null ? string.Empty
        : string.Join(" · ", new[] { this.Detail.UserEmail ?? this.Detail.Email, this.Detail.Name, this.Detail.Phone }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct());

    public string DetailStatus => this.Detail == null
        ? string.Empty
        : AdminDisplayLabels.SupportStatus(this.Detail.StatusCode, this.Detail.StatusName);

    public string DetailDate => UserDisplayFormatting.DateTime(this.Detail?.CreatedAt);

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var result = await this.SessionClient.GetSupportMessagesAsync(
            this._appliedSearch, this._appliedStatus, this._appliedSource, this.Page, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Rows = result.Items.Select(item => new SupportMessageRow(item)).ToArray();
        this.TotalCount = result.TotalCount;
        this.PageSize = Math.Max(1, result.PageSize);
        this.SelectedRow = null;
        this._newCount = result.NewCount;
        this._inProgressCount = result.InProgressCount;
        this._closedCount = result.ClosedCount;
        this._spamCount = result.SpamCount;
        this.UpdateSummary();
    }

    protected override void ApplyFilters()
    {
        this._appliedSearch = this.Search.Trim();
        this._appliedStatus = this.SelectedStatus.Code;
        this._appliedSource = this.SelectedSource.Code;
    }

    protected override void ResetFilters()
    {
        this._isResettingFilters = true;
        this.SelectedStatus = this.StatusOptions[0];
        this.SelectedSource = this.SourceOptions[0];
        this._isResettingFilters = false;
    }

    partial void OnSelectedStatusChanged(CommunicationOption value) => this.ApplySelectedFilters();

    partial void OnSelectedSourceChanged(CommunicationOption value) => this.ApplySelectedFilters();

    private void ApplySelectedFilters()
    {
        if (this._isResettingFilters || !this.ApplyFiltersCommand.CanExecute(null))
        {
            return;
        }

        this.ApplyFiltersCommand.Execute(null);
    }

    protected override void ClearData()
    {
        this.Rows = [];
        this.SelectedRow = null;
        this.Detail = null;
        this.Entries = [];
        this.IsDetailOpen = false;
        this.ReplyText = string.Empty;
        this.AdminComment = string.Empty;
        this.SuccessMessage = string.Empty;
        this.Summary = string.Empty;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.OpenDetailCommand.NotifyCanExecuteChanged();
        this.CloseDetailCommand.NotifyCanExecuteChanged();
        this.SaveDetailsCommand.NotifyCanExecuteChanged();
        this.SendReplyCommand.NotifyCanExecuteChanged();
        this.RefreshDetailCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(CanManage));
        this.OnPropertyChanged(nameof(CanReply));
        this.OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanOpenDetail() => this.CanLoad() && this.SelectedRow != null;
    private bool CanRefreshDetail() => this.CanLoad() && this.Detail != null;
    private bool CanSave() => this.CanRefreshDetail() && this.CanManage;
    private bool CanSendReply() => this.CanSave() && this.CanReply && !string.IsNullOrWhiteSpace(this.ReplyText);

    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync() => this.RunAsync(async cancellationToken =>
    {
        var detail = await this.SessionClient.GetSupportMessageAsync(this.SelectedRow!.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.ApplyDetail(detail);
        this.SuccessMessage = string.Empty;
        this.ReplyText = string.Empty;
        this.IsDetailOpen = true;
    });

    private void ApplyDetail(AdminSupportMessage detail)
    {
        this.Detail = detail;
        this.AdminComment = detail.AdminComment ?? string.Empty;
        this.NextStatus = this.EditStatusOptions.FirstOrDefault(option => option.Code == detail.StatusCode)
            ?? this.EditStatusOptions[0];

        this.Entries = detail.Entries.Count > 0
            ? detail.Entries.Select(entry => new SupportEntryRow(entry)).ToArray()
            : [new SupportEntryRow(new AdminSupportMessageEntry { Text = detail.Text, CreatedAt = detail.CreatedAt })];
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void CloseDetail() => this.IsDetailOpen = false;

    [RelayCommand(CanExecute = nameof(CanRefreshDetail))]
    private Task RefreshDetailAsync() => this.RunAsync(async cancellationToken =>
    {
        var detail = await this.SessionClient.GetSupportMessageAsync(this.Detail!.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.ApplyDetail(detail);
    });

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveDetailsAsync() => this.RunAsync(async cancellationToken =>
    {
        this.SuccessMessage = string.Empty;
        var id = this.Detail!.Id;
        var result = await this.SessionClient.UpdateSupportMessageAsync(id, this.NextStatus.Code, this.AdminComment, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.SuccessMessage = result.Message;
        var detail = await this.SessionClient.GetSupportMessageAsync(id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.ApplyDetail(detail);
        await this.LoadPageAsync(cancellationToken);
    });

    [RelayCommand(CanExecute = nameof(CanSendReply))]
    private Task SendReplyAsync() => this.RunAsync(async cancellationToken =>
    {
        this.SuccessMessage = string.Empty;
        var id = this.Detail!.Id;
        var result = await this.SessionClient.ReplyToSupportMessageAsync(id, this.ReplyText.Trim(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.ReplyText = string.Empty;
        this.SuccessMessage = result.Message;
        var detail = await this.SessionClient.GetSupportMessageAsync(id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.ApplyDetail(detail);
        await this.LoadPageAsync(cancellationToken);
    });

    private void UpdateSummary() => this.Summary = AdminLocalization.Instance.Format(
        "native.support.novye.result.newcount.v.rabote.result.inprogresscount.zakrytye.r",
        this._newCount,
        this._inProgressCount,
        this._closedCount,
        this._spamCount);
}
