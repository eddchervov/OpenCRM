using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель представления управления ролями и правами. </summary>
public partial class RolesViewModel(AdminSessionClient sessionClient) : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, string[]> s_permissionDependencies = new Dictionary<string, string[]>
    {
        ["Admin.Users.Manage"] = ["Admin.Users.View"],
        ["Admin.Users.Impersonate"] = ["Admin.Users.View"],
        ["Admin.Billing.Manage"] = ["Admin.Billing.View"],
        ["Admin.Content.Manage"] = ["Admin.Content.View"],
        ["Admin.Communications.Manage"] = ["Admin.Communications.View"]
    };

    private readonly AdminSessionClient _sessionClient = sessionClient;
    private bool _isActive;
    private IReadOnlyList<AdminPermission> _permissions = [];
    private IReadOnlyDictionary<string, AdminRolePermissions> _rolePermissions = new Dictionary<string, AdminRolePermissions>();
    private AdminRoleMembers? _members;
    private CancellationTokenSource? _selectionLoadCancellation;
    private readonly List<Task> _selectionLoadTasks = [];
    private long _membersLoadVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SavePermissionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveMemberCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePermissionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveMemberCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditPermissions))]
    [NotifyPropertyChangedFor(nameof(CanManageMembers))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoles))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<RoleOption> _roles = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups))]
    private IReadOnlyList<RolePermissionGroup> _permissionGroups = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMembers))]
    [NotifyPropertyChangedFor(nameof(FilteredMembers))]
    private IReadOnlyList<RoleMemberItem> _membersList = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvailableMembers))]
    private IReadOnlyList<RoleMemberItem> _availableMembers = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePermissionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditPermissions))]
    private RoleOption? _selectedRole;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredMembers))]
    private string _memberSearch = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    private RoleMemberItem? _selectedMember;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveMemberCommand))]
    [NotifyPropertyChangedFor(nameof(IsRemovalConfirmationOpen))]
    private RoleMemberItem? _memberToRemove;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMemberCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRemoveMemberCommand))]
    private bool _isMembersLoading;

    [ObservableProperty]
    private bool _isMembersTab;

    public event Action? SessionExpired;

    public event Func<Task>? AccessDenied;

    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    public bool HasMessage => !string.IsNullOrWhiteSpace(this.Message);

    public bool HasRoles => this.Roles.Count > 0;

    public bool HasGroups => this.PermissionGroups.Count > 0;

    public bool HasMembers => this.MembersList.Count > 0;

    public bool HasAvailableMembers => this.AvailableMembers.Count > 0;

    public bool CanEditPermissions => !this.IsSaving && this.SelectedRole is { IsLocked: false };

    public bool CanManageMembers => !this.IsSaving;

    public bool IsRemovalConfirmationOpen => this.MemberToRemove != null;

    public bool IsEmpty => !this.IsLoading && this.Roles.Count == 0 && !this.HasError;

    public IReadOnlyList<RoleMemberItem> FilteredMembers
    {
        get
        {
            var query = this.MemberSearch.Trim();
            if (query.Length == 0)
            {
                return this.MembersList;
            }

            return this.MembersList
                .Where(member => member.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || member.Email.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || member.Phone.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    /// <summary> Активирует экран и загружает роли. </summary>
    public async Task ActivateAsync()
    {
        this._isActive = true;
        this.NotifyCommands();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Деактивирует экран, при необходимости очищая данные. </summary>
    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        var tasks = new[]
        {
            this.RefreshCommand.ExecutionTask,
            this.SavePermissionsCommand.ExecutionTask,
            this.AddMemberCommand.ExecutionTask,
            this.ConfirmRemoveMemberCommand.ExecutionTask,
        }.OfType<Task>().Concat(this._selectionLoadTasks).ToArray();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Запросы уже отменены при деактивации экрана.
        }

        this._selectionLoadTasks.Clear();
    }

    /// <summary> Останавливает экран, при необходимости очищая данные. </summary>
    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this.RefreshCommand.Cancel();
        this.SavePermissionsCommand.Cancel();
        this.AddMemberCommand.Cancel();
        this.ConfirmRemoveMemberCommand.Cancel();
        this._selectionLoadCancellation?.Cancel();
        this._membersLoadVersion++;
        this.IsMembersLoading = false;
        this.NotifyCommands();
        if (!clear)
        {
            return;
        }

        this.Roles = [];
        this.PermissionGroups = [];
        this.MembersList = [];
        this.AvailableMembers = [];
        this.SelectedRole = null;
        this.SelectedMember = null;
        this.MemberToRemove = null;
        this.IsMembersLoading = false;
        this._members = null;
        this._permissions = [];
        this._rolePermissions = new Dictionary<string, AdminRolePermissions>();
        this.MemberSearch = string.Empty;
        this.ErrorMessage = string.Empty;
        this.Message = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        this.IsLoading = true;
        this.ErrorMessage = string.Empty;
        this.Message = string.Empty;

        try
        {
            var selectedRoleId = this.SelectedRole?.Id;
            var rolesTask = this._sessionClient.GetAdminRolesAsync(cancellationToken);
            var permissionsTask = this._sessionClient.GetAdminPermissionsAsync(cancellationToken);
            await Task.WhenAll(rolesTask, permissionsTask);
            if (!this._isActive || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var permissions = await permissionsTask;
            this._rolePermissions = permissions.Roles.ToDictionary(role => role.RoleId);
            this._permissions = permissions.Permissions;
            this.Roles = (await rolesTask).Roles
                .Select(role =>
                {
                    this._rolePermissions.TryGetValue(role.Id, out var rolePermission);
                    return new RoleOption(
                        role.Id,
                        role.Name,
                        role.UsersCount,
                        role.IsProtected,
                        rolePermission?.IsLocked ?? role.IsProtected,
                        rolePermission?.PermissionCodes.Count ?? 0);
                })
                .OrderByDescending(role => role.PermissionCount)
                .ThenByDescending(role => role.IsLocked)
                .ThenBy(role => role.Name, StringComparer.CurrentCulture)
                .ToArray();
            this.SelectedRole = this.Roles.FirstOrDefault(role => role.Id == selectedRoleId) ?? this.Roles.FirstOrDefault();
        }
        catch (ApiException exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await this.HandleErrorAsync(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Запрос отменён при закрытии экрана.
        }
        catch (HttpRequestException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                this.ShowTransportError(timedOut: false);
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                this.ShowTransportError(timedOut: true);
            }
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSavePermissions))]
    private async Task SavePermissionsAsync(CancellationToken cancellationToken)
    {
        var role = this.SelectedRole;
        if (role == null || role.IsLocked)
        {
            return;
        }

        this.IsSaving = true;
        this.ErrorMessage = string.Empty;
        this.Message = string.Empty;
        try
        {
            var permissionCodes = this.PermissionGroups
                .SelectMany(group => group.Permissions)
                .Where(permission => permission.IsSelected)
                .Select(permission => permission.Code)
                .ToArray();
            var result = await this._sessionClient.UpdateAdminRolePermissionsAsync(role.Id, permissionCodes, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested || this.SelectedRole?.Id != role.Id)
            {
                return;
            }

            this.Message = result.Message;
            this._rolePermissions = new Dictionary<string, AdminRolePermissions>(this._rolePermissions)
            {
                [role.Id] = new AdminRolePermissions
                {
                    RoleId = role.Id,
                    RoleName = role.Name,
                    IsLocked = role.IsLocked,
                    PermissionCodes = permissionCodes
                }
            };
            role.PermissionCount = permissionCodes.Length;
        }
        catch (ApiException exception)
        {
            if (cancellationToken.IsCancellationRequested || !this._isActive || this.SelectedRole?.Id != role.Id)
            {
                return;
            }

            await this.HandleErrorAsync(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Запрос отменён при закрытии экрана.
        }
        catch (HttpRequestException)
        {
            if (!cancellationToken.IsCancellationRequested && this._isActive && this.SelectedRole?.Id == role.Id)
            {
                this.ShowTransportError(timedOut: false, roleId: role.Id);
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && this._isActive && this.SelectedRole?.Id == role.Id)
            {
                this.ShowTransportError(timedOut: true, roleId: role.Id);
            }
        }
        finally
        {
            this.IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddMember))]
    private async Task AddMemberAsync(CancellationToken cancellationToken)
    {
        var role = this.SelectedRole;
        var member = this.SelectedMember;
        if (role == null || member == null)
        {
            return;
        }

        var roleId = role.Id;
        this.IsSaving = true;
        this.ErrorMessage = string.Empty;
        this.Message = string.Empty;
        try
        {
            var result = await this._sessionClient.AddAdminRoleMemberAsync(roleId, member.UserId, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested || this.SelectedRole?.Id != roleId)
            {
                return;
            }

            this.Message = result.Message;
            await this.ReloadMembersAsync(cancellationToken);
        }
        catch (ApiException exception)
        {
            if (cancellationToken.IsCancellationRequested || !this._isActive || this.SelectedRole?.Id != roleId)
            {
                return;
            }

            await this.HandleErrorAsync(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Запрос отменён при закрытии экрана.
        }
        catch (HttpRequestException)
        {
            if (!cancellationToken.IsCancellationRequested && this._isActive && this.SelectedRole?.Id == roleId)
            {
                this.ShowTransportError(timedOut: false, roleId);
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && this._isActive && this.SelectedRole?.Id == roleId)
            {
                this.ShowTransportError(timedOut: true, roleId);
            }
        }
        finally
        {
            this.IsSaving = false;
            if (this.SelectedRole?.Id == roleId)
            {
                this.SelectedMember = null;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveMember))]
    private async Task ConfirmRemoveMemberAsync(CancellationToken cancellationToken)
    {
        var role = this.SelectedRole;
        var member = this.MemberToRemove;
        if (role == null || member == null || this.IsMembersLoading
            || !this.MembersList.Any(item => item.UserId == member.UserId)
            || (member.IsCurrentUser && this._members?.IsProtected == true))
        {
            return;
        }

        this.IsSaving = true;
        this.ErrorMessage = string.Empty;
        this.Message = string.Empty;
        try
        {
            var result = await this._sessionClient.RemoveAdminRoleMemberAsync(role.Id, member.UserId, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested || this.SelectedRole?.Id != role.Id)
            {
                return;
            }

            this.Message = result.Message;
            this.MemberToRemove = null;
            await this.ReloadMembersAsync(cancellationToken);
        }
        catch (ApiException exception)
        {
            if (!this._isActive || cancellationToken.IsCancellationRequested || this.SelectedRole?.Id != role.Id)
            {
                return;
            }

            await this.HandleErrorAsync(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Запрос отменён при смене роли или закрытии экрана.
        }
        catch (HttpRequestException)
        {
            if (!cancellationToken.IsCancellationRequested && this._isActive && this.SelectedRole?.Id == role.Id)
            {
                this.ShowTransportError(timedOut: false, roleId: role.Id);
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && this._isActive && this.SelectedRole?.Id == role.Id)
            {
                this.ShowTransportError(timedOut: true, roleId: role.Id);
            }
        }
        finally
        {
            this.IsSaving = false;
        }
    }

    [RelayCommand]
    private void RequestRemoveMember(RoleMemberItem? member)
    {
        if (member == null || this.IsMembersLoading || !this.MembersList.Any(item => item.UserId == member.UserId)
            || (member.IsCurrentUser && this._members?.IsProtected == true))
        {
            return;
        }

        this.MemberToRemove = member;
    }

    [RelayCommand]
    private void CancelRemoveMember() => this.MemberToRemove = null;

    [RelayCommand]
    private void SelectTab(bool isMembersTab) => this.IsMembersTab = isMembersTab;

    [RelayCommand]
    private void TogglePermission(RolePermissionItem? permission)
    {
        if (permission == null || this.SelectedRole?.IsLocked != false)
        {
            return;
        }

        var permissions = this.PermissionGroups.SelectMany(group => group.Permissions).ToArray();
        if (permission.IsSelected)
        {
            RolesViewModel.AddPermissionWithDependencies(permission.Code, permissions);
            return;
        }

        foreach (var dependent in permissions.Where(item => item.IsSelected
            && RolesViewModel.GetPermissionDependencies(item.Code).Contains(permission.Code)))
        {
            dependent.IsSelected = false;
        }
    }

    private async Task LoadMembersAsync(CancellationToken cancellationToken, long loadVersion)
    {
        var role = this.SelectedRole;
        if (role == null || !this._isActive)
        {
            return;
        }

        this.ErrorMessage = string.Empty;
        try
        {
            var members = await this._sessionClient.GetAdminRoleMembersAsync(role.Id, cancellationToken);
            if (!this.IsCurrentMembersLoad(role.Id, loadVersion, cancellationToken))
            {
                return;
            }

            this._members = members;
            this.MembersList = this._members.Users.Where(member => member.HasRole).Select(member => new RoleMemberItem(member)).ToArray();
            this.AvailableMembers = this._members.Users.Where(member => !member.HasRole).Select(member => new RoleMemberItem(member)).ToArray();
            this.OnPropertyChanged(nameof(FilteredMembers));
        }
        catch (ApiException exception)
        {
            if (!this.IsCurrentMembersLoad(role.Id, loadVersion, cancellationToken))
            {
                return;
            }

            await this.HandleErrorAsync(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Запрос отменён при смене роли или закрытии экрана.
        }
        catch (HttpRequestException)
        {
            if (!cancellationToken.IsCancellationRequested && this.IsCurrentMembersLoad(role.Id, loadVersion, cancellationToken))
            {
                this.ShowTransportError(timedOut: false);
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && this.IsCurrentMembersLoad(role.Id, loadVersion, cancellationToken))
            {
                this.ShowTransportError(timedOut: true);
            }
        }
        finally
        {
            if (loadVersion == this._membersLoadVersion)
            {
                this.IsMembersLoading = false;
            }
        }
    }

    private async Task HandleErrorAsync(ApiException exception)
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

        this.ErrorMessage = exception.Message;
    }

    private bool CanRefresh() => this._isActive && !this.IsLoading && !this.IsSaving;

    private bool CanSavePermissions() => this._isActive && !this.IsLoading && !this.IsSaving && this.SelectedRole is { IsLocked: false };

    private bool CanAddMember() => this._isActive && !this.IsLoading && !this.IsSaving && !this.IsMembersLoading
        && this.SelectedMember != null
        && this._members?.Users.Any(member => member.UserId == this.SelectedMember.UserId && !member.HasRole) == true;

    private bool CanRemoveMember() => this._isActive && !this.IsLoading && !this.IsSaving && !this.IsMembersLoading
        && this.MemberToRemove != null
        && this.MembersList.Any(member => member.UserId == this.MemberToRemove.UserId);

    private static void AddPermissionWithDependencies(string permissionCode, IReadOnlyList<RolePermissionItem> permissions)
    {
        var permissionCodes = new HashSet<string>(StringComparer.Ordinal) { permissionCode };
        foreach (var dependency in GetPermissionDependencies(permissionCode))
        {
            permissionCodes.Add(dependency);
        }

        foreach (var permission in permissions.Where(permission => permissionCodes.Contains(permission.Code)))
        {
            permission.IsSelected = true;
        }
    }

    private static IReadOnlyList<string> GetPermissionDependencies(string permissionCode)
    {
        if (permissionCode == "Admin.Access")
        {
            return [];
        }

        return ["Admin.Access", .. s_permissionDependencies.GetValueOrDefault(permissionCode, [])];
    }

    partial void OnSelectedRoleChanged(RoleOption? value)
    {
        this.CancelSelectionLoad();
        this._membersLoadVersion++;
        this.IsMembersLoading = false;
        this.Message = string.Empty;
        this.ErrorMessage = string.Empty;
        this.MemberSearch = string.Empty;
        this.SelectedMember = null;
        this.MemberToRemove = null;
        this.MembersList = [];
        this.AvailableMembers = [];
        this._members = null;
        if (value == null)
        {
            this.PermissionGroups = [];
            this.MembersList = [];
            this.AvailableMembers = [];
            this._members = null;
            return;
        }

        var selectedCodes = this._rolePermissions.GetValueOrDefault(value.Id)?.PermissionCodes.ToHashSet(StringComparer.Ordinal)
            ?? [];
        this.PermissionGroups = this._permissions
            .GroupBy(permission => permission.Group)
            .Select(group => new RolePermissionGroup(
                group.Key,
                group.Select(permission => new RolePermissionItem(permission, selectedCodes.Contains(permission.Code))).ToArray()))
            .ToArray();
        if (this.IsMembersTab)
        {
            this.StartMembersLoad();
        }
    }

    partial void OnIsMembersTabChanged(bool value)
    {
        this.Message = string.Empty;
        if (value)
        {
            this.StartMembersLoad();
            return;
        }

        this.CancelSelectionLoad();
        this._membersLoadVersion++;
        this.IsMembersLoading = false;
        this.MemberToRemove = null;
    }

    private void StartMembersLoad()
    {
        this.CancelSelectionLoad();
        var cancellation = new CancellationTokenSource();
        this._selectionLoadCancellation = cancellation;
        this._membersLoadVersion++;
        this.IsMembersLoading = true;
        var task = this.LoadMembersForSelectionAsync(cancellation, this._membersLoadVersion);
        this._selectionLoadTasks.Add(task);
    }

    private async Task LoadMembersForSelectionAsync(CancellationTokenSource cancellation, long loadVersion)
    {
        try
        {
            await this.LoadMembersAsync(cancellation.Token, loadVersion);
        }
        finally
        {
            if (ReferenceEquals(this._selectionLoadCancellation, cancellation))
            {
                this._selectionLoadCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelSelectionLoad()
    {
        var cancellation = this._selectionLoadCancellation;
        this._selectionLoadCancellation = null;
        cancellation?.Cancel();
    }

    private Task ReloadMembersAsync(CancellationToken cancellationToken)
    {
        this._membersLoadVersion++;
        this.IsMembersLoading = true;
        return this.LoadMembersAsync(cancellationToken, this._membersLoadVersion);
    }

    private bool IsCurrentMembersLoad(string roleId, long loadVersion, CancellationToken cancellationToken) =>
        this._isActive && !cancellationToken.IsCancellationRequested && loadVersion == this._membersLoadVersion
        && this.SelectedRole?.Id == roleId;

    private void ShowTransportError(bool timedOut, string? roleId = null)
    {
        if (!this._isActive || (roleId != null && this.SelectedRole?.Id != roleId))
        {
            return;
        }

        this.ErrorMessage = timedOut
            ? "Превышено время ожидания ответа сервера. Повторите попытку."
            : "Не удалось связаться с сервером. Проверьте подключение и повторите попытку.";
    }

    private void NotifyCommands()
    {
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.SavePermissionsCommand.NotifyCanExecuteChanged();
        this.AddMemberCommand.NotifyCanExecuteChanged();
        this.ConfirmRemoveMemberCommand.NotifyCanExecuteChanged();
    }
}
