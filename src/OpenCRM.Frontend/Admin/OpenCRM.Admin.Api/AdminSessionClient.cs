using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

/// <summary> Клиент административного API для управления сессией и отчётами. </summary>
public sealed partial class AdminSessionClient : IDisposable
{
    /// <summary> Интервал, за который заблаговременно обновляется токен. </summary>
    private static readonly TimeSpan s_tokenRefreshWindow = TimeSpan.FromSeconds(30);

    /// <summary> Ограниченное время завершения ротации токена при закрытии приложения. </summary>
    private static readonly TimeSpan s_tokenRefreshTimeout = TimeSpan.FromSeconds(20);

    /// <summary> Тайм-аут отзыва токенов. </summary>
    private static readonly TimeSpan s_revokeTimeout = TimeSpan.FromSeconds(5);

    /// <summary> Настройки JSON-сериализации для запросов и ответов API. </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary> HTTP-клиент для обращений к административному API. </summary>
    private readonly HttpClient _httpClient;
    private string _uiLanguage = "ru";

    /// <summary> Поставщик времени для управления сроками токенов. </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary> Хранилище токена обновления между запусками приложения. </summary>
    private readonly IAdminSessionStore? _sessionStore;

    /// <summary> Блокировка для синхронизации доступа к сессии. </summary>
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    /// <summary> Текущие токены сессии, если есть. </summary>
    private ApiTokenResponse? _tokens;

    /// <summary> Профиль текущего администратора, если выполнен вход. </summary>
    private AdminProfile? _currentProfile;

    /// <summary> Версия сессии для проверки актуальности запросов. </summary>
    private long _sessionVersion;

    /// <summary> Признак освобождения ресурсов клиента. </summary>
    private bool _disposed;

    /// <summary> Создаёт клиент с HTTP-клиентом и необязательным поставщиком времени. </summary>
    public AdminSessionClient(
        HttpClient httpClient,
        TimeProvider? timeProvider = null,
        IAdminSessionStore? sessionStore = null)
    {
        this._httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this._timeProvider = timeProvider ?? TimeProvider.System;
        this._sessionStore = sessionStore;
    }

    /// <summary> Признак наличия активной сессии. </summary>
    public bool HasSession => this._tokens != null && this._currentProfile != null;

    /// <summary> Профиль текущего администратора, если есть. </summary>
    public AdminProfile? CurrentProfile => this._currentProfile;

    /// <summary> Ключ хоста и администратора для локального черновика финансовой операции. </summary>
    public string PendingActionScope => $"{this._httpClient.BaseAddress?.GetLeftPart(UriPartial.Authority)}{this._httpClient.BaseAddress?.AbsolutePath.TrimEnd('/')}:{this.CurrentProfile?.Id}";

    public string UiLanguage
    {
        get => this._uiLanguage;
        set => this._uiLanguage = AdminUiLanguage.Normalize(value);
    }

    /// <summary> Выполняет вход администратора по почте и паролю. </summary>
    public async Task<AdminProfile> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        await this._sessionLock.WaitAsync(cancellationToken);

        try
        {
            this.ClearSession(false);

            using var response = await this.SendJsonAsync(
                HttpMethod.Post,
                "api/v1/auth/login",
                new LoginRequest(email, password),
                null,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await this.CreateApiExceptionAsync(response, "Не удалось войти в административную панель.", cancellationToken);
            }

            var tokens = await this.ReadTokenResponseAsync(response, cancellationToken);
            this.ValidateTokenResponse(tokens, null);
            this._tokens = tokens;

            try
            {
                var profile = await this.GetProfileCoreAsync(cancellationToken);
                this.SaveSession();

                return profile;
            }
            catch
            {
                var rejectedTokens = this._tokens;
                this.ClearSession(false);
                if (rejectedTokens != null)
                {
                    await this.TryRevokeAsync(rejectedTokens);
                }

                throw;
            }
        }
        catch
        {
            this.ClearSession(false);
            throw;
        }
        finally
        {
            this._sessionLock.Release();
        }
    }

    /// <summary> Восстанавливает сессию и проверяет текущие права на сервере. </summary>
    public async Task<AdminProfile?> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        await this._sessionLock.WaitAsync(cancellationToken);

        try
        {
            var savedSession = this._sessionStore?.Load();
            if (savedSession == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(savedSession.UserId)
                || string.IsNullOrWhiteSpace(savedSession.RefreshToken)
                || savedSession.ExpiresAt <= this._timeProvider.GetUtcNow())
            {
                this.ClearSession();

                return null;
            }

            this.ClearSession(false);
            this._tokens = new ApiTokenResponse
            {
                RefreshToken = savedSession.RefreshToken,
                RefreshTokenExpiresAt = savedSession.ExpiresAt,
                User = new ApiTokenUser { Id = savedSession.UserId }
            };

            await this.RefreshTokensAsync(cancellationToken, restoringSession: true);

            return await this.GetProfileCoreAsync(cancellationToken);
        }
        catch
        {
            // Сетевой сбой не должен удалять сохранённый или уже обновлённый токен.
            this.ClearSession(false);
            throw;
        }
        finally
        {
            this._sessionLock.Release();
        }
    }

    /// <summary> Получает профиль текущего администратора. </summary>
    public async Task<AdminProfile> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        await this._sessionLock.WaitAsync(cancellationToken);

        try
        {
            if (this._tokens == null)
            {
                throw new ApiException(this.LocalizeFallback("Войдите в административную панель."), HttpStatusCode.Unauthorized);
            }

            return await this.GetProfileCoreAsync(cancellationToken);
        }
        finally
        {
            this._sessionLock.Release();
        }
    }

    /// <summary> Получает данные дашборда за заданный период. </summary>
    public async Task<AdminDashboard> GetDashboardAsync(
        int periodDays,
        CancellationToken cancellationToken = default)
    {
        return await this.GetAuthorizedReportAsync<AdminDashboard>(
            $"api/v1/admin/dashboard?periodDays={periodDays}",
            "Не удалось загрузить сводку административной панели.",
            cancellationToken);
    }

    /// <summary> Получает список требующих внимания платежей биллинга. </summary>
    public async Task<AdminBillingAttention> GetBillingAttentionAsync(
        int periodDays = 1,
        CancellationToken cancellationToken = default)
    {
        return await this.GetAuthorizedReportAsync<AdminBillingAttention>(
            $"api/v1/admin/billing/attention?periodDays={periodDays}",
            "Не удалось загрузить состояние чеков и активаций.",
            cancellationToken);
    }

    /// <summary> Получает сводные данные биллинга. </summary>
    public Task<AdminBilling> GetBillingAsync(CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<AdminBilling>(
            "api/v1/admin/billing",
            "Не удалось загрузить платежи и подписки.",
            cancellationToken);
    }

    /// <summary> Получает страницу платежей биллинга с фильтрами и сортировкой. </summary>
    public Task<AdminBillingPaymentsPage> GetBillingPaymentsPageAsync(
        string? search,
        string status,
        int periodDays,
        int page,
        int pageSize,
        string sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        var query = AdminSessionClient.BuildBillingPageQuery(search, status, page, pageSize, sort, descending);
        query.Add($"periodDays={periodDays}");

        return this.GetAuthorizedReportAsync<AdminBillingPaymentsPage>(
            $"api/v1/admin/billing/payments?{string.Join("&", query)}",
            "Не удалось загрузить страницу платежей.",
            cancellationToken);
    }

    /// <summary> Получает страницу подписок биллинга с фильтрами и сортировкой. </summary>
    public Task<AdminBillingSubscriptionsPage> GetBillingSubscriptionsPageAsync(
        string? search,
        string status,
        int page,
        int pageSize,
        string sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        var query = AdminSessionClient.BuildBillingPageQuery(search, status, page, pageSize, sort, descending);

        return this.GetAuthorizedReportAsync<AdminBillingSubscriptionsPage>(
            $"api/v1/admin/billing/subscriptions/organizations?{string.Join("&", query)}",
            "Не удалось загрузить страницу подписок организаций.",
            cancellationToken);
    }

    /// <summary> Формирует параметры строки запроса для страниц биллинга. </summary>
    private static List<string> BuildBillingPageQuery(
        string? search,
        string status,
        int page,
        int pageSize,
        string sort,
        bool descending)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        query.Add($"status={Uri.EscapeDataString(status)}");
        query.Add($"page={page}");
        query.Add($"pageSize={pageSize}");
        query.Add($"sort={Uri.EscapeDataString(sort)}");
        query.Add($"descending={descending.ToString().ToLowerInvariant()}");

        return query;
    }

    /// <summary> Получает платёж биллинга по идентификатору. </summary>
    public Task<AdminBillingPayment> GetBillingPaymentAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<AdminBillingPayment>(
            $"api/v1/admin/billing/payments/{paymentId}",
            "Не удалось загрузить платеж.",
            cancellationToken);
    }

    /// <summary> Получает историю изменений статуса платежа. </summary>
    public Task<IReadOnlyList<AdminPaymentStatusAudit>> GetBillingPaymentAuditsAsync(
        int paymentId,
        CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<IReadOnlyList<AdminPaymentStatusAudit>>(
            $"api/v1/admin/billing/payments/{paymentId}/status-audits",
            "Не удалось загрузить историю статусов платежа.",
            cancellationToken);
    }

    /// <summary> Получает историю изменений подписки. </summary>
    public Task<IReadOnlyList<AdminSubscriptionAudit>> GetBillingSubscriptionAuditsAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        return this.GetAuthorizedReportAsync<IReadOnlyList<AdminSubscriptionAudit>>(
            $"api/v1/admin/billing/subscriptions/organizations/{subscriptionId}/audits",
            "Не удалось загрузить историю подписки.",
            cancellationToken);
    }

    /// <summary> Получает страницу пользователей с поиском, фильтрами и сортировкой. </summary>
    public Task<AdminUsersResponse> GetUsersAsync(
        string? search = null,
        AdminUsersSort sort = AdminUsersSort.ByDateDesc,
        bool includeDeleted = false,
        int page = 1,
        int pageSize = 25,
        string? utmSource = null,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"sort={(int)sort}",
            $"includeDeleted={includeDeleted.ToString().ToLowerInvariant()}",
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrEmpty(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (!string.IsNullOrEmpty(utmSource))
        {
            query.Add($"utmSource={Uri.EscapeDataString(utmSource)}");
        }

        return this.GetAuthorizedReportAsync<AdminUsersResponse>(
            $"api/v1/admin/users?{string.Join("&", query)}",
            "Не удалось загрузить список пользователей.",
            cancellationToken);
    }

    /// <summary> Получает обзор пользователя по идентификатору. </summary>
    public Task<AdminUserOverview> GetUserOverviewAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return this.GetAuthorizedReportAsync<AdminUserOverview>(
            $"api/v1/admin/users/{Uri.EscapeDataString(userId)}/overview",
            "Не удалось загрузить карточку пользователя.",
            cancellationToken);
    }

    /// <summary> Выполняет выход и завершает сессию. </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        this.ThrowIfDisposed();
        await this._sessionLock.WaitAsync();

        try
        {
            if (this._tokens == null)
            {
                return;
            }

            await this.EnsureValidAccessTokenAsync(cancellationToken);
            var tokens = this._tokens;
            if (tokens == null)
            {
                return;
            }

            await this.RevokeAsync(tokens, cancellationToken);
        }
        finally
        {
            this.ClearSession();
            this._sessionLock.Release();
        }
    }

    /// <summary> Освобождает ресурсы клиента. </summary>
    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;
        this.ClearSession(false);
        this._sessionLock.Dispose();
    }

    /// <summary> Получает профиль администратора из API. </summary>
    private async Task<AdminProfile> GetProfileCoreAsync(CancellationToken cancellationToken)
    {
        await this.EnsureValidAccessTokenAsync(cancellationToken);
        using var response = await this.SendProfileRequestAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await this.RefreshTokensAsync(cancellationToken);
            using var retryResponse = await this.SendProfileRequestAsync(cancellationToken);
            return await this.ReadProfileResponseAsync(retryResponse, cancellationToken);
        }

        return await this.ReadProfileResponseAsync(response, cancellationToken);
    }

    /// <summary> Выполняет авторизованный запрос отчёта с обработкой обновления токена. </summary>
    private Task<TReport> GetAuthorizedReportAsync<TReport>(
        string path,
        string fallbackMessage,
        CancellationToken cancellationToken) =>
        this.SendAuthorizedRequestAsync<TReport>(HttpMethod.Get, path, null, fallbackMessage, cancellationToken);

    /// <summary> Выполняет запрос с обновлением токенов и проверкой актуальности сессии. </summary>
    private Task<TReport> SendAuthorizedRequestAsync<TReport>(
        HttpMethod method,
        string path,
        object? body,
        string fallbackMessage,
        CancellationToken cancellationToken) =>
        this.SendAuthorizedContentAsync<TReport>(method, path,
            () => body == null ? null : JsonContent.Create(body, options: s_jsonOptions),
            fallbackMessage, cancellationToken);

    /// <summary> Повторно создаёт содержимое запроса после обновления токена. </summary>
    private async Task<TReport> SendAuthorizedContentAsync<TReport>(
        HttpMethod method,
        string path,
        Func<HttpContent?> createContent,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        this.ThrowIfDisposed();
        var requestSession = await this.GetReportRequestSessionAsync(cancellationToken);
        using var response = await this.SendAsync(
            method,
            path,
            createContent(),
            requestSession.Tokens.AccessToken,
            cancellationToken);

        var requestTokens = requestSession.Tokens;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            requestTokens = await this.RefreshAfterUnauthorizedAsync(requestSession, requestTokens, cancellationToken);

            using var retryResponse = await this.SendAsync(
                method,
                path,
                createContent(),
                requestTokens.AccessToken,
                cancellationToken);

            return await this.CompleteAuthorizedReportRequestAsync<TReport>(
                retryResponse,
                fallbackMessage,
                requestSession.Version,
                requestTokens,
                cancellationToken);
        }

        return await this.CompleteAuthorizedReportRequestAsync<TReport>(
            response,
            fallbackMessage,
            requestSession.Version,
            requestTokens,
            cancellationToken);
    }

    /// <summary> Формирует сессию запроса отчёта с актуальными токенами. </summary>
    private async Task<ReportRequestSession> GetReportRequestSessionAsync(CancellationToken cancellationToken)
    {
        await this._sessionLock.WaitAsync(cancellationToken);

        try
        {
            if (this._tokens == null)
            {
                throw new ApiException(this.LocalizeFallback("Войдите в административную панель."), HttpStatusCode.Unauthorized);
            }

            await this.EnsureValidAccessTokenAsync(cancellationToken);
            return new ReportRequestSession(this._sessionVersion, this._tokens!);
        }
        finally
        {
            this._sessionLock.Release();
        }
    }

    /// <summary> Обновляет токены после ответа 401 и повторяет запрос. </summary>
    private async Task<ApiTokenResponse> RefreshAfterUnauthorizedAsync(
        ReportRequestSession requestSession,
        ApiTokenResponse attemptedTokens,
        CancellationToken cancellationToken)
    {
        await this._sessionLock.WaitAsync(cancellationToken);

        try
        {
            this.EnsureCurrentReportSession(requestSession.Version);
            var tokens = this._tokens!;
            if (ReferenceEquals(tokens, attemptedTokens))
            {
                await this.RefreshTokensAsync(cancellationToken);
                tokens = this._tokens
                    ?? throw new ApiException(this.LocalizeFallback("Срок действия сессии истек. Войдите снова."), HttpStatusCode.Unauthorized);
            }

            return tokens;
        }
        finally
        {
            this._sessionLock.Release();
        }
    }

    /// <summary> Обрабатывает ответ отчёта, проверяя актуальность сессии и токенов. </summary>
    private async Task<TReport> CompleteAuthorizedReportRequestAsync<TReport>(
        HttpResponseMessage response,
        string fallbackMessage,
        long sessionVersion,
        ApiTokenResponse requestTokens,
        CancellationToken cancellationToken)
    {
        await this._sessionLock.WaitAsync(cancellationToken);

        try
        {
            this.EnsureCurrentReportSession(sessionVersion);
            if (response.StatusCode == HttpStatusCode.Unauthorized
                && ReferenceEquals(this._tokens, requestTokens))
            {
                this.ClearSession();
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await this.CreateApiExceptionAsync(response, fallbackMessage, cancellationToken);
            }

            try
            {
                if (typeof(TReport) == typeof(AdminEmptyResponse))
                {
                    return (TReport)(object)new AdminEmptyResponse();
                }

                if (typeof(TReport) == typeof(byte[]))
                {
                    return (TReport)(object)await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }

                return await response.Content.ReadFromJsonAsync<TReport>(s_jsonOptions, cancellationToken)
                ?? throw new ApiException(this.LocalizeFallback("Сервер вернул пустой ответ."), response.StatusCode);
            }
            catch (JsonException exception)
            {
            throw new ApiException(this.LocalizeFallback("Сервер вернул некорректный ответ."), response.StatusCode, exception);
            }
        }
        finally
        {
            this._sessionLock.Release();
        }
    }

    /// <summary> Проверяет, что версия сессии запроса актуальна. </summary>
    private void EnsureCurrentReportSession(long sessionVersion)
    {
        if (this._sessionVersion != sessionVersion || this._tokens == null)
        {
            throw new ApiException(this.LocalizeFallback("Сессия изменилась. Войдите в административную панель снова."), HttpStatusCode.Unauthorized);
        }
    }

    /// <summary> Разбирает ответ с профилем администратора. </summary>
    private async Task<AdminProfile> ReadProfileResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var tokens = this._tokens;
            this.ClearSession();
            if (tokens != null)
            {
                await this.TryRevokeAsync(tokens);
            }

            throw new ApiException(this.LocalizeFallback("У учетной записи нет доступа к административной панели."), response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            this.ClearSession();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await this.CreateApiExceptionAsync(response, "Не удалось загрузить профиль администратора.", cancellationToken);
        }

        AdminProfile? profile;
        try
        {
            profile = await response.Content.ReadFromJsonAsync<AdminProfile>(s_jsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            this.ClearSession();
            throw new ApiException(this.LocalizeFallback("Сервер вернул некорректный профиль администратора."), response.StatusCode, exception);
        }

        if (profile == null || string.IsNullOrWhiteSpace(profile.Id) || profile.Roles == null
            || profile.Permissions == null || this._tokens == null
            || !string.Equals(profile.Id, this._tokens.User.Id, StringComparison.Ordinal))
        {
            this.ClearSession();
            throw new ApiException(this.LocalizeFallback("Сервер вернул неполный или несогласованный профиль администратора."), response.StatusCode);
        }

        if (!profile.IsSystem && !profile.Permissions.Contains("Admin.Access", StringComparer.Ordinal))
        {
            var tokens = this._tokens;
            this.ClearSession();
            if (tokens != null)
            {
                await this.TryRevokeAsync(tokens);
            }

            throw new ApiException(this.LocalizeFallback("У учетной записи нет права Admin.Access."), HttpStatusCode.Forbidden);
        }

        this._currentProfile = profile;
        return profile;
    }

    /// <summary> Отправляет запрос на получение профиля. </summary>
    private async Task<HttpResponseMessage> SendProfileRequestAsync(CancellationToken cancellationToken)
    {
        var tokens = this._tokens ?? throw new ApiException(this.LocalizeFallback("Войдите в административную панель."), HttpStatusCode.Unauthorized);
        return await this.SendAsync(HttpMethod.Get, "api/v1/admin/current-user", null, tokens.AccessToken, cancellationToken);
    }

    /// <summary> Обновляет токен доступа, если срок его действия истекает. </summary>
    private async Task EnsureValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        var tokens = this._tokens ?? throw new ApiException(this.LocalizeFallback("Войдите в административную панель."), HttpStatusCode.Unauthorized);
        if (tokens.AccessTokenExpiresAt <= this._timeProvider.GetUtcNow().Add(s_tokenRefreshWindow))
        {
            await this.RefreshTokensAsync(cancellationToken);
        }
    }

    /// <summary> Запрашивает новые токены по refresh-токену. </summary>
    private async Task RefreshTokensAsync(CancellationToken cancellationToken, bool restoringSession = false)
    {
        var currentTokens = this._tokens;
        if (currentTokens == null)
        {
            throw new ApiException(this.LocalizeFallback("Срок действия сессии истек. Войдите снова."), HttpStatusCode.Unauthorized);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var saveSession = this._sessionStore != null && (restoringSession || this._currentProfile != null);
        using var refreshTimeout = new CancellationTokenSource(s_tokenRefreshTimeout);

        // После отправки запроса сервер может уже погасить старый токен: сначала сохраняем ответ, затем закрываем окно.
        var refreshCancellation = saveSession ? refreshTimeout.Token : cancellationToken;

        try
        {
            using var response = await this.SendJsonAsync(
                HttpMethod.Post,
                "api/v1/auth/refresh",
                new RefreshRequest(currentTokens.User.Id, currentTokens.RefreshToken),
                null,
                refreshCancellation);
            if (!response.IsSuccessStatusCode)
            {
                throw await this.CreateApiExceptionAsync(response, "Срок действия сессии истек. Войдите снова.", refreshCancellation);
            }

            var refreshedTokens = await this.ReadTokenResponseAsync(response, refreshCancellation);
            this.ValidateTokenResponse(refreshedTokens, currentTokens.User.Id);
            this._tokens = refreshedTokens;
            if (saveSession)
            {
                this.SaveSession();
            }
        }
        catch (OperationCanceledException exception) when (refreshTimeout.IsCancellationRequested)
        {
            this.ClearSession(false);
            throw new ApiException(this.LocalizeFallback("Превышено время ожидания обновления сессии. Повторите вход."), innerException: exception);
        }
        catch (ApiException exception) when (exception.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            this.ClearSession();
            throw;
        }
        catch
        {
            this.ClearSession(false);
            throw;
        }
    }

    /// <summary> Отзывает переданные токены. </summary>
    private async Task RevokeAsync(ApiTokenResponse tokens, CancellationToken cancellationToken)
    {
        using var response = await this.SendJsonAsync(
            HttpMethod.Post,
            "api/v1/auth/revoke",
            new RevokeRequest(tokens.User.Id, tokens.RefreshToken),
            tokens.AccessToken,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await this.CreateApiExceptionAsync(response, "Не удалось завершить сессию на сервере.", cancellationToken);
        }
    }

    /// <summary> Пытается отозвать токены, подавляя ошибки. </summary>
    private async Task TryRevokeAsync(ApiTokenResponse tokens)
    {
        using var timeout = new CancellationTokenSource(s_revokeTimeout);
        try
        {
            await this.RevokeAsync(tokens, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Истечение ограниченного времени отзыва не должно подменять исходный отказ в доступе.
        }
        catch (ApiException)
        {
            // Ошибка отзыва не должна подменять исходную ошибку авторизации или проверки прав.
        }
    }

    /// <summary> Отправляет JSON-запрос с необязательным токеном доступа. </summary>
    private Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string path,
        object body,
        string? accessToken,
        CancellationToken cancellationToken) =>
        this.SendAsync(method, path, JsonContent.Create(body, options: s_jsonOptions), accessToken, cancellationToken);

    /// <summary> Отправляет HTTP-запрос с необязательным токеном доступа. </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.AcceptLanguage.ParseAdd(AdminUiLanguage.GetAcceptLanguage(this.UiLanguage));
        request.Headers.TryAddWithoutValidation("X-OpenCRM-UI-Language", this.UiLanguage);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        try
        {
            return await this._httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ApiException(this.LocalizeFallback("Не удалось связаться с сервером. Проверьте подключение и повторите попытку."), null, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException(this.LocalizeFallback("Превышено время ожидания ответа сервера. Повторите попытку."), null, exception);
        }
    }

    /// <summary> Разбирает ответ с токенами сессии. </summary>
    private async Task<ApiTokenResponse> ReadTokenResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiTokenResponse>(s_jsonOptions, cancellationToken)
                ?? throw new ApiException(this.LocalizeFallback("Сервер вернул пустой ответ авторизации."), response.StatusCode);
        }
        catch (JsonException exception)
        {
            throw new ApiException(this.LocalizeFallback("Сервер вернул некорректный ответ авторизации."), response.StatusCode, exception);
        }
    }

    /// <summary> Проверяет корректность и соответствие полученных токенов. </summary>
    private void ValidateTokenResponse(ApiTokenResponse tokens, string? expectedUserId)
    {
        if (string.IsNullOrWhiteSpace(tokens.AccessToken)
            || string.IsNullOrWhiteSpace(tokens.RefreshToken)
            || !string.Equals(tokens.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase)
            || tokens.User == null
            || string.IsNullOrWhiteSpace(tokens.User.Id)
            || tokens.User.Roles == null
            || tokens.AccessTokenExpiresAt <= this._timeProvider.GetUtcNow()
            || tokens.RefreshTokenExpiresAt <= this._timeProvider.GetUtcNow()
            || (expectedUserId != null && !string.Equals(tokens.User.Id, expectedUserId, StringComparison.Ordinal)))
        {
            throw new ApiException(this.LocalizeFallback("Сервер вернул неполные или некорректные данные авторизации."), HttpStatusCode.BadGateway);
        }
    }

    /// <summary> Формирует исключение API из ответа с ошибкой. </summary>
    private async Task<ApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        string? message = null;
        string? errorCode = null;
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var messageProperty)
                && messageProperty.ValueKind == JsonValueKind.String)
            {
                message = messageProperty.GetString();
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("code", out var codeProperty)
                && codeProperty.ValueKind == JsonValueKind.String)
            {
                errorCode = codeProperty.GetString();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => this.LocalizeFallback("Неверный email или пароль либо сессия истекла."),
                HttpStatusCode.Forbidden => this.LocalizeFallback("У учетной записи нет доступа к административной панели."),
                _ => this.LocalizeFallback(fallbackMessage)
            };
        }

        return new ApiException(message, response.StatusCode, errorCode: errorCode);
    }

    private string LocalizeFallback(string message)
    {
        return AdminApiLocalization.Localize(this.UiLanguage, message);
    }

    /// <summary> Очищает данные текущей сессии. </summary>
    private void ClearSession(bool deleteStoredSession = true)
    {
        this._sessionVersion++;
        this._tokens = null;
        this._currentProfile = null;
        if (deleteStoredSession)
        {
            this._sessionStore?.Delete();
        }
    }

    /// <summary> Сохраняет только данные, необходимые для следующего обновления токенов. </summary>
    private void SaveSession()
    {
        if (this._tokens is { } tokens)
        {
            this._sessionStore?.Save(new SavedAdminSession(tokens.User.Id, tokens.RefreshToken, tokens.RefreshTokenExpiresAt));
        }
    }

    /// <summary> Выбрасывает исключение, если клиент освобождён. </summary>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(this._disposed, this);

    /// <summary> Тело запроса на вход с почтой и паролем. </summary>
    private sealed record LoginRequest(string Email, string Password);

    /// <summary> Тело запроса обновления токенов. </summary>
    private sealed record RefreshRequest(string UserId, string RefreshToken);

    /// <summary> Тело запроса отзыва токенов. </summary>
    private sealed record RevokeRequest(string UserId, string RefreshToken);

    /// <summary> Сессия запроса отчёта с версией и токенами. </summary>
    private sealed record ReportRequestSession(long Version, ApiTokenResponse Tokens);
}
