using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Services.Interfaces.Admin;
using Npgsql;

namespace OpenCRM.Infrastructure.Tests.Integration;

/// <summary> Создаёт синтетические организации и тарифы только в отдельной копии браузерного QA. </summary>
internal static class PaymentsBrowserSeed
{
    public static async Task<object> SeedAsync(string connectionString, bool useRealProvider = false)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var adminActions = Environment.GetEnvironmentVariable("PAYMENTS_BROWSER_SCENARIO") == "AdminActions" && !useRealProvider;
        var expectedDatabase = adminActions ? "opencrm_payments_admin_actions_qa"
            : useRealProvider ? "opencrm_yookassa_browser_qa" : "opencrm_payments_browser_qa";
        if (connection.Host != "127.0.0.1" || connection.Port != 15439 || connection.Database != expectedDatabase)
        {
            throw new InvalidOperationException("Браузерная фикстура разрешена только в изолированной копии opencrm_payments_browser_qa.");
        }

        var options = new DbContextOptionsBuilder<OpenCRMContext>().UseNpgsql(connectionString,
            database => database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name)).Options;
        await using var context = new OpenCRMContext(options);
        await context.Database.MigrateAsync();
        var sourceId = Guid.Parse("2aba52a0-adb1-4dce-8b08-9f943dfddbe3");
        var ownerId = await context.Organizations.Where(item => item.Id == sourceId).Select(item => item.OwnerUserId).SingleAsync();
        await context.Users.Where(user => user.Id == ownerId)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.CanUseTestSubscriptions, false));
        var detailId = Guid.Parse(adminActions ? "a2480000-0000-4000-8000-000000000001" : useRealProvider ? "a2470000-0000-4000-8000-000000000001" : "a2460000-0000-4000-8000-000000000001");
        var serviceId = Guid.Parse(adminActions ? "a2480000-0000-4000-8000-000000000002" : useRealProvider ? "a2470000-0000-4000-8000-000000000002" : "a2460000-0000-4000-8000-000000000002");
        var scenarios = new[]
        {
            (Id: detailId, Code: OrganizationTypeCodes.Detailing, Name: "Платежи · браузерная QA"),
            (Id: serviceId, Code: OrganizationTypeCodes.ServiceStation, Name: "СТО · платёжная QA")
        };
        foreach (var scenario in scenarios)
        {
            if (!await context.Organizations.AnyAsync(item => item.Id == scenario.Id))
            {
                context.Organizations.Add(new Organization
                {
                    Id = scenario.Id, Name = scenario.Name, OwnerUserId = ownerId, PrimaryScenarioCode = scenario.Code,
                    ShiftTimeZoneId = "Asia/Novosibirsk",
                    Members = [new OrganizationMember { UserId = ownerId, RoleCode = OrganizationRoleCodes.Owner }]
                });
            }
        }

        var role = await context.Roles.SingleOrDefaultAsync(item => item.NormalizedName == "PAYMENTS_BROWSER_QA");
        if (role == null)
        {
            role = new IdentityRole("Payments Browser QA") { NormalizedName = "PAYMENTS_BROWSER_QA" };
            context.Roles.Add(role);
        }

        await context.SaveChangesAsync();
        if (!await context.UserRoles.AnyAsync(item => item.UserId == ownerId && item.RoleId == role.Id))
        {
            context.UserRoles.Add(new IdentityUserRole<string> { UserId = ownerId, RoleId = role.Id });
        }

        foreach (var permission in new[] { AdminAuthorizationPolicies.Access, AdminAuthorizationPolicies.ViewBilling, AdminAuthorizationPolicies.ManageBilling })
        {
            if (!await context.AdminRolePermissions.AnyAsync(item => item.RoleId == role.Id && item.PermissionCode == permission))
            {
                context.AdminRolePermissions.Add(new AdminRolePermission { RoleId = role.Id, PermissionCode = permission, CreatedAt = DateTime.UtcNow });
            }
        }

        var plan = await context.SubscriptionPlans.SingleOrDefaultAsync(item => item.Name == "Браузерный PRO · тестовые деньги");
        if (plan == null)
        {
            var model = await context.SubscriptionPlans.AsNoTracking().Include(item => item.Features)
                .Where(item => item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business && item.IsActive && !item.IsDemoPlan)
                .OrderByDescending(item => item.Order).FirstAsync();
            plan = new SubscriptionPlan
            {
                Name = "Браузерный PRO · тестовые деньги",
                Description = useRealProvider ? "Тестовый магазин ЮKassa, реальные деньги не списываются."
                    : "Только локальный имитатор, деньги не списываются.",
                Price = 420, DurationDays = 30, VehicleLimit = 20, Order = 1000, Group = "Платёжная QA",
                PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
                Features = model.Features.Select(feature => new SubscriptionPlanFeature
                {
                    FeatureDefinitionId = feature.FeatureDefinitionId, BoolValue = feature.BoolValue, IntValue = feature.IntValue
                }).ToList()
            };
            context.SubscriptionPlans.Add(plan);
        }

        await context.SaveChangesAsync();

        return new { detailingOrganizationId = detailId, serviceOrganizationId = serviceId, planId = plan.Id,
            detailingUrl = $"https://localhost:7244/detailing/{detailId:D}#billing",
            serviceUrl = $"https://localhost:7244/autoservice/{serviceId:D}#billing" };
    }
}
