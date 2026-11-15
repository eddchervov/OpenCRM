using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель представления раздела пользователей. </summary>
public partial class UsersViewModel : ObservableObject
{
    public event Action<string>? UserActivityRequested;
    /// <summary> Отмечает просмотр только после успешной загрузки списка. </summary>
    public Func<Func<Task>, CancellationToken, Task>? TrackListReadAsync { get; set; }

    /// <summary> Клиент административной сессии. </summary>
    private readonly AdminSessionClient _sessionClient;

    /// <summary> Признак активности экрана. </summary>
    private bool _isActive;

    /// <summary> Доступ к разделу биллинга. </summary>
    private bool _canViewBilling;

    /// <summary> Применённый поисковый запрос. </summary>
    private string _appliedSearch = string.Empty;

    /// <summary> Применённый фильтр учёта удалённых пользователей. </summary>
    private bool _appliedIncludeDeleted;

    /// <summary> Применённая сортировка списка. </summary>
    private AdminUsersSort _appliedSort = AdminUsersSort.ByDateDesc;

    /// <summary> Применённый размер страницы. </summary>
    private int _appliedPageSize = 50;

    /// <summary> Текущая страница списка пользователей. </summary>
    private AdminUsersResponse? _list;

    /// <summary> Обзорные данные выбранного пользователя. </summary>
    private AdminUserOverview? _overview;

    /// <summary> Идентификатор пользователя открытой карточки. </summary>
    private string? _cardUserId;

    /// <summary> Поисковый запрос. </summary>
    [ObservableProperty]
    private string _search = string.Empty;

    /// <summary> Признак учёта удалённых пользователей. </summary>
    [ObservableProperty]
    private bool _includeDeleted;

    /// <summary> Размер страницы. </summary>
    [ObservableProperty]
    private int _pageSize = 50;

    /// <summary> Признак загрузки списка. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SortCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    /// <summary> Признак загрузки карточки пользователя. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCardCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOrganizationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCardCommand))]
    private bool _isLoadingCard;

    /// <summary> Признак открытой карточки пользователя. </summary>
    [ObservableProperty]
    private bool _isCardOpen;

    /// <summary> Текст ошибки списка. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    /// <summary> Текст ошибки карточки. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardError))]
    private string _cardErrorMessage = string.Empty;

    /// <summary> Строки списка пользователей. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<AdminUserRow> _rows = [];

    /// <summary> Выбранный пользователь. </summary>
    [ObservableProperty]
    private AdminUserRow? _selectedUser;

    /// <summary> Модель карточки выбранного пользователя. </summary>
    [ObservableProperty]
    private UserCardViewModel? _card;

    /// <summary> Текущий номер страницы. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _page = 1;

    /// <summary> Общее количество пользователей. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalCount;

    /// <summary> Создаёт список и управление выбранным пользователем. </summary>
    public UsersViewModel(AdminSessionClient sessionClient)
    {
        this._sessionClient = sessionClient;
        this.Management = new UserManagementViewModel(sessionClient);
        this.Management.SessionExpired += () => this.SessionExpired?.Invoke();
        this.Management.AccessDenied += async () =>
        {
            if (this.AccessDenied != null)
            {
                await this.AccessDenied.Invoke();
            }
        };
        this.Management.UserChanged += this.RefreshAfterManagementAsync;
        this.Management.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(UserManagementViewModel.IsBusy))
            {
                this.NotifyCommands();
            }
        };
    }

    /// <summary> Подтверждённые действия над пользователем и его подписками. </summary>
    public UserManagementViewModel Management { get; }

    /// <summary> Доступные размеры страницы списка. </summary>
    public IReadOnlyList<int> PageSizes { get; } = [25, 50, 100];

    /// <summary> Заголовок колонки с электронной почтой. </summary>
    public string EmailHeader => this.SortLabel("Пользователь", AdminUsersSort.ByEmailAsc);

    /// <summary> Заголовок колонки с датой регистрации. </summary>
    public string RegistrationHeader => this.SortLabel("Регистрация", AdminUsersSort.ByDateAsc);

    /// <summary> Заголовок колонки с количеством техники. </summary>
    public string VehiclesHeader => this.SortLabel("Техника", AdminUsersSort.ByVehiclesAsc);

    /// <summary> Заголовок колонки с количеством обслуживаний. </summary>
    public string MaintenanceHeader => this.SortLabel("ТО", AdminUsersSort.ByMaintenanceAsc);

    /// <summary> Заголовок колонки с организациями. </summary>
    public string OrganizationsHeader => this.SortLabel("Организации", AdminUsersSort.ByOrganizationsAsc);

    /// <summary> Событие истечения сессии. </summary>
    public event Action? SessionExpired;

    /// <summary> Событие отказа в доступе. </summary>
    public event Func<Task>? AccessDenied;

    /// <summary> Признак наличия ошибки загрузки списка. </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    /// <summary> Признак ошибки при загрузке карточки. </summary>
    public bool HasCardError => !string.IsNullOrWhiteSpace(this.CardErrorMessage);

    /// <summary> Признак отсутствия пользователей в списке. </summary>
    public bool IsEmpty => this._list != null && !this.IsLoading && this.Rows.Count == 0 && !this.HasError;

    /// <summary> Подпись общего количества пользователей. </summary>
    public string TotalLabel => $"Найдено: {this.TotalCount:N0}";

    /// <summary> Подпись номера страницы. </summary>
    public string PageLabel => $"{this.Page} / {Math.Max(1, (int)Math.Ceiling((double)this.TotalCount / this._appliedPageSize))}";

    /// <summary> Активирует модель с учётом доступа к биллингу. </summary>
    public async Task ActivateAsync(bool canViewBilling)
    {
        this._isActive = true;
        this.IsCardOpen = false;
        this.SetBillingAccess(canViewBilling);
        this.NotifyCommands();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Обновляет доступ текущей сессии к биллингу. </summary>
    public void SetBillingAccess(bool canViewBilling)
    {
        var accessChanged = this._canViewBilling != canViewBilling;
        this._canViewBilling = canViewBilling;
        this.Management.RefreshAccess();
        if (this._list != null)
        {
            var selectedId = this.SelectedUser?.Id;
            this.Rows = this._list.Users.Select(user => new AdminUserRow(user)).ToArray();
            this.SelectedUser = this.Rows.FirstOrDefault(user => user.Id == selectedId);
        }

        if (this._overview != null)
        {
            if (accessChanged && this._isActive && this.IsCardOpen)
            {
                this.Management.SetOverview(this._overview, canViewBilling);
            }

            this.ApplyCard(this._overview);
        }
    }

    /// <summary> Останавливает работу модели, при необходимости очищая данные. </summary>
    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this.Management.Stop();
        this.RefreshCommand.Cancel();
        this.OpenCardCommand.Cancel();
        this.RefreshCardCommand.Cancel();
        this.NotifyCommands();
        if (!clear)
        {
            return;
        }

        this._list = null;
        this._overview = null;
        this._cardUserId = null;
        this.Rows = [];
        this.SelectedUser = null;
        this.Card = null;
        this.IsCardOpen = false;
        this.ErrorMessage = string.Empty;
        this.CardErrorMessage = string.Empty;
        this.Search = string.Empty;
        this.IncludeDeleted = false;
        this.PageSize = 50;
        this._appliedSearch = string.Empty;
        this._appliedIncludeDeleted = false;
        this._appliedSort = AdminUsersSort.ByDateDesc;
        this._appliedPageSize = 50;
        this.Page = 1;
        this.TotalCount = 0;
        this.OnPropertyChanged(nameof(IsEmpty));
        this.NotifySortHeaders();
    }

    /// <summary> Деактивирует модель, при необходимости очищая данные. </summary>
    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        await Task.WhenAll(new[] { this.RefreshCommand.ExecutionTask, this.OpenCardCommand.ExecutionTask,
            this.RefreshCardCommand.ExecutionTask, this.OpenOrganizationsCommand.ExecutionTask,
            this.Management.DeactivateAsync() }.OfType<Task>());
    }

    /// <summary> Уведомляет команды об изменении состояния. </summary>
    private void NotifyCommands()
    {
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.ApplyFiltersCommand.NotifyCanExecuteChanged();
        this.SortCommand.NotifyCanExecuteChanged();
        this.ResetFiltersCommand.NotifyCanExecuteChanged();
        this.OpenOrganizationsCommand.NotifyCanExecuteChanged();
        this.OpenCardCommand.NotifyCanExecuteChanged();
        this.RefreshCardCommand.NotifyCanExecuteChanged();
        this.PreviousPageCommand.NotifyCanExecuteChanged();
        this.NextPageCommand.NotifyCanExecuteChanged();
    }

    /// <summary> Проверяет возможность загрузки списка. </summary>
    private bool CanLoad() => this._isActive && !this.IsLoading && !this.Management.IsBusy;

    /// <summary> Проверяет возможность открытия карточки пользователя. </summary>
    private bool CanOpenCard(AdminUserRow? user) => this._isActive && user != null && !this.IsLoadingCard && !this.Management.IsBusy;

    /// <summary> Проверяет возможность обновления карточки. </summary>
    private bool CanRefreshCard() => this._isActive && this._cardUserId != null && !this.IsLoadingCard && !this.Management.IsBusy;

    /// <summary> Проверяет возможность перехода на предыдущую страницу. </summary>
    private bool CanPreviousPage() => this.CanLoad() && this.Page > 1;

    /// <summary> Проверяет возможность перехода на следующую страницу. </summary>
    private bool CanNextPage() => this.CanLoad() && this.Page * this._appliedPageSize < this.TotalCount;

    /// <summary> Применяет фильтры и перезагружает список. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task ApplyFiltersAsync()
    {
        this._appliedSearch = this.Search.Trim();
        this._appliedIncludeDeleted = this.IncludeDeleted;
        this._appliedPageSize = this.PageSizes.Contains(this.PageSize) ? this.PageSize : 50;
        this.Page = 1;
        this._list = null;
        this.Rows = [];
        this.SelectedUser = null;
        this.TotalCount = 0;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Перезагружает список при изменении размера страницы. </summary>
    partial void OnPageSizeChanged(int value)
    {
        if (value != this._appliedPageSize && this.CanLoad())
        {
            this.ApplyFiltersCommand.Execute(null);
        }
    }

    /// <summary> Перезагружает список при изменении учёта удалённых пользователей. </summary>
    partial void OnIncludeDeletedChanged(bool value)
    {
        if (value != this._appliedIncludeDeleted && this.CanLoad())
        {
            this.ApplyFiltersCommand.Execute(null);
        }
    }

    /// <summary> Сбрасывает фильтры и перезагружает список. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task ResetFiltersAsync()
    {
        this.Search = string.Empty;
        this._appliedIncludeDeleted = false;
        this.IncludeDeleted = false;
        this._appliedSort = AdminUsersSort.ByDateDesc;
        this.NotifySortHeaders();
        await this.ApplyFiltersCommand.ExecuteAsync(null);
    }

    /// <summary> Переключает сортировку списка по выбранному направлению. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task SortAsync(int ascendingSort)
    {
        var ascending = (AdminUsersSort)ascendingSort;
        var descending = UsersViewModel.GetDescendingSort(ascending);

        if (descending == null)
        {
            return;
        }

        this._appliedSort = this._appliedSort == ascending ? descending.Value
            : this._appliedSort == descending ? ascending
            : ascending == AdminUsersSort.ByEmailAsc
                ? ascending : descending.Value;
        this.NotifySortHeaders();
        this.Page = 1;
        await this.LoadPageAsync();
    }

    /// <summary> Формирует подпись заголовка сортировки с указанием направления. </summary>
    private string SortLabel(string title, AdminUsersSort ascending)
    {
        var descending = UsersViewModel.GetDescendingSort(ascending);

        return this._appliedSort == ascending ? title + " ↑" : this._appliedSort == descending ? title + " ↓" : title;
    }

    /// <summary> Определяет сортировку по убыванию для выбранного поля. </summary>
    private static AdminUsersSort? GetDescendingSort(AdminUsersSort ascending) => ascending switch
    {
        AdminUsersSort.ByDateAsc => AdminUsersSort.ByDateDesc,
        AdminUsersSort.ByEmailAsc => AdminUsersSort.ByEmailDesc,
        AdminUsersSort.ByVehiclesAsc => AdminUsersSort.ByVehiclesDesc,
        AdminUsersSort.ByMaintenanceAsc => AdminUsersSort.ByMaintenanceDesc,
        AdminUsersSort.ByOrganizationsAsc => AdminUsersSort.ByOrganizationsDesc,
        _ => null
    };

    /// <summary> Уведомляет об изменении заголовков сортировки. </summary>
    private void NotifySortHeaders()
    {
        this.OnPropertyChanged(nameof(EmailHeader));
        this.OnPropertyChanged(nameof(RegistrationHeader));
        this.OnPropertyChanged(nameof(VehiclesHeader));
        this.OnPropertyChanged(nameof(MaintenanceHeader));
        this.OnPropertyChanged(nameof(OrganizationsHeader));
    }

    /// <summary> Переходит на предыдущую страницу. </summary>
    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync()
    {
        this.Page--;
        await this.LoadPageAsync();
    }

    /// <summary> Переходит на следующую страницу. </summary>
    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync()
    {
        this.Page++;
        await this.LoadPageAsync();
    }

    /// <summary> Загружает страницу списка пользователей. </summary>
    private async Task LoadPageAsync()
    {
        this._list = null;
        this.Rows = [];
        this.SelectedUser = null;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Обновляет список пользователей. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        this.IsLoading = true;
        this.ErrorMessage = string.Empty;
        try
        {
            Func<Task> load = async () =>
            {
                var response = await this._sessionClient.GetUsersAsync(this._appliedSearch, this._appliedSort,
                    this._appliedIncludeDeleted, this.Page, this._appliedPageSize, null, cancellationToken);

                if (!this._isActive || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                this._list = response;
                this.Page = Math.Max(1, response.Page);
                this.TotalCount = response.AllCount;
                this.SetBillingAccess(this._canViewBilling);
            };

            await (this.TrackListReadAsync?.Invoke(load, cancellationToken) ?? load());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Уход со страницы отменяет загрузку списка.
        }
        catch (ApiException exception)
        {
            await this.HandleErrorAsync(exception, false);
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    /// <summary> Открывает карточку выбранного пользователя. </summary>
    [RelayCommand(CanExecute = nameof(CanOpenCard))]
    private async Task OpenCardAsync(AdminUserRow user, CancellationToken cancellationToken)
    {
        await this.Management.DeactivateAsync();
        this._cardUserId = user.Id;
        this.SelectedUser = user;
        this._overview = null;
        this.Card = null;
        this.IsCardOpen = true;
        await this.LoadCardAsync(user.Id, cancellationToken);
    }

    /// <summary> Открывает организации выбранного пользователя. </summary>
    [RelayCommand(CanExecute = nameof(CanOpenCard))]
    private async Task OpenOrganizationsAsync(AdminUserRow user)
    {
        await this.OpenCardCommand.ExecuteAsync(user);
        if (this._isActive && this.IsCardOpen && this.Card != null && this._cardUserId == user.Id)
        {
            this.Card.SelectedTab = this.Card.Tabs.First(tab => tab.Title.StartsWith("Организации", StringComparison.Ordinal));
        }
    }

    /// <summary> Обновляет данные открытой карточки. </summary>
    [RelayCommand(CanExecute = nameof(CanRefreshCard))]
    private Task RefreshCardAsync(CancellationToken cancellationToken) => this.LoadCardAsync(this._cardUserId!, cancellationToken);

    /// <summary> Загружает данные карточки пользователя по идентификатору. </summary>
    private async Task LoadCardAsync(string userId, CancellationToken cancellationToken)
    {
        this.IsLoadingCard = true;
        this.CardErrorMessage = string.Empty;
        try
        {
            var overview = await this._sessionClient.GetUserOverviewAsync(userId, cancellationToken);
            if (!this._isActive || !this.IsCardOpen || cancellationToken.IsCancellationRequested || this._cardUserId != userId)
            {
                return;
            }

            this._overview = overview;
            this.Management.SetOverview(overview, this._canViewBilling);
            this.ApplyCard(overview);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Возврат в список и уход со страницы отменяют загрузку карточки.
        }
        catch (ApiException exception)
        {
            await this.HandleErrorAsync(exception, true);
        }
        finally
        {
            this.IsLoadingCard = false;
        }
    }

    /// <summary> Применяет загруженные данные к карточке пользователя. </summary>
    private void ApplyCard(AdminUserOverview overview)
    {
        var previousTab = this.Card?.SelectedTab?.Title.Split('·')[0].Trim();
        var previousOrganizationStatus = this.Card?.OrganizationStatus;
        var previousSubscriptionStatus = this.Card?.SubscriptionStatus;
        this.Card = new UserCardViewModel(overview, this._canViewBilling, this.Management,
            userId => this.UserActivityRequested?.Invoke(userId));
        if (previousOrganizationStatus != null)
        {
            this.Card.OrganizationStatus = previousOrganizationStatus;
        }
        if (previousSubscriptionStatus != null)
        {
            this.Card.SubscriptionStatus = previousSubscriptionStatus;
        }

        this.Card.SelectedTab = this.Card.Tabs.FirstOrDefault(tab => tab.Title.Split('·')[0].Trim() == previousTab)
            ?? this.Card.Tabs[0];
    }

    /// <summary> Возвращается к списку пользователей. </summary>
    [RelayCommand]
    private async Task BackToListAsync()
    {
        this.IsCardOpen = false;
        var managementStopped = this.Management.DeactivateAsync();
        this.OpenCardCommand.Cancel();
        this.RefreshCardCommand.Cancel();
        await Task.WhenAll(new[] { this.OpenCardCommand.ExecutionTask, this.RefreshCardCommand.ExecutionTask,
            managementStopped }.OfType<Task>());
        this.CardErrorMessage = string.Empty;
    }

    /// <summary> Обновляет карточку после сохранённого административного изменения. </summary>
    private async Task RefreshAfterManagementAsync(CancellationToken cancellationToken)
    {
        if (!this._isActive || !this.IsCardOpen || this._cardUserId == null)
        {
            return;
        }

        await this.LoadCardAsync(this._cardUserId, cancellationToken);
        if (this.HasCardError)
        {
            throw new ApiException("Изменения сохранены, но карточку не удалось обновить. Нажмите «Обновить».",
                HttpStatusCode.ServiceUnavailable);
        }
    }

    /// <summary> Обрабатывает ошибку API для списка или карточки. </summary>
    private async Task HandleErrorAsync(ApiException exception, bool isCard)
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

        if (isCard)
        {
            this.CardErrorMessage = this.Card == null ? exception.Message : "Не удалось обновить карточку. Показаны прежние данные. " + exception.Message;
        }
        else
        {
            this.ErrorMessage = this.Rows.Count == 0 ? exception.Message : "Не удалось обновить список. Показаны прежние данные. " + exception.Message;
        }
    }
}
