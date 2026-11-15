using System.Net;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    public async Task<AdminTestDataOrganizationsPage> GetTestDataOrganizationsPageAsync(
        string search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 10, 100);
        page = Math.Max(1, page);
        try
        {
            return await this.GetAuthorizedReportAsync<AdminTestDataOrganizationsPage>(
                $"api/v1/admin/test-data/organizations/page?search={Uri.EscapeDataString(search)}&page={page}&pageSize={pageSize}",
                "Не удалось загрузить организации.", cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            var organizations = await this.GetTestDataOrganizationsAsync(search, cancellationToken);
            page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(organizations.Count / (double)pageSize)));

            return new AdminTestDataOrganizationsPage
            {
                Items = organizations.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                TotalCount = organizations.Count,
                Page = page,
                PageSize = pageSize,
                IsLegacyResponse = true
            };
        }
    }

    public async Task<AdminTestDataOrganization?> GetTestDataOrganizationAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await this.GetAuthorizedReportAsync<AdminTestDataOrganization>(
                $"api/v1/admin/test-data/organizations/{organizationId}",
                "Не удалось загрузить организацию.", cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            var organizations = await this.GetTestDataOrganizationsAsync(organizationId.ToString(), cancellationToken);

            return organizations.FirstOrDefault(organization => organization.Id == organizationId);
        }
    }

    public Task<IReadOnlyList<AdminTestDataOrganization>> GetTestDataOrganizationsAsync(
        string search, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminTestDataOrganization>>(
            $"api/v1/admin/test-data/organizations?search={Uri.EscapeDataString(search)}",
            "Не удалось загрузить организации.", cancellationToken);

    public Task<IReadOnlyList<AdminTestDataVehicle>> GetTestDataVehiclesAsync(
        Guid organizationId, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminTestDataVehicle>>(
            $"api/v1/admin/test-data/organizations/{organizationId}/vehicles",
            "Не удалось загрузить технику организации.", cancellationToken);

    public Task<AdminTestDataGenerationResult> GenerateTestVehiclesAsync(
        Guid organizationId, int count, CancellationToken cancellationToken = default)
    {
        ValidateCount(count, 100);

        return this.SendAuthorizedRequestAsync<AdminTestDataGenerationResult>(HttpMethod.Post,
            $"api/v1/admin/test-data/organizations/{organizationId}/vehicles/generate",
            new GenerateRequest(count), "Не удалось создать тестовую технику.", cancellationToken);
    }

    public Task<AdminTestDataGenerationResult> GenerateTestMaintenancesAsync(
        Guid organizationId, Guid vehicleId, int count, string mode, CancellationToken cancellationToken = default)
    {
        ValidateCount(count, 300);
        if (mode is not ("completed" or "planned"))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "Укажите завершённые или запланированные обслуживания.");
        }

        return this.SendAuthorizedRequestAsync<AdminTestDataGenerationResult>(HttpMethod.Post,
            $"api/v1/admin/test-data/organizations/{organizationId}/vehicles/{vehicleId}/maintenances/generate",
            new GenerateMaintenanceRequest(count, mode), "Не удалось создать тестовые обслуживания.", cancellationToken);
    }

    public Task<AdminTestDataGenerationResult> GenerateTestDefectsAsync(
        Guid organizationId, Guid vehicleId, int count, CancellationToken cancellationToken = default)
    {
        ValidateCount(count, 100);

        return this.SendAuthorizedRequestAsync<AdminTestDataGenerationResult>(HttpMethod.Post,
            $"api/v1/admin/test-data/organizations/{organizationId}/vehicles/{vehicleId}/defects/generate",
            new GenerateRequest(count), "Не удалось создать тестовые дефекты.", cancellationToken);
    }

    private static void ValidateCount(int count, int maximum)
    {
        if (count is < 1 || count > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(count), $"Количество должно быть от 1 до {maximum}.");
        }
    }

    private sealed record GenerateRequest(int Count);
    private sealed record GenerateMaintenanceRequest(int Count, string Mode);
}
