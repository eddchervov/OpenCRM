using System.Net;
using System.Net.Http.Json;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.Tests.Integration;

internal sealed class UsersApiHandler : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    public bool CanViewUsers { get; set; } = true;

    public bool CanViewBilling { get; set; } = true;

    public bool CommercialEnabled { get; set; } = true;

    public HttpStatusCode UsersStatus { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode CardStatus { get; set; } = HttpStatusCode.OK;

    public bool DelayCard { get; set; }

    public bool DelayList { get; set; }

    public bool HoldCancellation { get; set; }

    public TaskCompletionSource ReleaseCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CanViewDashboard { get; set; }

    public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool RequestCancelled { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this.Requests.Add(request.RequestUri!.PathAndQuery);
        var path = request.RequestUri.AbsolutePath;
        if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
        {
            return UsersApiHandler.Json(new
            {
                accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                user = new { id = "admin", email = "contact@example.invalid" }
            });
        }

        if (path == "/api/v1/auth/revoke")
        {
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (path == "/api/v1/admin/current-user")
        {
            return UsersApiHandler.Json(new AdminProfile
            {
                CommercialEnabled = this.CommercialEnabled,
                Id = "admin", Email = "contact@example.invalid",
                Permissions = ["Admin.Access", .. this.CanViewDashboard ? new[] { "Admin.Dashboard.View" } : [], .. this.CanViewUsers ? new[] { "Admin.Users.View" } : [],
                    .. this.CanViewBilling ? new[] { "Admin.Billing.View" } : []]
            });
        }

        var isList = path == "/api/v1/admin/users";
        var isCard = path.EndsWith("/overview", StringComparison.Ordinal);
        if ((isList && this.DelayList) || (isCard && this.DelayCard))
        {
            this.RequestStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                this.RequestCancelled = true;
                if (this.HoldCancellation)
                {
                    await this.ReleaseCancellation.Task;
                }

                throw;
            }
        }

        var status = isList ? this.UsersStatus : this.CardStatus;
        if (status != HttpStatusCode.OK)
        {
            return new HttpResponseMessage(status) { Content = JsonContent.Create(new { message = "Данные недоступны." }) };
        }

        if (isList)
        {
            var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
            var page = int.Parse(query["page"]);
            var size = int.Parse(query["pageSize"]);
            var searched = query.ContainsKey("search");
            return UsersApiHandler.Json(new AdminUsersResponse
            {
                Page = page, PageSize = size, AllCount = searched ? 1 : 62,
                Users = Enumerable.Range((page - 1) * size + 1, searched ? 1 : Math.Min(size, 62 - (page - 1) * size))
                    .Select(index => new AdminUserListItem
                    {
                        Id = "user-" + index, Email = index == 1 ? "contact@example.invalid" : $"user{index}@example.com",
                        Phone = "+7 000 000-00-00", EmailConfirmed = true,
                        DatetimeCreation = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc),
                        LastLoginDate = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
                        CountVehicles = 12, CountMaintenance = 45, OrganizationsCount = 2,
                        EffectivePlanName = "Бизнес", FirstUtmSource = "yandex"
                    }).ToArray()
            });
        }

        if (isCard)
        {
            return UsersApiHandler.Json(new AdminUserOverview
            {
                GeneratedAt = DateTime.UtcNow,
                Profile = new AdminUserOverviewProfile
                {
                    Id = path.Split('/')[5], Email = "contact@example.invalid", EmailConfirmed = true,
                    Phone = "+7 000 000-00-00", DatetimeCreation = new DateTime(2026, 10, 12),
                    LastLoginDate = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc),
                    CountLogins = 17, HasPassword = true, LockoutEnabled = true,
                    FirstUtmSource = "yandex",
                    FirstVisitAt = new DateTime(2026, 10, 12), PersonalDataProcessingConsentAccepted = true
                },
                Kpis = new AdminUserOverviewKpis { OrganizationsTotal = 2, PaymentsTotal = 3, PaymentsAmount = 4500 },
                Organizations = [new() { OrganizationName = "Detail Studio", ScenarioName = "Детейлинг", RoleName = "Владелец",
                    MembersCount = 5, VehiclesCount = 12, ClientsCount = 48, CurrentPlanName = "Бизнес", HasActiveSubscription = true, IsActive = true },
                    new() { OrganizationName = "Вторая организация", ScenarioName = "СТО", RoleName = "Сотрудник",
                        CurrentPlanName = "СТО бизнес", HasActiveSubscription = true, IsActive = true }],
                Subscriptions = new() { Organizations = [
                    new() { OrganizationName = "Detail Studio", PlanName = "Бизнес", IsActive = true },
                    new() { OrganizationName = "Вторая организация", PlanName = "СТО бизнес", IsActive = false }] },
                Payments = [new() { PlanName = "Бизнес", Amount = 1500, PaymentStatusName = "Оплачен", PaymentMethodName = "Карта", PaymentDate = DateTime.UtcNow }],
                Vehicles = [new() { Title = "BMW 320", RegistrationPlate = "А123АА54", CurrentMileage = 75000, MileageUnitName = "км", MaintenancesCount = 4 }],
                SupportMessages = [new() { Topic = "Настройка студии", Text = string.Concat(Enumerable.Repeat("Как добавить сотрудника в студию? ", 40)), StatusName = "Новое", CreatedAt = DateTime.UtcNow }],
                Timeline = [new() { Type = "payment", Title = "Оплата тарифа", Label = "Платёж", Date = DateTime.UtcNow },
                    new() { Type = "organization", Title = "Detail Studio", Label = "Организация", Date = DateTime.UtcNow }]
            });
        }

        throw new InvalidOperationException("Неожиданный запрос: " + path);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
