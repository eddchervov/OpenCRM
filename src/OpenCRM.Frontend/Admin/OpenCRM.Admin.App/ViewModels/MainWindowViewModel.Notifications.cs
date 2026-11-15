using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public partial class MainWindowViewModel
{
    private const string NavigationReadFailureMessage = "Раздел загружен, но не удалось сохранить отметку просмотра. Повторите обновление списка.";

    private static readonly TimeSpan s_navigationUpdatesInterval = TimeSpan.FromMinutes(1);

    private CancellationTokenSource? _navigationUpdatesCancellation;
    private Task _navigationUpdatesTask = Task.CompletedTask;
    private int _navigationUpdatesVersion;
    private bool _navigationUpdatesForbidden;
    private bool _backgroundPollingPaused;
    private CancellationTokenSource? _navigationSectionCancellation;

    private void InitializeNavigationReadTracking()
    {
        this.Users.TrackListReadAsync = (load, token) => this.TrackNavigationSectionAsync(
            "users", load, () => this.IsUsersOpen && this.CanViewUsers, token);
        this.EmailHistory.TrackListReadAsync = (load, token) => this.TrackNavigationSectionAsync(
            "emails", load, () => this.IsEmailHistoryOpen && this.CanViewEmailHistory, token);
        this.SupportMessages.TrackListReadAsync = (load, token) => this.TrackNavigationSectionAsync(
            "supportMessages", load, () => this.IsSupportMessagesOpen && this.CanViewSupportMessages, token);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTodayUsers))]
    private int _todayUsersCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTodayBilling))]
    private int _todayBillingCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTodaySupportMessages))]
    private int _todaySupportMessagesCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTodayEmailHistory))]
    private int _todayEmailHistoryCount;

    /// <summary> Признак непросмотренных пользователей. </summary>
    public bool HasTodayUsers => this.TodayUsersCount > 0;

    /// <summary> Признак оплаченных платежей, требующих завершения обработки или загрузки чека. </summary>
    public bool HasTodayBilling => this.TodayBillingCount > 0;

    /// <summary> Признак непросмотренных обращений. </summary>
    public bool HasTodaySupportMessages => this.TodaySupportMessagesCount > 0;

    /// <summary> Признак непросмотренных писем. </summary>
    public bool HasTodayEmailHistory => this.TodayEmailHistoryCount > 0;

    /// <summary> Текущий или последний завершённый цикл проверки счётчиков меню. </summary>
    public Task NavigationUpdatesTask => this._navigationUpdatesTask;

    /// <summary> Запускает периодическое обновление непросмотренных событий меню. </summary>
    public void StartNavigationUpdates(bool clearCounts = true)
    {
        this.StopNavigationUpdates(clearCounts);
        if (this._isClosing || this._backgroundPollingPaused || this.Profile == null || !this._sessionClient.HasSession)
        {
            return;
        }

        this._navigationUpdatesForbidden = false;
        var cancellation = new CancellationTokenSource();
        var version = this._navigationUpdatesVersion;
        this._navigationUpdatesCancellation = cancellation;
        this._navigationUpdatesTask = this.PollNavigationUpdatesAsync(this.Profile.Id, version, cancellation);
    }

    /// <summary> Останавливает периодическую проверку счётчиков меню. </summary>
    public void StopNavigationUpdates(bool clearCounts = true)
    {
        this._navigationUpdatesVersion++;
        var cancellation = this._navigationUpdatesCancellation;
        this._navigationUpdatesCancellation = null;
        cancellation?.Cancel();
        if (clearCounts)
        {
            this.SetNavigationCounts(0, 0, 0, 0);
            this.SetPendingCatalogSuggestionsCount(0);
        }
    }

    /// <summary> Приостанавливает фоновые запросы свёрнутого окна и возобновляет опрос при восстановлении. </summary>
    public void SetBackgroundPollingPaused(bool isPaused)
    {
        if (this._backgroundPollingPaused == isPaused)
        {
            return;
        }

        this._backgroundPollingPaused = isPaused;
        if (isPaused)
        {
            this.StopNavigationUpdates(clearCounts: false);

            return;
        }

        this.StartNavigationUpdates(clearCounts: false);
    }

    /// <summary> Обновляет профиль по таймеру, если окно не свёрнуто и сессия доступна. </summary>
    public Task RefreshBackgroundProfileAsync()
    {
        if (this._backgroundPollingPaused || !this.IsAuthenticated || !this.RefreshProfileCommand.CanExecute(null))
        {
            return Task.CompletedTask;
        }

        return this.RefreshProfileCommand.ExecuteAsync(null);
    }

    private async Task PollNavigationUpdatesAsync(
        string profileId,
        int version,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !this._navigationUpdatesForbidden)
            {
                try
                {
                    var today = DateTime.Today;
                    var fromUtc = today.ToUniversalTime();
                    var toUtc = today.AddDays(1).ToUniversalTime();
                    var counts = await this._sessionClient.GetUnreadNavigationCountsAsync(fromUtc, toUtc, cancellationToken);

                    if (!this.IsCurrentNavigationUpdate(version, profileId, cancellationToken))
                    {
                        return;
                    }

                    if (!this._sessionClient.HasSession)
                    {
                        return;
                    }

                    this.SetNavigationCounts(
                        this.CanViewUsers ? counts.Users : 0,
                        this.CanViewBilling ? counts.Payments : 0,
                        this.CanViewSupportMessages ? counts.SupportMessages : 0,
                        this.CanViewEmailHistory ? counts.Emails : 0);
                    if (this.HasPermission("Admin.Content.View"))
                    {
                        var pendingCount = counts.PendingCatalogSuggestions;
                        if (pendingCount == null)
                        {
                            var suggestions = await this._sessionClient.GetCatalogSuggestionsAsync(1, cancellationToken);
                            pendingCount = suggestions.Count(suggestion => suggestion.Status == 1);
                        }

                        if (this.IsCurrentNavigationUpdate(version, profileId, cancellationToken))
                        {
                            this.SetPendingCatalogSuggestionsCount(pendingCount.Value);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException exception)
                {
                    Debug.WriteLine($"Опрос счётчиков меню прерван: {exception.Message}");
                }
                catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (this.IsCurrentNavigationUpdate(version, profileId, cancellationToken))
                    {
                        this.SetProfile(null);
                    }

                    return;
                }
                catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
                {
                    if (this.IsCurrentNavigationUpdate(version, profileId, cancellationToken))
                    {
                        this._navigationUpdatesForbidden = true;
                        this.SetNavigationCounts(0, 0, 0, 0);
                        this.SetPendingCatalogSuggestionsCount(0);
                    }

                    return;
                }
                catch (Exception exception) when (exception is ApiException or HttpRequestException or IOException
                    or JsonException or InvalidOperationException)
                {
                    Debug.WriteLine($"Не удалось обновить счётчики меню: {exception.Message}");
                }

                try
                {
                    await Task.Delay(s_navigationUpdatesInterval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(this._navigationUpdatesCancellation, cancellation))
            {
                this._navigationUpdatesCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentNavigationUpdate(int version, string profileId, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && version == this._navigationUpdatesVersion
        && !this._isClosing
        && this.Profile?.Id == profileId;

    /// <summary> Отменяет отметку просмотра при переходе, выходе или закрытии приложения. </summary>
    private int AdvanceNavigationVersion()
    {
        this._navigationSectionCancellation?.Cancel();

        return ++this._navigationVersion;
    }

    /// <summary> Фиксирует серверную границу только после успешной загрузки актуального раздела. </summary>
    private async Task TrackNavigationSectionAsync(
        string section,
        Func<Task> activate,
        Func<bool> isLoaded,
        CancellationToken loadToken)
    {
        var navigationVersion = this._navigationVersion;
        var profileId = this.Profile?.Id;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(loadToken);
        this._navigationSectionCancellation = cancellation;
        var token = cancellation.Token;
        try
        {
            var snapshot = await this.LoadNavigationSnapshotAsync(token);
            if (!this.IsCurrentNavigationSection(navigationVersion, profileId, token))
            {
                return;
            }

            await activate();
            if (snapshot?.AsOfUtc is not { } viewedThroughUtc || !isLoaded()
                || !this.IsCurrentNavigationSection(navigationVersion, profileId, token))
            {
                return;
            }

            await this.ConfirmNavigationSectionAsync(section, viewedThroughUtc, profileId!, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Переход на другой экран отменяет неподтверждённый просмотр.
        }
        catch (OperationCanceledException)
        {
            if (navigationVersion == this._navigationVersion && !this._isClosing)
            {
                this.ErrorMessage = "Сервер не ответил вовремя. Повторите открытие раздела.";
            }
        }
        finally
        {
            if (ReferenceEquals(this._navigationSectionCancellation, cancellation))
            {
                this._navigationSectionCancellation = null;
            }
        }
    }

    private bool IsCurrentNavigationSection(int version, string? profileId, CancellationToken token) =>
        !token.IsCancellationRequested && !this._isClosing && this._sessionClient.HasSession
        && version == this._navigationVersion && this.Profile?.Id == profileId;

    /// <summary> Сбой счётчиков не препятствует загрузке самого раздела. </summary>
    private async Task<AdminNavigationCounts?> LoadNavigationSnapshotAsync(CancellationToken token)
    {
        try
        {
            var today = DateTime.Today;

            return await this._sessionClient.GetUnreadNavigationCountsAsync(
                today.ToUniversalTime(), today.AddDays(1).ToUniversalTime(), token);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or IOException
            or JsonException or InvalidOperationException || exception is OperationCanceledException && !token.IsCancellationRequested)
        {
            Debug.WriteLine($"Не удалось получить границу просмотра раздела: {exception.Message}");

            return null;
        }
    }

    private async Task ConfirmNavigationSectionAsync(
        string section, DateTime viewedThroughUtc, string profileId, CancellationToken token)
    {
        // Старый ответ фонового опроса не должен вернуть уже сброшенный значок.
        this.StopNavigationUpdates(clearCounts: false);
        try
        {
            await this._sessionClient.MarkNavigationSectionViewedAsync(section, viewedThroughUtc, token);
            if (!token.IsCancellationRequested && this.Profile?.Id == profileId)
            {
                this.ClearNavigationSectionCount(section);
                if (this.ErrorMessage == NavigationReadFailureMessage)
                {
                    this.ErrorMessage = string.Empty;
                }
            }
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or IOException or JsonException)
        {
            if (this.Profile?.Id == profileId && !token.IsCancellationRequested)
            {
                this.ErrorMessage = NavigationReadFailureMessage;
            }

            Debug.WriteLine($"Не удалось сохранить отметку просмотра раздела: {exception.Message}");
        }
        finally
        {
            if (this.Profile?.Id == profileId)
            {
                this.StartNavigationUpdates(clearCounts: false);
            }
        }
    }

    private void ClearNavigationSectionCount(string section)
    {
        switch (section)
        {
            case "users": this.TodayUsersCount = 0; break;
            case "supportMessages": this.TodaySupportMessagesCount = 0; break;
            case "emails": this.TodayEmailHistoryCount = 0; break;
        }
    }

    private void SetNavigationCounts(int users, int billing, int supportMessages, int emailHistory)
    {
        this.TodayUsersCount = users;
        this.TodayBillingCount = billing;
        this.TodaySupportMessagesCount = supportMessages;
        this.TodayEmailHistoryCount = emailHistory;
        if (!this.HasPermission("Admin.Content.View"))
        {
            this.SetPendingCatalogSuggestionsCount(0);
        }
    }

    private void SetPendingCatalogSuggestionsCount(int count)
    {
        var section = this._operationSections.FirstOrDefault(section => section.Model is CatalogSuggestionsViewModel);
        if (section != null)
        {
            section.BadgeCount = Math.Max(0, count);
        }
    }
}
