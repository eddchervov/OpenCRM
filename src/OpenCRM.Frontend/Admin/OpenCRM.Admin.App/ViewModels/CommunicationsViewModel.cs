using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Управляет загрузкой страниц и отменой запросов коммуникационных разделов. </summary>
public abstract partial class CommunicationsViewModel(AdminSessionClient sessionClient) : ObservableObject
{
    /// <summary> Отмечает просмотр только после успешной загрузки страницы журнала. </summary>
    public Func<Func<Task>, CancellationToken, Task>? TrackListReadAsync { get; set; }

    protected AdminSessionClient SessionClient { get; } = sessionClient;
    protected Task CurrentOperation => this._operation;
    protected bool IsActive => this._isActive;
    protected CancellationToken LifetimeToken => this._lifetime?.Token ?? new CancellationToken(canceled: true);
    private CancellationTokenSource? _lifetime;
    private Task _operation = Task.CompletedTask;
    private int _version;
    private bool _isActive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private int _page = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _pageSize = 50;

    public string PageLabel => $"Найдено: {this.TotalCount:N0} · Страница {this.Page} из {Math.Max(1, (int)Math.Ceiling((double)this.TotalCount / Math.Max(1, this.PageSize)))}";

    public event Action? SessionExpired;
    public event Func<Task>? AccessDenied;

    public async Task ActivateAsync()
    {
        await this.DeactivateAsync();
        this.ClearData();
        this._lifetime = new CancellationTokenSource();
        this._isActive = true;
        this.NotifyCommands();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this._version++;
        this._lifetime?.Cancel();
        this.IsBusy = false;
        this.NotifyCommands();
        if (clear)
        {
            this.ClearData();
            this.Search = string.Empty;
            this.ResetFilters();
            this.ApplyFilters();
            this.ErrorMessage = string.Empty;
            this.Page = 1;
            this.TotalCount = 0;
        }
    }

    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        await this._operation;
        this._lifetime?.Dispose();
        this._lifetime = null;
    }

    protected bool CanLoad() => this._isActive && !this.IsBusy;

    protected Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (!this.CanLoad() || this._lifetime == null)
        {
            return Task.CompletedTask;
        }

        this._operation = this.ExecuteOperationAsync(action, this._version, this._lifetime.Token);

        return this._operation;
    }

    protected async Task HandleApiErrorAsync(Exception exception)
    {
        if (!this._isActive)
        {
            return;
        }

        if (exception is ApiException apiException)
        {
            this.ErrorMessage = apiException.Message;
            if (apiException.StatusCode == HttpStatusCode.Unauthorized)
            {
                this.SessionExpired?.Invoke();
            }
            else if (apiException.StatusCode == HttpStatusCode.Forbidden && this.AccessDenied != null)
            {
                await this.AccessDenied.Invoke();
            }

            return;
        }

        this.ErrorMessage = exception is HttpRequestException
            ? "Не удалось связаться с сервером. Проверьте подключение."
            : "Не удалось выполнить запрос. Повторите попытку.";
    }

    private async Task ExecuteOperationAsync(Func<CancellationToken, Task> action, int version, CancellationToken token)
    {
        this.IsBusy = true;
        this.ErrorMessage = string.Empty;
        try
        {
            await action(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Уход с экрана отменяет запрос и сохраняет интерфейс нового раздела.
        }
        catch (ApiException exception) when (version == this._version && this._isActive)
        {
            this.ErrorMessage = exception.Message;
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
            // Ответ завершённой сессии не изменяет состояние экрана.
        }
        catch (OperationCanceledException)
        {
            if (version == this._version && this._isActive)
            {
                this.ErrorMessage = "Превышено время ожидания ответа сервера. Повторите попытку.";
            }
        }
        catch (HttpRequestException)
        {
            if (version == this._version && this._isActive)
            {
                this.ErrorMessage = "Не удалось связаться с сервером. Проверьте подключение.";
            }
        }
        finally
        {
            if (version == this._version)
            {
                this.IsBusy = false;
            }
        }
    }

    private Task LoadTrackedPageAsync(CancellationToken token) =>
        this.TrackListReadAsync?.Invoke(() => this.LoadPageAsync(token), token) ?? this.LoadPageAsync(token);

    protected abstract Task LoadPageAsync(CancellationToken cancellationToken);
    protected abstract void ApplyFilters();
    protected abstract void ResetFilters();
    protected abstract void ClearData();

    protected virtual void NotifyCommands()
    {
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.ApplyFiltersCommand.NotifyCanExecuteChanged();
        this.ResetFiltersCommand.NotifyCanExecuteChanged();
        this.PreviousPageCommand.NotifyCanExecuteChanged();
        this.NextPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => this.NotifyCommands();
    partial void OnErrorMessageChanged(string value) => this.OnPropertyChanged("HasError");
    partial void OnPageChanged(int value) => this.NotifyPage();
    partial void OnTotalCountChanged(int value) => this.NotifyPage();
    partial void OnPageSizeChanged(int value) => this.NotifyPage();

    private void NotifyPage()
    {
        this.OnPropertyChanged(nameof(PageLabel));
        this.PreviousPageCommand.NotifyCanExecuteChanged();
        this.NextPageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task RefreshAsync() => this.RunAsync(this.LoadTrackedPageAsync);

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task ApplyFiltersAsync()
    {
        this.ApplyFilters();
        this.Page = 1;
        this.ClearData();

        return this.RunAsync(this.LoadTrackedPageAsync);
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task ResetFiltersAsync()
    {
        this.Search = string.Empty;
        this.ResetFilters();

        return this.ApplyFiltersAsync();
    }

    private bool CanPreviousPage() => this.CanLoad() && this.Page > 1;
    private bool CanNextPage() => this.CanLoad() && (long)this.Page * this.PageSize < this.TotalCount;

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private Task PreviousPageAsync()
    {
        this.Page--;
        this.ClearData();

        return this.RunAsync(this.LoadTrackedPageAsync);
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private Task NextPageAsync()
    {
        this.Page++;
        this.ClearData();

        return this.RunAsync(this.LoadTrackedPageAsync);
    }
}
