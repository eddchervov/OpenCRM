using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Счётчики доступных администратору разделов. </summary>
public interface IAdminNavigationService
{
    Task<AdminNavigationCountsDto> GetCountsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlySet<string> permissions,
        CancellationToken cancellationToken = default);

    Task<AdminNavigationCountsDto> GetUnreadCountsAsync(
        string userId,
        DateTime fromUtc,
        IReadOnlySet<string> permissions,
        CancellationToken cancellationToken = default);

    Task MarkSectionViewedAsync(
        string userId,
        string sectionCode,
        DateTime viewedThroughUtc,
        CancellationToken cancellationToken = default);
}
