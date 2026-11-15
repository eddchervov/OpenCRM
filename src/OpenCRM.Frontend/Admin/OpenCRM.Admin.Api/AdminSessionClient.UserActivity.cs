using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    /// <summary> Загружает страницу журнала активности с фильтрами и курсором. </summary>
    public Task<AdminUserActivityPage> GetUserActivityAsync(
        string? userId = null,
        string? search = null,
        string? kind = null,
        string? scenario = null,
        Guid? organizationId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        long? beforeId = null,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var query = new List<string> { $"pageSize={pageSize}" };
        Add("userId", userId);
        Add("search", search);
        Add("kind", kind);
        Add("scenario", scenario);
        if (organizationId.HasValue)
        {
            query.Add($"organizationId={organizationId.Value}");
        }

        if (from.HasValue)
        {
            query.Add($"from={Uri.EscapeDataString(from.Value.UtcDateTime.ToString("O"))}");
        }

        if (to.HasValue)
        {
            query.Add($"to={Uri.EscapeDataString(to.Value.UtcDateTime.ToString("O"))}");
        }

        if (beforeId.HasValue)
        {
            query.Add($"beforeId={beforeId.Value}");
        }

        return this.GetAuthorizedReportAsync<AdminUserActivityPage>(
            $"api/v1/admin/user-activity?{string.Join("&", query)}",
            "Не удалось загрузить журнал активности пользователей.",
            cancellationToken);

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query.Add($"{name}={Uri.EscapeDataString(value.Trim())}");
            }
        }
    }
}
