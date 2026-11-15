using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Infrastructure.Admin;

/// <summary> Получает счётчики событий и очереди модерации без загрузки списков. </summary>
public sealed class AdminNavigationService(OpenCRMContext context, IConfiguration? configuration = null) : IAdminNavigationService
{
    private static readonly (string SectionCode, string Permission)[] s_sections =
    [
        (AdminNavigationSections.Users, AdminAuthorizationPolicies.ViewUsers),
        (AdminNavigationSections.SupportMessages, AdminAuthorizationPolicies.ViewContent),
        (AdminNavigationSections.Emails, AdminAuthorizationPolicies.ViewCommunications)
    ];

    public async Task<AdminNavigationCountsDto> GetCountsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlySet<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var users = permissions.Contains(AdminAuthorizationPolicies.ViewUsers)
            ? await context.ApplicationUsers.AsNoTracking().CountAsync(
                user => !user.IsDeleted && user.DatetimeCreation >= fromUtc && user.DatetimeCreation < toUtc,
                cancellationToken)
            : 0;

        var payments = (configuration?.GetValue("PlatformPolicy:Commercial:Enabled", false) ?? false)
            && permissions.Contains(AdminAuthorizationPolicies.ViewBilling)
            ? await context.SubscriptionPayments.AsNoTracking().CountAsync(
                payment => payment.CreatedAt >= fromUtc && payment.CreatedAt < toUtc, cancellationToken)
            : 0;

        var supportMessages = permissions.Contains(AdminAuthorizationPolicies.ViewContent)
            ? await context.SupportMessages.AsNoTracking().CountAsync(
                message => message.CreatedAt >= fromUtc && message.CreatedAt < toUtc, cancellationToken)
            : 0;

        var emails = permissions.Contains(AdminAuthorizationPolicies.ViewCommunications)
            ? await context.EmailMessages.AsNoTracking().CountAsync(
                message => message.CreatedAt >= fromUtc && message.CreatedAt < toUtc, cancellationToken)
            : 0;

        var pendingCatalogSuggestions = permissions.Contains(AdminAuthorizationPolicies.ViewContent)
            ? await context.VehicleCatalogSuggestions.AsNoTracking().CountAsync(
                suggestion => suggestion.Status == VehicleCatalogSuggestionStatus.Pending, cancellationToken)
            : 0;

        return new AdminNavigationCountsDto
        {
            Users = users,
            Payments = payments,
            SupportMessages = supportMessages,
            Emails = emails,
            PendingCatalogSuggestions = pendingCatalogSuggestions
        };
    }

    public async Task<AdminNavigationCountsDto> GetUnreadCountsAsync(
        string userId,
        DateTime fromUtc,
        IReadOnlySet<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var readStates = await context.AdminNavigationReadStates.AsNoTracking()
            .Where(state => state.UserId == userId)
            .ToDictionaryAsync(state => state.SectionCode, cancellationToken);

        var seededReadState = false;
        foreach (var (sectionCode, permission) in s_sections)
        {
            if (!permissions.Contains(permission) || readStates.ContainsKey(sectionCode))
            {
                continue;
            }

            await this.SeedReadStateAsync(userId, sectionCode, fromUtc, cancellationToken);
            seededReadState = true;
        }

        if (seededReadState)
        {
            readStates = await context.AdminNavigationReadStates.AsNoTracking()
                .Where(state => state.UserId == userId)
                .ToDictionaryAsync(state => state.SectionCode, cancellationToken);
        }
        var asOfUtc = TruncateToMicroseconds(DateTime.UtcNow);

        var users = permissions.Contains(AdminAuthorizationPolicies.ViewUsers)
            ? await context.ApplicationUsers.AsNoTracking().CountAsync(
                user => !user.IsDeleted
                    && user.DatetimeCreation >= readStates[AdminNavigationSections.Users].ViewedThroughUtc
                    && user.DatetimeCreation < asOfUtc,
                cancellationToken)
            : 0;

        var payments = (configuration?.GetValue("PlatformPolicy:Commercial:Enabled", false) ?? false)
            && permissions.Contains(AdminAuthorizationPolicies.ViewBilling)
            ? await context.SubscriptionPayments.AsNoTracking().CountAsync(
                payment => payment.PaidAt != null
                    && payment.RefundedAmount < payment.Amount
                    && payment.PaymentStatus != "canceled"
                    && payment.PaymentStatus != "refunded"
                    && (payment.PaymentStatus != "completed"
                        || (string.IsNullOrWhiteSpace(payment.NpdReceiptFilePath)
                            && string.IsNullOrWhiteSpace(payment.NpdReceiptUrl))),
                cancellationToken)
            : 0;

        var supportMessages = permissions.Contains(AdminAuthorizationPolicies.ViewContent)
            ? await context.SupportMessages.AsNoTracking().CountAsync(
                message => message.CreatedAt >= readStates[AdminNavigationSections.SupportMessages].ViewedThroughUtc
                    && message.CreatedAt < asOfUtc,
                cancellationToken)
            : 0;

        var emails = permissions.Contains(AdminAuthorizationPolicies.ViewCommunications)
            ? await context.EmailMessages.AsNoTracking().CountAsync(
                message => message.CreatedAt >= readStates[AdminNavigationSections.Emails].ViewedThroughUtc
                    && message.CreatedAt < asOfUtc,
                cancellationToken)
            : 0;

        var pendingCatalogSuggestions = permissions.Contains(AdminAuthorizationPolicies.ViewContent)
            ? await context.VehicleCatalogSuggestions.AsNoTracking().CountAsync(
                suggestion => suggestion.Status == VehicleCatalogSuggestionStatus.Pending, cancellationToken)
            : 0;

        return new AdminNavigationCountsDto
        {
            Users = users,
            Payments = payments,
            SupportMessages = supportMessages,
            Emails = emails,
            PendingCatalogSuggestions = pendingCatalogSuggestions,
            AsOfUtc = asOfUtc
        };
    }

    public async Task MarkSectionViewedAsync(
        string userId,
        string sectionCode,
        DateTime viewedThroughUtc,
        CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin."NavigationReadStates" ("UserId", "SectionCode", "ViewedThroughUtc")
            VALUES ({userId}, {sectionCode}, {viewedThroughUtc})
            ON CONFLICT ("UserId", "SectionCode") DO UPDATE
            SET "ViewedThroughUtc" = GREATEST(
                admin."NavigationReadStates"."ViewedThroughUtc",
                EXCLUDED."ViewedThroughUtc");
            """, cancellationToken);
    }

    private async Task SeedReadStateAsync(
        string userId,
        string sectionCode,
        DateTime fromUtc,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO admin."NavigationReadStates" ("UserId", "SectionCode", "ViewedThroughUtc")
            VALUES ({userId}, {sectionCode}, {fromUtc})
            ON CONFLICT ("UserId", "SectionCode") DO NOTHING;
            """, cancellationToken);
    }

    private static DateTime TruncateToMicroseconds(DateTime valueUtc) =>
        new(valueUtc.Ticks - valueUtc.Ticks % 10, DateTimeKind.Utc);
}
