using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api.Tests.Unit;

public sealed class AdminSessionClientTests
{
    [Fact]
    public async Task LoginRejectsUnauthorizedResponse()
    {
        using var client = CreateClient((_, _) => JsonResponse(HttpStatusCode.Unauthorized, new { message = "Неверный email или пароль." }));
        using var session = new AdminSessionClient(client);

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.LoginAsync("contact@example.invalid", "password"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.False(session.HasSession);
    }

    [Fact]
    public async Task LoginRevokesTokenAndRejectsForbiddenProfile()
    {
        var revokeCalled = false;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.Forbidden, new { message = "Нет доступа." }),
            "/api/v1/auth/revoke" => RevokeResponse(() => revokeCalled = true),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.LoginAsync("contact@example.invalid", "password"));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.False(session.HasSession);
        Assert.Null(session.CurrentProfile);
        Assert.True(revokeCalled);
    }

    [Fact]
    public async Task LoginRejectsMalformedOrMissingTokens()
    {
        using var client = CreateClient((_, _) => JsonResponse(HttpStatusCode.OK, new
        {
            accessToken = "access-token",
            refreshToken = "refresh",
            tokenType = "Bearer",
            user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = Array.Empty<string>() }
        }));
        using var session = new AdminSessionClient(client);

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.LoginAsync("contact@example.invalid", "password"));

        Assert.Contains("некорректные данные авторизации", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.HasSession);
    }

    [Fact]
    public async Task LoginReturnsProfileOnlyAfterPermissionCheck()
    {
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);

        var profile = await session.LoginAsync("contact@example.invalid", "password");

        Assert.True(session.HasSession);
        Assert.Same(profile, session.CurrentProfile);
        Assert.Equal("admin-1", profile.Id);
        Assert.Equal("contact@example.invalid", profile.Email);
        Assert.Contains("Admin.Access", profile.Permissions);
        Assert.True(profile.CommercialEnabled);
    }

    [Fact]
    public async Task LoginAllowsSystemProfileWithoutExplicitPermission()
    {
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile(isSystem: true, permissions: [])),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);

        var profile = await session.LoginAsync("contact@example.invalid", "password");

        Assert.True(profile.IsSystem);
        Assert.True(session.HasSession);
    }

    [Fact]
    public async Task ProfileUnauthorizedRefreshesRotatedTokensAndRetriesOnlyOnce()
    {
        var profileRequests = 0;
        var authorizationHeaders = new List<string?>();
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (path == "/api/v1/admin/current-user")
            {
                profileRequests++;
                authorizationHeaders.Add(request.Headers.Authorization?.Parameter);
                return profileRequests == 1
                    ? JsonResponse(HttpStatusCode.OK, CreateProfile())
                    : profileRequests == 2
                        ? JsonResponse(HttpStatusCode.Unauthorized, new { message = "Токен истек." })
                        : JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            if (path == "/api/v1/auth/refresh")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens(accessToken: "rotated-access", refreshToken: "rotated-refresh"));
            }

            throw new InvalidOperationException("Неожиданный запрос.");
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var profile = await session.GetProfileAsync();

        Assert.Equal("admin-1", profile.Id);
        Assert.Equal(3, profileRequests);
        Assert.Equal(new[] { "access-token", "access-token", "rotated-access" }, authorizationHeaders);
        Assert.True(session.HasSession);
    }

    [Fact]
    public async Task FailedRefreshClearsSession()
    {
        var profileRequests = 0;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => ++profileRequests == 1
                ? JsonResponse(HttpStatusCode.OK, CreateProfile())
                : JsonResponse(HttpStatusCode.Unauthorized, new { message = "Токен истек." }),
            "/api/v1/auth/refresh" => JsonResponse(HttpStatusCode.Unauthorized, new { message = "Refresh недействителен." }),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        await Assert.ThrowsAsync<ApiException>(() => session.GetProfileAsync());

        Assert.False(session.HasSession);
        Assert.Null(session.CurrentProfile);
    }

    [Fact]
    public async Task DashboardRequestUsesBearerAndDeserializesDashboard()
    {
        string? requestedPath = null;
        string? authorization = null;
        using var client = CreateClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            requestedPath = request.RequestUri.PathAndQuery;
            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse(HttpStatusCode.OK, new
            {
                generatedAt = DateTimeOffset.UtcNow,
                period = new { days = 7, newUsers = 3 },
                kpis = new { usersTotal = 12 },
                attention = new { newSupportMessages = 2 },
                charts = new { registrations = new[] { new { date = "2026-10-14T00:00:00Z", label = "14 окт", value = 3 } } },
                recent = new { }
            });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var dashboard = await session.GetDashboardAsync(7);

        Assert.Equal("/api/v1/admin/dashboard?periodDays=7", requestedPath);
        Assert.Equal("Bearer access-token", authorization);
        Assert.Equal(7, dashboard.Period.Days);
        Assert.Equal(12, dashboard.Kpis.UsersTotal);
        Assert.Equal(3, dashboard.Charts.Registrations[0].Value);
    }

    [Fact]
    public async Task DashboardForbiddenDoesNotClearSession()
    {
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/dashboard" => JsonResponse(HttpStatusCode.Forbidden, new { message = "Нет права просмотра сводки." }),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.GetDashboardAsync(1));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.True(session.HasSession);
        Assert.NotNull(session.CurrentProfile);
    }

    [Fact]
    public async Task BillingAttentionRequestUsesBearerAndDeserializesCounts()
    {
        string? requestedPath = null;
        string? authorization = null;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/billing/attention" => CaptureBillingAttention(request),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var attention = await session.GetBillingAttentionAsync(7);

        Assert.Equal("/api/v1/admin/billing/attention?periodDays=7", requestedPath);
        Assert.Equal(7, attention.PeriodDays);
        Assert.Equal(6, attention.MissingReceiptCount);
        Assert.Equal("Bearer access-token", authorization);
        Assert.Equal(4, attention.OverdueReceiptCount);
        Assert.Equal(2, attention.FailedActivationCount);

        HttpResponseMessage CaptureBillingAttention(HttpRequestMessage request)
        {
            requestedPath = request.RequestUri!.PathAndQuery;
            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse(HttpStatusCode.OK, new { periodDays = 7, missingReceiptCount = 6, overdueReceiptCount = 4, failedActivationCount = 2 });
        }
    }

    [Fact]
    public async Task GetBillingReadsSnapshotWithoutRequestingPurchases()
    {
        string? requestedPath = null;
        string? authorization = null;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/billing" => CaptureBilling(request),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var billing = await session.GetBillingAsync();

        Assert.Equal("/api/v1/admin/billing", requestedPath);
        Assert.Equal("Bearer access-token", authorization);
        Assert.Equal(2, billing.Payments.Count);
        Assert.Equal(1800m, billing.Kpis.Payments30DaysAmount);
        Assert.Equal("OpenCRM Detailing", Assert.Single(billing.Subscriptions.Organizations).OrganizationName);
        Assert.Empty(billing.Subscriptions.Users);

        HttpResponseMessage CaptureBilling(HttpRequestMessage request)
        {
            requestedPath = request.RequestUri!.PathAndQuery;
            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse(HttpStatusCode.OK, new
            {
                generatedAt = "2026-10-14T00:00:00Z",
                kpis = new { payments30DaysAmount = 1800m, activeOrganizationSubscriptions = 1 },
                payments = new[]
                {
                    new { id = 10, amount = 1000m, paymentStatus = "completed" },
                    new { id = 11, amount = 800m, paymentStatus = "pending" }
                },
                subscriptions = new
                {
                    users = Array.Empty<object>(),
                    organizations = new[]
                    {
                        new
                        {
                            id = 45,
                            organizationId = "a136b684-94d2-4781-9db6-cfa2a72db719",
                            organizationName = "OpenCRM Detailing",
                            planName = "Студия"
                        }
                    }
                }
            });
        }
    }

    [Fact]
    public async Task GetBillingPaymentLoadsPaymentDetailsOnDemand()
    {
        string? requestedPath = null;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/billing/payments/73" => CapturePayment(request),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var payment = await session.GetBillingPaymentAsync(73);

        Assert.Equal("/api/v1/admin/billing/payments/73", requestedPath);
        Assert.Equal(73, payment.Id);
        Assert.Equal("contact@example.invalid", payment.UserEmail);
        Assert.Equal("completed", payment.PaymentStatus);
        Assert.Equal(1000m, payment.Amount);
        Assert.Equal("Профи", payment.PlanName);

        HttpResponseMessage CapturePayment(HttpRequestMessage request)
        {
            requestedPath = request.RequestUri!.PathAndQuery;
            return JsonResponse(HttpStatusCode.OK, new
            {
                id = 73,
                userId = "user-1",
                user = "contact@example.invalid",
                userEmail = "contact@example.invalid",
                subscriptionPlanId = 4,
                planName = "Профи",
                amount = 1000m,
                paymentStatus = "completed",
                statusName = "Оплачен"
            });
        }
    }

    [Fact]
    public async Task BillingAuditRequestsUsePaymentRouteAndFixedOrganizationScope()
    {
        var requestedPaths = new List<string>();
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/billing/payments/73/status-audits" => CaptureAudits(request, new
            {
                id = 4L,
                previousStatus = "pending",
                newStatus = "completed",
                source = "yookassa",
                changedBy = "Платёжный шлюз"
            }),
            "/api/v1/admin/billing/subscriptions/organizations/45/audits" => CaptureAudits(request, new
            {
                id = 9L,
                subscriptionId = 45,
                action = "extended",
                reason = "Продление администратором"
            }),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var paymentAudits = await session.GetBillingPaymentAuditsAsync(73);
        var subscriptionAudits = await session.GetBillingSubscriptionAuditsAsync(45);

        Assert.Equal(
            [
                "/api/v1/admin/billing/payments/73/status-audits",
                "/api/v1/admin/billing/subscriptions/organizations/45/audits"
            ],
            requestedPaths);
        Assert.Equal("completed", Assert.Single(paymentAudits).NewStatus);
        Assert.Equal("yookassa", paymentAudits[0].Source);
        Assert.Equal("extended", Assert.Single(subscriptionAudits).Action);
        Assert.Equal("Продление администратором", subscriptionAudits[0].Reason);

        HttpResponseMessage CaptureAudits(HttpRequestMessage request, object audit)
        {
            requestedPaths.Add(request.RequestUri!.PathAndQuery);
            return JsonResponse(HttpStatusCode.OK, new[] { audit });
        }
    }

    [Fact]
    public async Task GetUsersEncodesFiltersAndDeserializesPage()
    {
        string? requestedPath = null;
        string? authorization = null;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/users" => CaptureUsers(request),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var users = await session.GetUsersAsync(
            search: "иван + contact@example.invalid",
            sort: AdminUsersSort.ByLastLoginDesc,
            includeDeleted: true,
            page: 2,
            pageSize: 50,
            utmSource: "Google / Метрика");

        Assert.Equal(
            "/api/v1/admin/users?sort=5&includeDeleted=true&page=2&pageSize=50&search=%D0%B8%D0%B2%D0%B0%D0%BD%20%2B%20contact%40example.invalid&utmSource=Google%20%2F%20%D0%9C%D0%B5%D1%82%D1%80%D0%B8%D0%BA%D0%B0",
            requestedPath);
        Assert.Equal("Bearer access-token", authorization);
        Assert.Equal(72, users.AllCount);
        Assert.Equal(2, users.Page);
        Assert.Equal(50, users.PageSize);
        var user = Assert.Single(users.Users);
        Assert.Equal("user-1", user.Id);
        Assert.Equal("contact@example.invalid", user.Email);
        Assert.Equal(4, user.CountVehicles);

        HttpResponseMessage CaptureUsers(HttpRequestMessage request)
        {
            requestedPath = request.RequestUri!.PathAndQuery;
            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse(HttpStatusCode.OK, new
            {
                users = new[]
                {
                    new
                    {
                        id = "user-1",
                        email = "contact@example.invalid",
                        countVehicles = 4,
                        currentSubscriptionName = "Профи"
                    }
                },
                allCount = 72,
                page = 2,
                pageSize = 50
            });
        }
    }

    [Fact]
    public async Task GetUsersOmitsEmptyOptionalFilters()
    {
        string? requestedPath = null;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/users" => CaptureUsers(request),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        await session.GetUsersAsync(search: "", utmSource: null);

        Assert.Equal("/api/v1/admin/users?sort=1&includeDeleted=false&page=1&pageSize=25", requestedPath);

        HttpResponseMessage CaptureUsers(HttpRequestMessage request)
        {
            requestedPath = request.RequestUri!.PathAndQuery;
            return JsonResponse(HttpStatusCode.OK, new { users = Array.Empty<object>(), allCount = 0, page = 1, pageSize = 25 });
        }
    }

    [Fact]
    public async Task GetUserOverviewEncodesUserIdAndDeserializesTabbedData()
    {
        string? requestedPath = null;
        string? authorization = null;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/users/user%20%2Bid/overview" => CaptureOverview(request),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var overview = await session.GetUserOverviewAsync("user +id");

        Assert.Equal("/api/v1/admin/users/user%20%2Bid/overview", requestedPath);
        Assert.Equal("Bearer access-token", authorization);
        Assert.Equal("user-1", overview.Profile.Id);
        Assert.Equal(3, overview.Kpis.VehiclesTotal);
        Assert.Equal(1, Assert.Single(overview.Charts.Registrations).Value);
        Assert.Equal(1, Assert.Single(overview.Vehicles).MaintenancesCount);
        Assert.Equal("Профи", Assert.Single(overview.Subscriptions.Users).PlanName);
        Assert.Equal("payment", Assert.Single(overview.Timeline).Type);

        HttpResponseMessage CaptureOverview(HttpRequestMessage request)
        {
            requestedPath = request.RequestUri!.PathAndQuery;
            authorization = request.Headers.Authorization?.ToString();
            return JsonResponse(HttpStatusCode.OK, new
            {
                generatedAt = "2026-10-14T00:00:00Z",
                profile = new { id = "user-1" },
                kpis = new { vehiclesTotal = 3 },
                charts = new { registrations = new[] { new { label = "Окт", value = 1 } } },
                vehicles = new[] { new { id = "78f8169f-9e9e-44c2-b891-f252229560b7", title = "Toyota", maintenancesCount = 1 } },
                subscriptions = new { users = new[] { new { id = 12, planName = "Профи" } } },
                timeline = new[] { new { type = "payment", title = "Оплата", key = "payment:12" } }
            });
        }
    }

    [Fact]
    public async Task ProfileWithoutAdminAccessIsDeniedAndRefreshTokenRevoked()
    {
        var revokeCalled = false;
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile(permissions: [])),
            "/api/v1/auth/revoke" => RevokeResponse(() => revokeCalled = true),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.LoginAsync("contact@example.invalid", "password"));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("Admin.Access", exception.Message);
        Assert.False(session.HasSession);
        Assert.True(revokeCalled);
    }

    [Fact]
    public async Task LogoutClearsLocalSessionWhenRevokeFails()
    {
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/auth/revoke" => JsonResponse(HttpStatusCode.ServiceUnavailable, new { message = "Сервер недоступен." }),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.LogoutAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.False(session.HasSession);
        Assert.Null(session.CurrentProfile);
    }

    [Fact]
    public async Task LogoutRefreshesExpiredAccessTokenAndRevokesRotatedTokens()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var revokeAuthorization = string.Empty;
        string? refreshBody = null;
        string? revokeBody = null;
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens(
                    accessTokenExpiresAt: timeProvider.GetUtcNow().AddSeconds(40),
                    now: timeProvider.GetUtcNow()));
            }

            if (path == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            if (path == "/api/v1/auth/refresh")
            {
                refreshBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonResponse(HttpStatusCode.OK, CreateTokens(
                    accessToken: "rotated-access",
                    refreshToken: "rotated-refresh",
                    now: timeProvider.GetUtcNow()));
            }

            if (path == "/api/v1/auth/revoke")
            {
                revokeAuthorization = request.Headers.Authorization?.Parameter ?? string.Empty;
                revokeBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException("Неожиданный запрос.");
        });
        using var session = new AdminSessionClient(client, timeProvider);
        await session.LoginAsync("contact@example.invalid", "password");
        timeProvider.Advance(TimeSpan.FromSeconds(45));

        await session.LogoutAsync();

        Assert.False(session.HasSession);
        Assert.Equal("rotated-access", revokeAuthorization);
        using var refreshRequest = JsonDocument.Parse(refreshBody!);
        Assert.Equal("refresh-token", refreshRequest.RootElement.GetProperty("refreshToken").GetString());
        using var revokeRequest = JsonDocument.Parse(revokeBody!);
        Assert.Equal("rotated-refresh", revokeRequest.RootElement.GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task LogoutClearsLocalSessionWhenRefreshFails()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens(
                accessTokenExpiresAt: timeProvider.GetUtcNow().AddSeconds(40),
                now: timeProvider.GetUtcNow())),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/auth/refresh" => JsonResponse(HttpStatusCode.Unauthorized, new { message = "Refresh недействителен." }),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client, timeProvider);
        await session.LoginAsync("contact@example.invalid", "password");
        timeProvider.Advance(TimeSpan.FromSeconds(45));

        await Assert.ThrowsAsync<ApiException>(() => session.LogoutAsync());

        Assert.False(session.HasSession);
        Assert.Null(session.CurrentProfile);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };
    }

    private static HttpClient CreateAsyncClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new AsyncFakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value)
    {
        return new HttpResponseMessage(statusCode) { Content = JsonContent.Create(value) };
    }

    private static HttpResponseMessage RevokeResponse(Action callback)
    {
        callback();
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private static object CreateTokens(
        string accessToken = "access-token",
        string refreshToken = "refresh-token",
        DateTimeOffset? accessTokenExpiresAt = null,
        DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        return new
        {
            accessToken,
            accessTokenExpiresAt = accessTokenExpiresAt ?? currentTime.AddHours(1),
            refreshToken,
            refreshTokenExpiresAt = currentTime.AddDays(10),
            tokenType = "Bearer",
            user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
        };
    }

    private static object CreateProfile(bool isSystem = false, IReadOnlyList<string>? permissions = null, bool commercialEnabled = true)
    {
        return new
        {
            id = "admin-1",
            email = "contact@example.invalid",
            roles = new[] { "Admin" },
            permissions = permissions ?? ["Admin.Access"],
            isSystem,
            commercialEnabled
        };
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request, cancellationToken));
        }
    }

    private sealed class AsyncFakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => this._utcNow;

        public void Advance(TimeSpan duration)
        {
            this._utcNow += duration;
        }
    }
}
