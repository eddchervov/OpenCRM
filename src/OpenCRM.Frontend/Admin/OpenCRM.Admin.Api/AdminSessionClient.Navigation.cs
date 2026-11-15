using System.Globalization;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Получает счётчики меню за указанные локальные сутки в UTC. </summary>
    public Task<AdminNavigationCounts> GetNavigationCountsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return this.GetAuthorizedReportAsync<AdminNavigationCounts>(
            $"api/v1/admin/navigation/counts?fromUtc={from}&toUtc={to}",
            "Не удалось обновить счётчики меню.", cancellationToken);
    }

    public Task<AdminNavigationCounts> GetUnreadNavigationCountsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var from = Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        return this.GetAuthorizedReportAsync<AdminNavigationCounts>(
            $"api/v1/admin/navigation/counts?fromUtc={from}&toUtc={to}&unread=true",
            "Не удалось обновить счётчики меню.", cancellationToken);
    }

    public Task<AdminOperationResponse> MarkNavigationSectionViewedAsync(
        string section,
        DateTime viewedThroughUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        var viewedThrough = viewedThroughUtc.ToUniversalTime()
            .ToString("O", CultureInfo.InvariantCulture);

        return this.SendAuthorizedRequestAsync<AdminOperationResponse>(
            HttpMethod.Post,
            $"api/v1/admin/navigation/sections/{Uri.EscapeDataString(section)}/viewed",
            new MarkNavigationSectionViewedRequest(viewedThrough),
            "Не удалось сохранить отметку просмотра.", cancellationToken);
    }

    private sealed record MarkNavigationSectionViewedRequest(string ViewedThroughUtc);
}
