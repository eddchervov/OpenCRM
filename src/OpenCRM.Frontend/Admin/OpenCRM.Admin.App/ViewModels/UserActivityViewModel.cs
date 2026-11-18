using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Загружает и фильтрует журнал действий пользователей. </summary>
public partial class UserActivityViewModel(AdminSessionClient sessionClient) : ObservableObject
{
    private const int PageSize = 100;
    private CancellationTokenSource? _loadCancellation;
    private Task _operation = Task.CompletedTask;
    private long? _nextBeforeId;
    private long? _currentBeforeId;
    private readonly List<long?> _previousCursors = [];
    private bool _isActive;
    private int _requestVersion;
    private string? _fixedUserId;
    private ActivityFilters? _appliedFilters;
    private CancellationTokenSource? _userSearchCancellation;
    private int _userSearchVersion;
    private string? _errorMessageTranslationKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private ActivityUserOption? _selectedUser;

    [ObservableProperty]
    private string _userSearch = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ActivityUserOption> _users = [];

    [ObservableProperty]
    private bool _isSearchingUsers;

    [ObservableProperty]
    private string _search = string.Empty;

    private string _kindCode = "all";
    private string _scenarioCode = "all";

    [ObservableProperty]
    private string _fromDate = string.Empty;

    [ObservableProperty]
    private string _toDate = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled))]
    private bool _enabled = true;

    [ObservableProperty]
    private int _retentionDays;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    private long? _nextCursor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    private IReadOnlyList<UserActivityRow> _items = [];

    public IReadOnlyList<AdminLocalizedOption> KindOptions { get; } =
    [
        new("all", "core.activity.kind.all", "Все типы"),
        new("page", "core.activity.kind.page", "Переход"),
        new("click", "core.activity.kind.click", "Нажатие"),
        new("goal", "core.activity.kind.goal", "Цель"),
        new("operation", "core.activity.kind.operation", "Операция")
    ];

    public IReadOnlyList<AdminLocalizedOption> ScenarioOptions { get; } =
    [
        new("all", "core.activity.scenario.all", "Все"),
        new("detailing", "activity.scenario.detailing", "Детейлинг"),
        new("autoservice", "activity.scenario.autoservice", "Автосервис"),
        new("personal", "activity.scenario.personal", "Личный кабинет"),
        new("account", "activity.scenario.account", "Аккаунт"),
        new("fleet", "activity.scenario.fleet", "Автопарк")
    ];

    public string Kind
    {
        get => this.KindOptions.First(option => option.Code == this._kindCode).Title;
        set => this.SetSelectedOption(value, this.KindOptions, isScenario: false);
    }

    public AdminLocalizedOption SelectedKindOption
    {
        get => this.KindOptions.First(option => option.Code == this._kindCode);
        set => this.Kind = value.Code;
    }

    public string Scenario
    {
        get => this.ScenarioOptions.First(option => option.Code == this._scenarioCode).Title;
        set => this.SetSelectedOption(value, this.ScenarioOptions, isScenario: true);
    }

    public AdminLocalizedOption SelectedScenarioOption
    {
        get => this.ScenarioOptions.First(option => option.Code == this._scenarioCode);
        set => this.Scenario = value.Code;
    }

    public string Title => string.IsNullOrEmpty(this._fixedUserId)
        ? AdminLocalization.Instance["activity.title"]
        : AdminLocalization.Instance["activity.userTitle"];

    public Func<string, CancellationToken, Task<IEnumerable<object>>> PopulateUsers => this.PopulateUsersAsync;

    public bool IsUserFixed => this._fixedUserId != null;

    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    public bool HasItems => this.Items.Count > 0;

    public bool HasMore => this.NextCursor.HasValue;

    public bool CanGoPrevious => this._previousCursors.Count > 0;

    public bool IsDisabled => !this.Enabled;

    public string StatusLabel => this.Enabled
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, AdminLocalization.Instance["activity.pageNumber"], this._previousCursors.Count + 1)
        : AdminLocalization.Instance["core.activity.collectionDisabled"];

    public void RefreshLocalizedValues()
    {
        foreach (var option in this.KindOptions.Concat(this.ScenarioOptions))
        {
            option.RefreshTitle();
        }

        if (this._fixedUserId != null && this.SelectedUser is { } selectedUser)
        {
            var previousLabel = selectedUser.Label;
            var localizedLabel = AdminLocalization.Instance["activity.selectedUser"];
            this.SelectedUser = new ActivityUserOption(selectedUser.Id, localizedLabel);
            this.Users = [this.SelectedUser];
            if (this.UserSearch == previousLabel)
            {
                this.UserSearch = localizedLabel;
            }
        }

        this.Items = this.Items.Select(item => new UserActivityRow(item.Item)).ToArray();
        this.OnPropertyChanged(nameof(this.Kind));
        this.OnPropertyChanged(nameof(this.SelectedKindOption));
        this.OnPropertyChanged(nameof(this.Scenario));
        this.OnPropertyChanged(nameof(this.SelectedScenarioOption));
        this.OnPropertyChanged(nameof(this.Title));
        this.OnPropertyChanged(nameof(this.StatusLabel));
        if (this._errorMessageTranslationKey is { } errorKey)
        {
            this.ErrorMessage = AdminLocalization.Instance[errorKey];
        }
    }

    public event Action? SessionExpired;
    public event Func<Task>? AccessDenied;

    public async Task ActivateAsync(string? userId = null)
    {
        await this.DeactivateAsync();
        this._isActive = true;
        this._fixedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId;
        this.SelectedUser = this._fixedUserId == null ? null : new ActivityUserOption(this._fixedUserId, AdminLocalization.Instance["activity.selectedUser"]);
        this.UserSearch = this.SelectedUser?.Label ?? string.Empty;
        this.Users = this.SelectedUser == null ? [] : [this.SelectedUser];
        this.OnPropertyChanged(nameof(Title));
        this.OnPropertyChanged(nameof(IsUserFixed));
        this._appliedFilters = null;
        this.ClearData();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this._requestVersion++;
        this._userSearchVersion++;
        this._userSearchCancellation?.Cancel();
        this.IsSearchingUsers = false;
        this._loadCancellation?.Cancel();
        this.IsLoading = false;
        if (!clear)
        {
            return;
        }

        this._fixedUserId = null;
        this.SelectedUser = null;
        this.UserSearch = string.Empty;
        this.Users = [];
        this.Search = string.Empty;
        this.Kind = "all";
        this.Scenario = "all";
        this.FromDate = string.Empty;
        this.ToDate = string.Empty;
        this._errorMessageTranslationKey = null;
        this.ErrorMessage = string.Empty;
        this.Enabled = true;
        this.RetentionDays = 0;
        this.ClearData();
        this.OnPropertyChanged(nameof(Title));
        this.OnPropertyChanged(nameof(IsUserFixed));
        this._appliedFilters = null;
    }

    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        if (this._loadCancellation != null)
        {
            await this._loadCancellation.CancelAsync();
            await this._operation;
            this._loadCancellation.Dispose();
            this._loadCancellation = null;
        }
    }

    private async Task<IEnumerable<object>> PopulateUsersAsync(string search, CancellationToken cancellationToken)
    {
        if (!this._isActive || this.IsUserFixed)
        {
            return [];
        }

        this._userSearchCancellation?.Cancel();
        using var searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this._userSearchCancellation = searchCancellation;
        var version = ++this._userSearchVersion;
        this.IsSearchingUsers = true;
        try
        {
            var page = await sessionClient.GetUsersAsync(
                search == this.SelectedUser?.Label ? null : search.Trim(),
                includeDeleted: true,
                pageSize: 30,
                cancellationToken: searchCancellation.Token);
            searchCancellation.Token.ThrowIfCancellationRequested();
            if (!this._isActive || version != this._userSearchVersion)
            {
                return [];
            }

            var users = page.Users.Select(user => new ActivityUserOption(
                user.Id, user.Email ?? user.Phone ?? AdminLocalization.Instance["activity.userMissingEmail"])).ToArray();
            this.Users = this.SelectedUser is { } selected && users.All(user => user.Id != selected.Id)
                ? [selected, .. users]
                : users;

            return users;
        }
        catch (OperationCanceledException) when (searchCancellation.IsCancellationRequested)
        {
            return [];
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or OperationCanceledException)
        {
            if (this._isActive && version == this._userSearchVersion)
            {
                if (exception is ApiException apiException)
                {
                    this._errorMessageTranslationKey = null;
                    this.ErrorMessage = apiException.Message;
                }
                else
                {
                    this.SetLocalizedError("activity.loadUsersFailed");
                }
                if (exception is ApiException { StatusCode: HttpStatusCode.Unauthorized })
                {
                    this.SessionExpired?.Invoke();
                }
                else if (exception is ApiException { StatusCode: HttpStatusCode.Forbidden } && this.AccessDenied != null)
                {
                    await this.AccessDenied.Invoke();
                }
            }

            return [];
        }
        finally
        {
            if (ReferenceEquals(this._userSearchCancellation, searchCancellation))
            {
                this._userSearchCancellation = null;
            }

            if (version == this._userSearchVersion)
            {
                this.IsSearchingUsers = false;
            }
        }
    }

    [RelayCommand]
    private void ClearUser()
    {
        if (this.IsUserFixed)
        {
            return;
        }

        this.SelectedUser = null;
        this.UserSearch = string.Empty;
    }

    private bool CanLoad() => this._isActive && !this.IsLoading;

    private bool CanLoadNext() => this.CanLoad() && this.HasMore;

    private void ClearData()
    {
        this.Items = [];
        this._nextBeforeId = null;
        this._currentBeforeId = null;
        this._previousCursors.Clear();
        this.NextCursor = null;
        this.OnPropertyChanged(nameof(HasItems));
        this.OnPropertyChanged(nameof(CanGoPrevious));
        this.PreviousPageCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAsync(long? beforeId, CancellationToken cancellationToken)
    {
        if (this._fixedUserId == null && this.SelectedUser == null && !string.IsNullOrWhiteSpace(this.UserSearch))
        {
            this.SetLocalizedError("activity.chooseUser");
            return;
        }

        if (!DateTime.TryParseExact(this.FromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var fromDate) && !string.IsNullOrWhiteSpace(this.FromDate))
        {
            this.SetLocalizedError("activity.invalidFromDate");
            return;
        }

        if (!DateTime.TryParseExact(this.ToDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var toDate) && !string.IsNullOrWhiteSpace(this.ToDate))
        {
            this.SetLocalizedError("activity.invalidToDate");
            return;
        }

        if (!string.IsNullOrWhiteSpace(this.FromDate) && !string.IsNullOrWhiteSpace(this.ToDate) && fromDate > toDate)
        {
            this.SetLocalizedError("activity.dateRangeInvalid");
            return;
        }

        this._errorMessageTranslationKey = null;
        this.ErrorMessage = string.Empty;
        var version = this._requestVersion;
        var filters = this.CreateFilters();
        try
        {
            var page = await sessionClient.GetUserActivityAsync(
                filters.UserId,
                filters.Search,
                filters.Kind,
                filters.Scenario,
                from: string.IsNullOrWhiteSpace(this.FromDate) ? null : new DateTimeOffset(DateTime.SpecifyKind(fromDate, DateTimeKind.Utc)),
                to: string.IsNullOrWhiteSpace(this.ToDate) ? null : new DateTimeOffset(DateTime.SpecifyKind(toDate.AddTicks(TimeSpan.TicksPerDay - 1), DateTimeKind.Utc)),
                beforeId: beforeId,
                pageSize: PageSize,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!this._isActive || version != this._requestVersion)
            {
                return;
            }

            this.Enabled = page.Enabled;
            this.RetentionDays = page.RetentionDays;
            this._nextBeforeId = page.NextBeforeId;
            this.NextCursor = page.NextBeforeId;
            this._currentBeforeId = beforeId;
            var rows = page.Items.Take(PageSize).Select(item => new UserActivityRow(item)).ToArray();
            if (this._fixedUserId != null && rows.FirstOrDefault() is { } firstRow)
            {
                this.SelectedUser = new ActivityUserOption(this._fixedUserId, firstRow.User);
                this.Users = [this.SelectedUser];
                this.UserSearch = this.SelectedUser.Label;
            }

            this._appliedFilters = filters;
            this.Items = rows;
            this.OnPropertyChanged(nameof(HasItems));
            this.OnPropertyChanged(nameof(StatusLabel));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (this._isActive && version == this._requestVersion)
        {
            this.SetLocalizedError("activity.timeout");
        }
        catch (ApiException exception) when (this._isActive && version == this._requestVersion)
        {
            this._errorMessageTranslationKey = null;
            this.ErrorMessage = exception.Message;
            if (AdminDisplayLabels.TransportErrorKey(exception) is { } transportKey)
            {
                this.SetLocalizedError(transportKey);
            }

            if (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                this.SessionExpired?.Invoke();
            }
            else if (exception.StatusCode == HttpStatusCode.Forbidden && this.AccessDenied != null)
            {
                await this.AccessDenied.Invoke();
            }
        }
        catch (HttpRequestException) when (this._isActive && version == this._requestVersion)
        {
            this.SetLocalizedError("activity.network");
        }
    }

    private Task RunAsync(long? beforeId)
    {
        if (!this.CanLoad())
        {
            return Task.CompletedTask;
        }

        this._loadCancellation?.Dispose();
        this._loadCancellation = new CancellationTokenSource();
        var cancellationToken = this._loadCancellation.Token;
        var version = ++this._requestVersion;
        this.IsLoading = true;
        this._operation = this.RunOperationAsync(beforeId, cancellationToken, version);
        return this._operation;
    }

    private async Task RunOperationAsync(long? beforeId, CancellationToken cancellationToken, int version)
    {
        try
        {
            await this.LoadAsync(beforeId, cancellationToken);
        }
        finally
        {
            if (version == this._requestVersion)
            {
                this.IsLoading = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task RefreshAsync()
    {
        this.ClearData();
        this._appliedFilters = this.CreateFilters();
        return this.RunAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task ApplyFiltersAsync()
    {
        this.ClearData();
        this._appliedFilters = this.CreateFilters();
        return this.RunAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanLoadNext))]
    private Task NextPageAsync()
    {
        if (!this.HasMore)
        {
            return Task.CompletedTask;
        }

        if (this._appliedFilters != this.CreateFilters())
        {
            return this.ApplyFiltersAsync();
        }

        return this.LoadNextPageAsync();
    }

    private async Task LoadNextPageAsync()
    {
        var previousItems = this.Items;
        var previousCursor = this._currentBeforeId;
        var operation = this.RunAsync(this._nextBeforeId);
        var version = this._requestVersion;
        await operation;
        if (!this._isActive || version != this._requestVersion)
        {
            return;
        }

        if (!ReferenceEquals(previousItems, this.Items))
        {
            this._previousCursors.Add(previousCursor);
            this.OnPropertyChanged(nameof(CanGoPrevious));
            this.PreviousPageCommand.NotifyCanExecuteChanged();
            this.OnPropertyChanged(nameof(StatusLabel));
        }
    }

    private bool CanLoadPrevious() => this.CanLoad() && this.CanGoPrevious;

    [RelayCommand(CanExecute = nameof(CanLoadPrevious))]
    private async Task PreviousPageAsync()
    {
        if (!this.CanGoPrevious)
        {
            return;
        }

        if (this._appliedFilters != this.CreateFilters())
        {
            await this.ApplyFiltersAsync();
            return;
        }

        var previousItems = this.Items;
        var previousCursor = this._previousCursors[^1];
        var operation = this.RunAsync(previousCursor);
        var version = this._requestVersion;
        await operation;
        if (!this._isActive || version != this._requestVersion)
        {
            return;
        }

        if (!ReferenceEquals(previousItems, this.Items))
        {
            this._previousCursors.RemoveAt(this._previousCursors.Count - 1);
            this.OnPropertyChanged(nameof(CanGoPrevious));
            this.PreviousPageCommand.NotifyCanExecuteChanged();
            this.OnPropertyChanged(nameof(StatusLabel));
        }
    }

    private void SetSelectedOption(string value, IReadOnlyList<AdminLocalizedOption> options, bool isScenario)
    {
        var option = options.FirstOrDefault(candidate => candidate.Code == value || candidate.LegacyTitle == value || candidate.Title == value);
        if (option == null)
        {
            return;
        }

        if (isScenario)
        {
            if (this._scenarioCode == option.Code)
            {
                return;
            }

            this._scenarioCode = option.Code;
            this.OnPropertyChanged(nameof(this.Scenario));
            this.OnPropertyChanged(nameof(this.SelectedScenarioOption));
        }
        else
        {
            if (this._kindCode == option.Code)
            {
                return;
            }

            this._kindCode = option.Code;
            this.OnPropertyChanged(nameof(this.Kind));
            this.OnPropertyChanged(nameof(this.SelectedKindOption));
        }

        this.ApplyFiltersCommand.NotifyCanExecuteChanged();
    }

    private void SetLocalizedError(string key)
    {
        this._errorMessageTranslationKey = key;
        this.ErrorMessage = AdminLocalization.Instance[key];
    }

    private ActivityFilters CreateFilters() => new(
        this._fixedUserId ?? this.SelectedUser?.Id ?? string.Empty,
        this.Search.Trim(),
        this._kindCode == "all" ? null : this._kindCode,
        this._scenarioCode == "all" ? null : this._scenarioCode,
        this.FromDate.Trim(),
        this.ToDate.Trim());

    private sealed record ActivityFilters(
        string UserId,
        string Search,
        string? Kind,
        string? Scenario,
        string FromDate,
        string ToDate);

    partial void OnItemsChanged(IReadOnlyList<UserActivityRow> value)
    {
        this.OnPropertyChanged(nameof(HasItems));
        this.OnPropertyChanged(nameof(StatusLabel));
        this.NextPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnNextCursorChanged(long? value) => this.NextPageCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadingChanged(bool value)
    {
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.ApplyFiltersCommand.NotifyCanExecuteChanged();
        this.NextPageCommand.NotifyCanExecuteChanged();
        this.PreviousPageCommand.NotifyCanExecuteChanged();
    }
}

