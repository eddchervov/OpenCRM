using System.Net;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public partial class EmailComposeViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleRecipient))]
    [NotifyPropertyChangedFor(nameof(RecipientDetailsVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmailTarget))]
    [NotifyPropertyChangedFor(nameof(IsUserTarget))]
    [NotifyPropertyChangedFor(nameof(IsOrganizationTypeTarget))]
    [NotifyPropertyChangedFor(nameof(ShowTargetFilters))]
    [NotifyPropertyChangedFor(nameof(ScheduleButtonLabel))]
    [NotifyPropertyChangedFor(nameof(CanSchedule))]
    private CommunicationOption _targetMode = new("user", "Пользователь");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSchedule))]
    private IReadOnlyList<CommunicationOption> _targetModes =
    [
        new("email", "Указать email"), new("user", "Выбрать пользователя"),
        new("organizationType", "Тип организации"), new("all", "Все пользователи")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComposeData))]
    private IReadOnlyList<AdminEmailUser> _users = [];

    [ObservableProperty]
    private IReadOnlyList<CommunicationOption> _organizationTypes = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSchedule))]
    private AdminEmailRecipientsPreview? _recipients;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHtml))]
    private string _subject = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHtml))]
    [NotifyPropertyChangedFor(nameof(HasBody))]
    private string _body = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHtml))]
    [NotifyPropertyChangedFor(nameof(IsPlainText))]
    private CommunicationOption _bodyFormat = new("text", "Обычный текст");

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private AdminEmailUser? _selectedUser;

    [ObservableProperty]
    private CommunicationOption? _selectedOrganizationType;

    [ObservableProperty]
    private bool _onlyConfirmedEmail;

    [ObservableProperty]
    private bool _onlyAdvertisingConsent;

    [ObservableProperty]
    private int _intervalSeconds = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleButtonLabel))]
    private int _startDelayMinutes;

    [ObservableProperty]
    private int _scheduledCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleWarning))]
    private bool _isScheduled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSchedule))]
    [NotifyPropertyChangedFor(nameof(ScheduleWarning))]
    private bool _isScheduleAttempted;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isPreviewing;

    public IReadOnlyList<CommunicationOption> BodyFormats { get; } =
    [new("text", "Обычный текст"), new("html", "HTML")];

    public bool IsSingleRecipient => this.TargetMode.Code is "email" or "user";
    public bool RecipientDetailsVisible => this.TargetMode.Code is "email" or "user" or "organizationType";
    public bool IsEmailTarget => this.TargetMode.Code == "email";
    public bool IsUserTarget => this.TargetMode.Code == "user";
    public bool IsOrganizationTypeTarget => this.TargetMode.Code == "organizationType";
    public bool ShowTargetFilters => this.TargetMode.Code != "email";
    public bool HasComposeData => this.Users.Count != 0 || this.OrganizationTypes.Count != 0;
    public bool HasBody => !string.IsNullOrWhiteSpace(this.Body);
    public bool IsPlainText => this.BodyFormat.Code == "text";
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);
    public string ScheduleButtonLabel => this.StartDelayMinutes > 0 ? "Запланировать" : "Поставить в очередь";
    public string ScheduleWarning => this.IsScheduleAttempted && !this.IsScheduled
        ? "Запрос уже отправлен, но сервер не подтвердил результат. Проверьте список рассылок перед повторной отправкой."
        : string.Empty;
    public bool CanSchedule => this.CanLoad() && !this.IsScheduleAttempted && this.Recipients?.TotalCount > 0 &&
        !string.IsNullOrWhiteSpace(this.Subject) && !string.IsNullOrWhiteSpace(this.Body) &&
        (this.TargetMode.Code != "email" || !string.IsNullOrWhiteSpace(this.Email)) &&
        (this.TargetMode.Code != "user" || this.SelectedUser != null) &&
        (this.TargetMode.Code != "organizationType" || this.SelectedOrganizationType != null);

    public string PreviewHtml => EmailPreviewDocument.Create(
        this.BodyFormat.Code == "html" ? this.Body : this.CreateTextHtml(this.Body), this.Body);

    public Func<string, CancellationToken, Task<IEnumerable<object>>> PopulateUsers => this.PopulateUsersAsync;

    public IReadOnlyList<CommunicationOption> TargetModeOptions => this.TargetModes;

    public string RecipientCountLabel => this.Recipients == null
        ? "Сначала проверьте список адресатов."
        : $"Получателей: {this.Recipients.TotalCount:N0}";

    public event Action? CloseRequested;

    [RelayCommand]
    private void Close() => this.CloseRequested?.Invoke();

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var data = await this.SessionClient.GetEmailComposeDataAsync(null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Users = data.Users;
        this.OrganizationTypes = data.OrganizationTypes.Select(item => new CommunicationOption(item.Code, item.Name)).ToArray();
    }

    protected override void ApplyFilters() { }
    protected override void ResetFilters() { }

    protected override void ClearData()
    {
        this.Users = [];
        this.OrganizationTypes = [];
        this.Recipients = null;
        this.TargetMode = this.TargetModes[1];
        this.Email = string.Empty;
        this.SelectedUser = null;
        this.SelectedOrganizationType = null;
        this.OnlyConfirmedEmail = true;
        this.OnlyAdvertisingConsent = false;
        this.Subject = string.Empty;
        this.Body = string.Empty;
        this.BodyFormat = this.BodyFormats[0];
        this.IntervalSeconds = 60;
        this.StartDelayMinutes = 0;
        this.IsScheduleAttempted = false;
        this.IsScheduled = false;
        this.ScheduledCount = 0;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.PreviewRecipientsCommand.NotifyCanExecuteChanged();
        this.ScheduleCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(CanSchedule));
    }

    private AdminEmailRecipientRequest CreateRecipientRequest() => new()
    {
        TargetMode = this.TargetMode.Code,
        Email = this.TargetMode.Code == "email" ? this.Email.Trim() : null,
        UserId = this.TargetMode.Code == "user" ? this.SelectedUser?.Id : null,
        OrganizationTypeCode = this.TargetMode.Code == "organizationType" ? this.SelectedOrganizationType?.Code : null,
        OnlyConfirmedEmail = this.TargetMode.Code != "email" && this.OnlyConfirmedEmail,
        OnlyAdvertisingConsent = this.TargetMode.Code != "email" && this.OnlyAdvertisingConsent
    };

    private string CreateTextHtml(string value) =>
        string.Join(string.Empty, value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.None)
            .Select(paragraph => $"<p style=\"margin:0 0 16px;white-space:pre-wrap\">{WebUtility.HtmlEncode(paragraph)}</p>"));

    private int _userSearchVersion;

    private async Task<IEnumerable<object>> PopulateUsersAsync(string search, CancellationToken cancellationToken)
    {
        if (!this.IsActive)
        {
            return [];
        }

        var version = Interlocked.Increment(ref this._userSearchVersion);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, this.LifetimeToken);
        this.IsSearching = true;
        this.ErrorMessage = string.Empty;
        try
        {
            var data = await this.SessionClient.GetEmailComposeDataAsync(search.Trim(), linkedCancellation.Token);
            linkedCancellation.Token.ThrowIfCancellationRequested();
            if (!this.IsCurrentUserSearch(version, linkedCancellation.Token))
            {
                return [];
            }

            var users = data.Users;
            if (this.SelectedUser is { } selectedUser && users.All(user => user.Id != selectedUser.Id))
            {
                users = [selectedUser, .. users];
            }

            this.Users = users;
            this.OrganizationTypes = data.OrganizationTypes
                .Select(item => new CommunicationOption(item.Code, item.Name))
                .ToArray();

            return data.Users.Cast<object>().ToArray();
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            return [];
        }
        catch (ApiException exception)
        {
            if (this.IsCurrentUserSearch(version, linkedCancellation.Token))
            {
                await this.HandleApiErrorAsync(exception);
            }

            return [];
        }
        catch (HttpRequestException exception)
        {
            if (this.IsCurrentUserSearch(version, linkedCancellation.Token))
            {
                await this.HandleApiErrorAsync(exception);
            }

            return [];
        }
        catch (OperationCanceledException) when (this.IsCurrentUserSearch(version, linkedCancellation.Token))
        {
            await this.HandleApiErrorAsync(new TimeoutException());
            return [];
        }
        finally
        {
            if (version == Volatile.Read(ref this._userSearchVersion))
            {
                this.IsSearching = false;
            }
        }
    }

    private bool IsCurrentUserSearch(int version, CancellationToken cancellationToken) =>
        version == Volatile.Read(ref this._userSearchVersion) && this.IsActive && !cancellationToken.IsCancellationRequested;

    private bool CanPreviewRecipients() => this.CanLoad() && !this.IsPreviewing;

    [RelayCommand(CanExecute = nameof(CanPreviewRecipients))]
    private Task PreviewRecipientsAsync() => this.RunAsync(async cancellationToken =>
    {
        var request = this.CreateRecipientRequest();
        this.IsPreviewing = true;
        this.Recipients = null;
        try
        {
            var result = await this.SessionClient.PreviewEmailRecipientsAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (this.IsSameRecipientRequest(request))
            {
                this.Recipients = result;
            }
        }
        finally
        {
            this.IsPreviewing = false;
        }
    });

    [RelayCommand(CanExecute = nameof(CanSchedule))]
    private Task ScheduleAsync() => this.RunAsync(async cancellationToken =>
    {
        this.IsScheduleAttempted = true;
        var result = await this.SessionClient.ScheduleEmailCampaignAsync(new AdminEmailCampaignScheduleRequest
        {
            TargetMode = this.TargetMode.Code,
            Email = this.TargetMode.Code == "email" ? this.Email.Trim() : null,
            UserId = this.TargetMode.Code == "user" ? this.SelectedUser?.Id : null,
            OrganizationTypeCode = this.TargetMode.Code == "organizationType" ? this.SelectedOrganizationType?.Code : null,
            OnlyConfirmedEmail = this.TargetMode.Code != "email" && this.OnlyConfirmedEmail,
            OnlyAdvertisingConsent = this.TargetMode.Code != "email" && this.OnlyAdvertisingConsent,
            Subject = this.Subject.Trim(),
            HtmlBody = this.BodyFormat.Code == "html" ? this.Body : this.CreateTextHtml(this.Body),
            IntervalSeconds = this.IsSingleRecipient ? 60 : this.IntervalSeconds,
            StartDelayMinutes = this.StartDelayMinutes
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.ScheduledCount = result.ScheduledCount;
        this.IsScheduled = true;
        this.Recipients = null;
        this.CloseRequested?.Invoke();
    });

    private bool IsSameRecipientRequest(AdminEmailRecipientRequest request) =>
        request.TargetMode == this.TargetMode.Code
        && request.Email == (this.TargetMode.Code == "email" ? this.Email.Trim() : null)
        && request.UserId == (this.TargetMode.Code == "user" ? this.SelectedUser?.Id : null)
        && request.OrganizationTypeCode == (this.TargetMode.Code == "organizationType" ? this.SelectedOrganizationType?.Code : null)
        && request.OnlyConfirmedEmail == (this.TargetMode.Code != "email" && this.OnlyConfirmedEmail)
        && request.OnlyAdvertisingConsent == (this.TargetMode.Code != "email" && this.OnlyAdvertisingConsent);

    partial void OnTargetModeChanged(CommunicationOption value)
    {
        this.Recipients = null;
        this.ScheduleCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(ScheduleButtonLabel));
    }
    partial void OnSelectedUserChanged(AdminEmailUser? value)
    {
        this.Recipients = null;
        this.ScheduleCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedOrganizationTypeChanged(CommunicationOption? value)
    {
        this.Recipients = null;
        this.ScheduleCommand.NotifyCanExecuteChanged();
    }

    partial void OnEmailChanged(string value)
    {
        this.Recipients = null;
        this.ScheduleCommand.NotifyCanExecuteChanged();
    }
    partial void OnOnlyConfirmedEmailChanged(bool value) => this.Recipients = null;
    partial void OnOnlyAdvertisingConsentChanged(bool value) => this.Recipients = null;
    partial void OnSubjectChanged(string value) => this.ScheduleCommand.NotifyCanExecuteChanged();
    partial void OnBodyChanged(string value) => this.ScheduleCommand.NotifyCanExecuteChanged();
    partial void OnStartDelayMinutesChanged(int value) => this.OnPropertyChanged(nameof(ScheduleButtonLabel));
    partial void OnIsScheduleAttemptedChanged(bool value) => this.ScheduleCommand.NotifyCanExecuteChanged();
    partial void OnRecipientsChanged(AdminEmailRecipientsPreview? value)
    {
        this.ScheduleCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(RecipientCountLabel));
    }
}
