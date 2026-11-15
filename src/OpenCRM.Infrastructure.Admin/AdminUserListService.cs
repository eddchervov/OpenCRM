using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Сервис загрузки списка пользователей для админки. </summary>
internal sealed class AdminUserListService(OpenCRMContext opencrmContext, bool commercialEnabled)
{
    /// <summary> Зависимость контекста базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Возвращает страницу списка пользователей с фильтрами и сортировкой. </summary>
    public async Task<GetUserResponse> GetAsync(
        string? search,
        UsersSort sort,
        bool includeDeleted,
        int page,
        int pageSize,
        string? utmSource)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var localSearch = search?.Trim()?.ToUpperInvariant();
        var phoneSearch = !string.IsNullOrEmpty(localSearch)
            && localSearch.All(character => char.IsDigit(character) || character is '+' or '-' or '(' or ')' or ' ')
            ? new string(localSearch.Where(char.IsDigit).ToArray())
            : localSearch;
        var now = DateTime.UtcNow;
        var defaultPlanName = commercialEnabled ? await this._opencrmContext.SubscriptionPlans
            .Where(x =>
                x.IsActive &&
                x.IsDefaultPlan &&
                x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() : null;

        var query = this._opencrmContext.ApplicationUsers.AsQueryable();

        if (!includeDeleted)
        {
            query = query.Where(x => !x.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(utmSource))
        {
            var source = utmSource.Trim().ToUpperInvariant();
            query = query.Where(x => x.FirstUtmSource != null && x.FirstUtmSource.ToUpper() == source);
        }

        if (!string.IsNullOrEmpty(localSearch))
        {
            query = query.Where(x =>
                (x.NormalizedEmail != null && EF.Functions.ILike(x.NormalizedEmail, $"%{localSearch}%")) ||
                (!string.IsNullOrEmpty(phoneSearch) && x.PhoneFiltered != null
                    && EF.Functions.ILike(x.PhoneFiltered, $"%{phoneSearch}%")) ||
                (x.FirstUtmSource != null && EF.Functions.ILike(x.FirstUtmSource, $"%{localSearch}%")));
        }

        var queryDb = query.Select(x => new GetUserDto
        {
            Id = x.Id,
            Phone = x.PhoneNumber,
            PhoneConfirmed = x.PhoneNumberConfirmed,
            SMSCode = x.SMSCode,
            Email = x.Email,
            EmailConfirmed = x.EmailConfirmed,
            HasPassword = !string.IsNullOrEmpty(x.PasswordHash),
            LockoutEnabled = x.LockoutEnabled,
            DatetimeCreation = x.DatetimeCreation,
            LastDateOfEntry = x.LastDateOfEntry,
            CountVisits = x.CountVisits,
            LastLoginDate = x.LastLoginDate,
            CountLogins = x.CountLogins,
            FirstUtmSource = x.FirstUtmSource,
            FirstUtmMedium = x.FirstUtmMedium,
            CanUseTestSubscriptions = commercialEnabled && x.CanUseTestSubscriptions,
            CurrentSubscriptionName = commercialEnabled ? this._opencrmContext.UserSubscriptions
                .Where(us => us.UserId == x.Id && us.EndDate >= now)
                .Select(us => us.SubscriptionPlan.Name)
                .FirstOrDefault() : null,
            CurrentSubscriptionEndDate = commercialEnabled ? this._opencrmContext.UserSubscriptions
                .Where(us => us.UserId == x.Id && us.EndDate >= now)
                .Select(us => (DateTime?)us.EndDate)
                .FirstOrDefault() : null,
            CurrentSubscriptionIsDemo = commercialEnabled ? this._opencrmContext.UserSubscriptions
                .Where(us => us.UserId == x.Id && us.EndDate >= now)
                .Select(us => us.SubscriptionPlan.IsDemoPlan)
                .FirstOrDefault() : false,
            ActiveFeatureGrantsCount = commercialEnabled ? x.FeatureGrants.Count(grant =>
                grant.IsEnabled &&
                (!grant.StartDate.HasValue || grant.StartDate <= now) &&
                (!grant.EndDate.HasValue || grant.EndDate >= now)) : 0,
            IsDeleted = x.IsDeleted,
            CountVehicles = x.Vehicles.Count(car => !car.IsDeleted),
            OrganizationsCount = x.OrganizationMemberships.Select(member => member.OrganizationId)
                .Union(x.OwnedOrganizations.Select(organization => organization.Id))
                .Count(),
            CountMaintenance = x.Vehicles
                .Where(car => !car.IsDeleted)
                .SelectMany(car => car.Maintenance)
                .Count(maintenance => !maintenance.IsDeleted)
        });

        var sortedQuery = sort switch
        {
            UsersSort.ByDateAsc => queryDb.OrderBy(x => x.DatetimeCreation),
            UsersSort.ByEmailAsc => queryDb.OrderBy(x => x.Email),
            UsersSort.ByEmailDesc => queryDb.OrderByDescending(x => x.Email),
            UsersSort.ByLastLoginDesc => queryDb.OrderByDescending(x => x.LastLoginDate),
            UsersSort.ByLastLoginAsc => queryDb.OrderBy(x => x.LastLoginDate),
            UsersSort.ByVehiclesDesc => queryDb.OrderByDescending(x => x.CountVehicles),
            UsersSort.ByVehiclesAsc => queryDb.OrderBy(x => x.CountVehicles),
            UsersSort.ByMaintenanceDesc => queryDb.OrderByDescending(x => x.CountMaintenance),
            UsersSort.ByMaintenanceAsc => queryDb.OrderBy(x => x.CountMaintenance),
            UsersSort.ByOrganizationsDesc => queryDb.OrderByDescending(x => x.OrganizationsCount),
            UsersSort.ByOrganizationsAsc => queryDb.OrderBy(x => x.OrganizationsCount),
            UsersSort.ByPlanAsc => queryDb.OrderBy(x => x.CurrentSubscriptionName),
            UsersSort.ByPlanDesc => queryDb.OrderByDescending(x => x.CurrentSubscriptionName),
            UsersSort.BySourceAsc => queryDb.OrderBy(x => x.FirstUtmSource),
            UsersSort.BySourceDesc => queryDb.OrderByDescending(x => x.FirstUtmSource),
            _ => queryDb.OrderByDescending(x => x.DatetimeCreation)
        };

        var allCount = await queryDb.CountAsync();
        page = Math.Min(page, Math.Max(1, (allCount + pageSize - 1) / pageSize));
        var users = await sortedQuery
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        foreach (var user in users)
        {
            if (!string.IsNullOrEmpty(user.CurrentSubscriptionName))
            {
                user.EffectivePlanName = user.CurrentSubscriptionName;
                user.EffectivePlanType = user.CurrentSubscriptionIsDemo ? "demo" : "subscription";
            }
            else if (!string.IsNullOrEmpty(defaultPlanName))
            {
                user.EffectivePlanName = defaultPlanName;
                user.EffectivePlanType = "default";
            }
            else
            {
                user.EffectivePlanName = null;
                user.EffectivePlanType = "none";
            }
        }

        return new GetUserResponse
        {
            Users = users,
            AllCount = allCount,
            Page = page,
            PageSize = pageSize
        };
    }
}
