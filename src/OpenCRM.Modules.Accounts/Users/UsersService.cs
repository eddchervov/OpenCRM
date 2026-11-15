using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Modules.Accounts.Users;

/// <summary> Сервис работы с пользователями. </summary>
public class UsersService
    : IUsersService
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext;

    /// <summary> Сведения о текущем клиенте, если вызов связан с HTTP-запросом. </summary>
    private readonly IUserAccessInfoAccessor? _accessInfoAccessor;

    private readonly bool _trackingEnabled;

    /// <summary> Создаёт сервис с контекстом базы данных. </summary>
    public UsersService(OpenCRMContext opencrmContext, IUserAccessInfoAccessor? accessInfoAccessor = null, IConfiguration? configuration = null)
    {
        this._opencrmContext = opencrmContext;
        this._accessInfoAccessor = accessInfoAccessor;
        this._trackingEnabled = configuration?.GetValue<bool>("UserTracking:Enabled") == true;
    }

    /// <summary> Записывает дату последнего входа в систему. </summary>
    public async Task RecordLastDateOfEntry(string userId)
    {
        if (!this._trackingEnabled)
        {
            return;
        }

        var user = await _opencrmContext.ApplicationUsers.FirstOrDefaultAsync(x => x.Id == userId)
            ?? throw new Exception($"Не найден пользователь с id={userId}");

        user.LastDateOfEntry = DateTime.Now.ToUniversalTime();
        user.CountVisits++;

        await _opencrmContext.SaveChangesAsync();
    }

    /// <summary> Записывает дату последнего входа по логину. </summary>
    public Task RecordLastLoginDate(string userId) => this.RecordLastLoginDate(userId, "unknown");

    /// <summary> Записывает успешную авторизацию вместе со сведениями о клиенте. </summary>
    public async Task RecordLastLoginDate(string userId, string authenticationMethod)
    {
        var user = await this._opencrmContext.ApplicationUsers.FirstOrDefaultAsync(x => x.Id == userId)
            ?? throw new Exception($"Не найден пользователь с id={userId}");

        var now = DateTime.UtcNow;
        user.LastLoginDate = now;
        if (this._trackingEnabled)
        {
            user.CountLogins++;
        }
        user.LastLoginMethod = authenticationMethod;
        var access = this._accessInfoAccessor?.GetCurrent();
        if (this._trackingEnabled && access != null)
        {
            UsersService.ApplyAccess(user, access, now);
        }

        await this._opencrmContext.SaveChangesAsync();
    }

    /// <summary> Обновляет только снимок последнего посещения без изменения счётчика авторизаций. </summary>
    public async Task RecordLastAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!this._trackingEnabled)
        {
            return;
        }

        var access = this._accessInfoAccessor?.GetCurrent();
        if (access == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        await this._opencrmContext.ApplicationUsers
            .Where(user => user.Id == userId && !user.IsDeleted && (user.LastAccessAt == null || user.LastAccessAt < now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.LastAccessAt, now)
                .SetProperty(user => user.LastAccessIpAddress, access.IpAddress)
                .SetProperty(user => user.LastAccessUserAgent, access.UserAgent)
                .SetProperty(user => user.LastAccessLanguage, access.Language)
                .SetProperty(user => user.LastAccessClientBrands, access.ClientBrands)
                .SetProperty(user => user.LastAccessPlatform, access.Platform)
                .SetProperty(user => user.LastAccessIsMobile, access.IsMobile)
                .SetProperty(user => user.LastAccessAuthenticationScheme, access.AuthenticationScheme), cancellationToken);
    }

    /// <summary> Сохраняет единый снимок клиента при успешной авторизации. </summary>
    private static void ApplyAccess(ApplicationUser user, UserAccessInfoDto access, DateTime now)
    {
        user.LastAccessAt = now;
        user.LastAccessIpAddress = access.IpAddress;
        user.LastAccessUserAgent = access.UserAgent;
        user.LastAccessLanguage = access.Language;
        user.LastAccessClientBrands = access.ClientBrands;
        user.LastAccessPlatform = access.Platform;
        user.LastAccessIsMobile = access.IsMobile;
        user.LastAccessAuthenticationScheme = access.AuthenticationScheme;
    }

    /// <summary> Возвращает персональную статистику пользователя. </summary>
    public async Task<UserPersonalStatsDto> GetPersonalStatsAsync(string userId)
    {
        return new UserPersonalStatsDto
        {
            VehiclesCount = await _opencrmContext.Vehicles.CountAsync(x => x.UserId == userId && x.OrganizationId == null && !x.IsDeleted),
            MaintenanceCount = await _opencrmContext.Maintenance.CountAsync(x => x.UserId == userId && x.Vehicle.OrganizationId == null && !x.IsDeleted)
        };
    }

    /// <summary> Проверяет, принадлежал ли email удалённому исходному аккаунту. </summary>
    public async Task<bool> HasDeletedOriginalEmailAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return await _opencrmContext.Users
            .AnyAsync(u => u.OriginalEmail == email && u.IsDeleted);
    }
}
