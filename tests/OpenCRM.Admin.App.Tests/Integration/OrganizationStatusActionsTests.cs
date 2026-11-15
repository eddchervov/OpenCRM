using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class OrganizationStatusActionsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusChangeRequiresReasonAndConfirmationAndRefreshesCardAndList(bool archived)
    {
        using var handler = new OrganizationHandler { Archived = archived };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");
        var model = new OrganizationsViewModel(session);
        await model.ActivateAsync();
        await model.OpenCardCommand.ExecuteAsync(model.Rows.Single());

        if (archived)
        {
            model.RequestRestoreCommand.Execute(null);
        }
        else
        {
            model.RequestArchiveCommand.Execute(null);
            Assert.Contains("30 дней", model.StatusChangeWarning);
        }

        Assert.False(model.ConfirmStatusChangeCommand.CanExecute(null));
        model.StatusChangeReason = "  Ошибка подключения  ";
        Assert.False(model.ConfirmStatusChangeCommand.CanExecute(null));
        model.IsStatusChangeConfirmed = true;
        Assert.True(model.ConfirmStatusChangeCommand.CanExecute(null));
        await model.ConfirmStatusChangeCommand.ExecuteAsync(null);

        Assert.Equal(1, handler.Mutations);
        Assert.Equal(archived ? "restore" : "archive", handler.LastAction);
        Assert.Equal("Ошибка подключения", handler.LastReason);
        Assert.Equal(!archived, model.Organization!.IsArchived);
        Assert.Equal(!archived, model.Rows.Single().Organization.IsArchived);
        Assert.False(model.HasPendingStatusChange);
        Assert.False(model.IsStatusChangeConfirmed);
        Assert.Empty(model.StatusChangeReason);
        Assert.Equal("Статус изменён.", model.StatusChangeMessage);
        Assert.Empty(model.CardErrorMessage);
        await model.DeactivateAsync(true);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ReadOnlyAdministratorAndDeletedOrganizationCannotChangeStatus(bool canManage, bool deleted)
    {
        using var handler = new OrganizationHandler { CanManage = canManage, Deleted = deleted };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");
        var model = new OrganizationsViewModel(session);
        await model.ActivateAsync();
        await model.OpenCardCommand.ExecuteAsync(model.Rows.Single());

        Assert.False(model.RequestArchiveCommand.CanExecute(null));
        Assert.False(model.RequestRestoreCommand.CanExecute(null));
        Assert.False(model.ConfirmStatusChangeCommand.CanExecute(null));
        Assert.Equal(0, handler.Mutations);
        await model.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task LostResponseClearsConfirmationAndDoesNotRepeatMutation()
    {
        using var handler = new OrganizationHandler { LoseResponse = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");
        var model = new OrganizationsViewModel(session);
        await model.ActivateAsync();
        await model.OpenCardCommand.ExecuteAsync(model.Rows.Single());
        model.RequestArchiveCommand.Execute(null);
        model.StatusChangeReason = "Проверка";
        model.IsStatusChangeConfirmed = true;
        await model.ConfirmStatusChangeCommand.ExecuteAsync(null);

        Assert.Equal(1, handler.Mutations);
        Assert.False(model.HasPendingStatusChange);
        Assert.False(model.ConfirmStatusChangeCommand.CanExecute(null));
        Assert.False(model.IsChangingStatus);
        Assert.Contains("проверьте статус", model.CardErrorMessage);
        Assert.False(model.RequestArchiveCommand.CanExecute(null));
        Assert.False(model.RequestRestoreCommand.CanExecute(null));
        await model.RefreshCardCommand.ExecuteAsync(null);
        Assert.True(model.Organization!.IsArchived);
        Assert.True(model.RequestRestoreCommand.CanExecute(null));
        Assert.Equal(1, handler.Mutations);
        await model.DeactivateAsync(true);
    }

    private sealed class OrganizationHandler : HttpMessageHandler
    {
        private readonly Guid _organizationId = Guid.NewGuid();

        public bool Archived { get; set; }
        public bool Deleted { get; init; }
        public bool CanManage { get; init; } = true;
        public bool LoseResponse { get; init; }
        public int Mutations { get; private set; }
        public string? LastAction { get; private set; }
        public string? LastReason { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return Json(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    tokenType = "Bearer", user = new { id = "admin", roles = new[] { "Admin" } }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return Json(new
                {
                    id = "admin", roles = new[] { "Admin" },
                    permissions = this.CanManage
                        ? new[] { "Admin.Access", "Admin.Users.View", "Admin.Users.Manage" }
                        : new[] { "Admin.Access", "Admin.Users.View" }
                });
            }

            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal($"/api/v1/admin/organizations/{this._organizationId:D}/", path[..(path.LastIndexOf('/') + 1)]);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                this.LastAction = path[(path.LastIndexOf('/') + 1)..];
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                this.LastReason = body.RootElement.GetProperty("reason").GetString();
                this.Mutations++;
                this.Archived = this.LastAction == "archive";
                if (this.LoseResponse)
                {
                    throw new HttpRequestException("Ответ потерян.");
                }

                return Json(new { message = "Статус изменён." });
            }

            var organization = new
            {
                id = this._organizationId, name = "Чужая студия", ownerUserId = "other-user",
                owner = "Другой владелец", isArchived = this.Archived, isDeleted = this.Deleted,
                isActive = !this.Archived && !this.Deleted
            };

            return path == "/api/v1/admin/organizations"
                ? Json(new { organizations = new[] { organization }, totalCount = 1, page = 1, pageSize = 50 })
                : Json(organization);
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
