using System.Net;
using System.Net.Http.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class RolesTests
{
    [Fact]
    public async Task PermissionDependenciesAndLockedRoleAreEnforcedAndEditableRoleCanBeSaved()
    {
        using var handler = new RolesApiHandler();
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);
        await model.ActivateAsync();

        var protectedRole = model.Roles.Single(role => role.Id == "admin-role");
        model.SelectedRole = protectedRole;
        Assert.True(protectedRole.IsLocked);
        Assert.False(model.CanEditPermissions);

        model.SelectedRole = model.Roles.Single(role => role.Id == "manager-role");
        var managePermission = model.PermissionGroups.SelectMany(group => group.Permissions)
            .Single(permission => permission.Code == "Admin.Users.Manage");
        managePermission.IsSelected = true;
        model.TogglePermissionCommand.Execute(managePermission);

        Assert.Contains(model.PermissionGroups.SelectMany(group => group.Permissions), permission => permission.Code == "Admin.Users.View" && permission.IsSelected);
        Assert.Contains(model.PermissionGroups.SelectMany(group => group.Permissions), permission => permission.Code == "Admin.Access" && permission.IsSelected);
        await model.SavePermissionsCommand.ExecuteAsync(null);

        Assert.Contains("\"permissionCodes\"", handler.LastBody);
        Assert.Equal("Права сохранены.", model.Message);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task MemberRemovalRequiresConfirmationAndProtectedCurrentUserCannotBeRemoved()
    {
        using var handler = new RolesApiHandler();
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRole = model.Roles.Single(role => role.Id == "manager-role");
        model.SelectTabCommand.Execute(true);
        var member = Assert.Single(model.MembersList);

        model.RequestRemoveMemberCommand.Execute(member);

        Assert.True(model.IsRemovalConfirmationOpen);
        Assert.Equal(0, handler.RemoveRequests);
        await model.ConfirmRemoveMemberCommand.ExecuteAsync(null);
        Assert.Equal(1, handler.RemoveRequests);
        Assert.False(model.IsRemovalConfirmationOpen);

        model.SelectedRole = model.Roles.Single(role => role.Id == "admin-role");
        model.SelectTabCommand.Execute(true);
        model.RequestRemoveMemberCommand.Execute(Assert.Single(model.MembersList));

        Assert.False(model.IsRemovalConfirmationOpen);
        Assert.Equal(1, handler.RemoveRequests);
        await model.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task RolesViewRendersAtDesktopAndCompactWidths()
    {
        using var handler = new RolesApiHandler();
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);
        await model.ActivateAsync();
        var window = new Window { Width = 1160, Height = 840, Content = new RolesView { DataContext = model } };
        window.Show();
        RolesTests.SaveScreenshot(window, "roles-list-1160.png");
        window.Width = 900;
        RolesTests.SaveScreenshot(window, "roles-list-900.png");
        await model.DeactivateAsync(true);
        window.Close();
    }

    [Fact]
    public async Task DeactivationCancelsAnInFlightMemberLoadAndClearsTheView()
    {
        using var handler = new RolesApiHandler { DelayMembers = true };
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRole = model.Roles.Single(role => role.Id == "manager-role");
        model.SelectTabCommand.Execute(true);
        await handler.MemberRequestStarted.Task;

        await model.DeactivateAsync(true);

        Assert.Empty(model.Roles);
        Assert.Empty(model.MembersList);
        handler.ReleaseMemberRequest.TrySetResult();
    }

    [Fact]
    public async Task FailedRoleRefreshAndPermissionSaveShowAnErrorWithoutEscapingCommands()
    {
        using var handler = new RolesApiHandler { FailRoleList = true };
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);

        await model.ActivateAsync();

        Assert.True(model.HasError);
        handler.FailRoleList = false;
        await model.RefreshCommand.ExecuteAsync(null);
        handler.FailPermissionSave = true;
        model.SelectedRole = model.Roles.Single(role => role.Id == "manager-role");
        await model.SavePermissionsCommand.ExecuteAsync(null);

        Assert.True(model.HasError);
        Assert.Contains("связаться с сервером", model.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task MemberRequestTimeoutIsShownAsAnError()
    {
        using var handler = new RolesApiHandler { TimeoutMembers = true };
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRole = model.Roles.Single(role => role.Id == "manager-role");
        model.SelectTabCommand.Execute(true);
        await RolesTests.WaitUntilAsync(() => !model.IsMembersLoading);

        Assert.True(model.HasError);
        Assert.Contains("ожидания", model.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task ChangingRoleDuringMemberLoadDiscardsPreviousRoleMembers()
    {
        using var handler = new RolesApiHandler { DelayManagerMembers = true };
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRole = model.Roles.Single(role => role.Id == "manager-role");
        model.SelectTabCommand.Execute(true);
        await handler.ManagerMemberRequestStarted.Task;

        Assert.Empty(model.MembersList);
        Assert.Empty(model.AvailableMembers);
        model.SelectedRole = model.Roles.Single(role => role.Id == "admin-role");
        await RolesTests.WaitUntilAsync(() => !model.IsMembersLoading);
        var oldRoleMember = new RoleMemberItem(new AdminRoleMember
        {
            UserId = "manager-user",
            DisplayName = "manager-user",
            HasRole = true
        });
        model.SelectedMember = oldRoleMember;
        model.RequestRemoveMemberCommand.Execute(oldRoleMember);

        Assert.DoesNotContain(model.MembersList, member => member.UserId == oldRoleMember.UserId);
        Assert.False(model.AddMemberCommand.CanExecute(null));
        Assert.False(model.IsRemovalConfirmationOpen);
        Assert.False(model.ConfirmRemoveMemberCommand.CanExecute(null));
        await model.DeactivateAsync(true);
        handler.ReleaseManagerMemberRequest.TrySetResult();
    }

    [Fact]
    public async Task DelayedNetworkFailureAfterRoleSwitchDoesNotEscapeDeactivation()
    {
        using var handler = new RolesApiHandler { DelayManagerMembers = true, FailManagerAfterCancellation = true };
        using var client = await RolesTests.CreateClientAsync(handler);
        var model = new RolesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRole = model.Roles.Single(role => role.Id == "manager-role");
        model.SelectTabCommand.Execute(true);
        await handler.ManagerMemberRequestStarted.Task;

        model.SelectedRole = model.Roles.Single(role => role.Id == "admin-role");
        await RolesTests.WaitUntilAsync(() => !model.IsMembersLoading);
        handler.ReleaseManagerMemberRequest.TrySetResult();

        await model.DeactivateAsync(true);

        Assert.Empty(model.Roles);
    }

    private static async Task<AdminSessionClient> CreateClientAsync(RolesApiHandler handler)
    {
        var client = new AdminSessionClient(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });
        await client.LoginAsync("contact@example.invalid", "password");
        return client;
    }

    private static void SaveScreenshot(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(Path.Combine(directory, fileName));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class RolesApiHandler : HttpMessageHandler
    {
        public bool DelayMembers { get; init; }

        public bool DelayManagerMembers { get; init; }

        public bool TimeoutMembers { get; init; }

        public bool FailManagerAfterCancellation { get; init; }

        public bool FailRoleList { get; set; }

        public bool FailPermissionSave { get; set; }

        public TaskCompletionSource MemberRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMemberRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ManagerMemberRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseManagerMemberRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RemoveRequests { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        private readonly HashSet<string> _assignedMemberIds = ["manager-user", "admin-user"];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return RolesApiHandler.Json(HttpStatusCode.OK, new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid", roles = new[] { "Admin" } }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return RolesApiHandler.Json(HttpStatusCode.OK, new AdminProfile
                {
                    Id = "admin",
                    Email = "contact@example.invalid",
                    Roles = ["Admin"],
                    Permissions = ["Admin.Access", "Admin.Permissions.Manage"]
                });
            }

            if (path == "/api/v1/admin/roles" && request.Method == HttpMethod.Get)
            {
                if (this.FailRoleList)
                {
                    throw new HttpRequestException("Сетевой сбой при загрузке ролей.");
                }

                return RolesApiHandler.Json(HttpStatusCode.OK, new
                {
                    roles = new[]
                    {
                        new { id = "admin-role", name = "Admin", usersCount = 1, isProtected = true },
                        new { id = "manager-role", name = "Manager", usersCount = this._assignedMemberIds.Contains("manager-user") ? 1 : 0, isProtected = false }
                    }
                });
            }

            if (path == "/api/v1/admin/permissions")
            {
                return RolesApiHandler.Json(HttpStatusCode.OK, new
                {
                    permissions = new[]
                    {
                        new { code = "Admin.Access", name = "Доступ", group = "Система" },
                        new { code = "Admin.Users.View", name = "Просмотр пользователей", group = "Пользователи" },
                        new { code = "Admin.Users.Manage", name = "Управление пользователями", group = "Пользователи" }
                    },
                    roles = new[]
                    {
                        new { roleId = "admin-role", roleName = "Admin", isLocked = true, permissionCodes = new[] { "Admin.Access" } },
                        new { roleId = "manager-role", roleName = "Manager", isLocked = false, permissionCodes = new[] { "Admin.Access", "Admin.Users.View" } }
                    }
                });
            }

            if (path.EndsWith("/members", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                if (this.DelayMembers)
                {
                    this.MemberRequestStarted.TrySetResult();
                    await this.ReleaseMemberRequest.Task.WaitAsync(cancellationToken);
                }

                var isAdminRole = path.Contains("admin-role", StringComparison.Ordinal);
                if (!isAdminRole && this.DelayManagerMembers)
                {
                    this.ManagerMemberRequestStarted.TrySetResult();
                    try
                    {
                        await this.ReleaseManagerMemberRequest.Task.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (this.FailManagerAfterCancellation)
                    {
                        await this.ReleaseManagerMemberRequest.Task;
                        throw new HttpRequestException("Отложенный сетевой сбой.");
                    }
                }

                if (this.TimeoutMembers)
                {
                    throw new TaskCanceledException("Истекло время ожидания ответа.");
                }

                var roleId = isAdminRole ? "admin-role" : "manager-role";
                var assignedId = isAdminRole ? "admin-user" : "manager-user";
                return RolesApiHandler.Json(HttpStatusCode.OK, new
                {
                    roleId,
                    roleName = isAdminRole ? "Admin" : "Manager",
                    isProtected = isAdminRole,
                    users = new[]
                    {
                        new { userId = assignedId, displayName = assignedId, email = assignedId + "@example.test", phone = "", hasRole = this._assignedMemberIds.Contains(assignedId), isCurrentUser = isAdminRole },
                        new { userId = "available-user", displayName = "available-user", email = "contact@example.invalid", phone = "", hasRole = false, isCurrentUser = false }
                    }
                });
            }

            if (path == "/api/v1/admin/permissions/roles/manager-role" && request.Method == HttpMethod.Put)
            {
                if (this.FailPermissionSave)
                {
                    throw new HttpRequestException("Сетевой сбой при сохранении прав.");
                }

                this.LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return RolesApiHandler.Json(HttpStatusCode.OK, new { message = "Права сохранены." });
            }

            if (path == "/api/v1/admin/roles/manager-role/members/manager-user" && request.Method == HttpMethod.Delete)
            {
                this.RemoveRequests++;
                this._assignedMemberIds.Remove("manager-user");
                return RolesApiHandler.Json(HttpStatusCode.OK, new { message = "Роль снята." });
            }

            throw new InvalidOperationException($"Неожиданный запрос: {request.Method} {path}.");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, object value) =>
            new(statusCode) { Content = JsonContent.Create(value) };
    }
}
