using System.Net;
using System.Net.Http.Json;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.Tests.Integration;

internal sealed class DashboardApiHandler : HttpMessageHandler
{
    private int _cancelledBillingRequests;

    public List<string> Requests { get; } = [];

    public bool CanViewBilling { get; set; } = true;

    public bool CommercialEnabled { get; set; } = true;

    public bool CanViewDashboard { get; set; } = true;

    public HttpStatusCode DashboardStatus { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode BillingStatus { get; set; } = HttpStatusCode.OK;

    public bool LegacyBillingContract { get; set; }

    public bool LegacySitePeriod { get; set; }

    public int? ReturnedBillingPeriod { get; set; }

    public HttpStatusCode ProfileStatus { get; set; } = HttpStatusCode.OK;

    public bool DelayBilling { get; set; }

    public TaskCompletionSource BillingStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CancelledBillingRequests => Volatile.Read(ref this._cancelledBillingRequests);

    public int FailedActivationCount { get; set; }

    public bool RevokeBillingOnRequest { get; set; }

    public bool DelayDashboard { get; set; }

    public bool RevokeDashboardOnRequest { get; set; }

    public TaskCompletionSource DashboardStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool DashboardCancelled { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        this.Requests.Add(path);
        if (request.RequestUri.AbsolutePath is "/api/v1/auth/login" or "/api/v1/auth/refresh")
        {
            return DashboardApiHandler.Json(new
            {
                accessToken = "access-token",
                accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                refreshToken = "refresh-token",
                refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                user = new { id = "administrator", email = "contact@example.invalid" }
            });
        }

        if (request.RequestUri.AbsolutePath == "/api/v1/auth/revoke")
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
        {
            if (this.ProfileStatus != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(this.ProfileStatus) { Content = JsonContent.Create(new { message = "Профиль недоступен." }) };
            }

            return DashboardApiHandler.Json(new AdminProfile
            {
                Id = "administrator",
                Email = "contact@example.invalid",
                CommercialEnabled = this.CommercialEnabled,
                Permissions = ["Admin.Access",
                    .. this.CanViewDashboard ? new[] { "Admin.Dashboard.View" } : [],
                    .. this.CanViewBilling ? new[] { "Admin.Billing.View" } : []]
            });
        }

        if (request.RequestUri.AbsolutePath == "/api/v1/admin/dashboard")
        {
            if (this.RevokeDashboardOnRequest)
            {
                this.CanViewDashboard = false;
            }

            if (this.DelayDashboard)
            {
                this.DashboardStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    this.DashboardCancelled = true;
                    throw;
                }
            }

            if (this.DashboardStatus != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(this.DashboardStatus) { Content = JsonContent.Create(new { message = "Отчёт недоступен." }) };
            }

            var periodDays = int.Parse(request.RequestUri.Query.Split('=')[1]);

            return DashboardApiHandler.Json(new AdminDashboard
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Period = new AdminDashboardPeriod
                {
                    Days = periodDays, NewUsers = 12, NewOrganizations = 3, PaymentsCount = 5, PaymentsAmount = 9500,
                    Emails = 28, SupportMessages = periodDays, CatalogSuggestions = 2,
                    SentEmails = this.LegacySitePeriod ? null : periodDays == 1 ? 21 : periodDays == 7 ? 32 : 47,
                    FailedEmails = this.LegacySitePeriod ? null : periodDays == 1 ? 2 : periodDays == 7 ? 3 : 5
                },
                Kpis = new AdminDashboardKpis { UsersTotal = 1234, OrganizationsTotal = 75, VehiclesTotal = 320,
                    Payments30DaysAmount = 45000, SentEmails7Days = 32, FailedEmails7Days = 3, PendingEmails = 4 },
                Attention = new AdminDashboardAttention { NewSupportMessages = 2, FailedPayments = 1 },
                Charts = new AdminDashboardCharts
                {
                    Registrations = [new() { Label = "10:00", Value = 2 }, new() { Label = "11:00", Value = 5 }],
                    Logins = [new() { Label = "10:00", Value = 8 }, new() { Label = "11:00", Value = 12 }],
                    Emails = [new() { Label = "10:00", Value = 6 }, new() { Label = "11:00", Value = 3 }],
                    Maintenances = [new() { Label = "10:00", Value = 1 }, new() { Label = "11:00", Value = 2 }],
                    Payments = [new() { Label = "10:00", Value = 2500 }, new() { Label = "11:00", Value = 7000 }]
                }
            });
        }

        if (request.RequestUri.AbsolutePath == "/api/v1/admin/billing/attention")
        {
            if (this.DelayBilling)
            {
                this.BillingStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref this._cancelledBillingRequests);
                    throw;
                }
            }

            if (this.RevokeBillingOnRequest)
            {
                this.CanViewBilling = false;
            }

            if (this.BillingStatus != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(this.BillingStatus) { Content = JsonContent.Create(new { message = "Платежи недоступны." }) };
            }

            var period = int.Parse(request.RequestUri.Query.Split('=')[1]);

            return DashboardApiHandler.Json(new AdminBillingAttention
            {
                PeriodDays = this.LegacyBillingContract ? null : this.ReturnedBillingPeriod ?? period,
                MissingReceiptCount = this.LegacyBillingContract ? null : period == 1 ? 3 : period,
                OverdueReceiptCount = period == 1 ? 0 : 1,
                FailedActivationCount = this.FailedActivationCount
            });
        }

        throw new InvalidOperationException("Неожиданный запрос: " + path);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
