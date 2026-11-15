using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Navigation;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> Управляет подготовкой и потоковым скачиванием архива текущим владельцем. </summary>
[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrganizationDataExportApiController(
    IOrganizationDataExportService exports,
    IOrganizationQueryService organizationQueries) : ControllerBase
{
    [HttpPost("/api/v1/organizations/{organizationId:guid}/data-export")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.OrganizationDataExport)]
    public Task<IActionResult> Prepare(Guid organizationId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId =>
        {
            var status = await exports.RequestAsync(userId, organizationId, cancellationToken);

            return this.StatusCode(status.State is "queued" or "running" ? StatusCodes.Status202Accepted : StatusCodes.Status200OK, status);
        });

    [HttpGet("/api/v1/organizations/{organizationId:guid}/data-export/status")]
    public Task<IActionResult> Status(Guid organizationId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => this.Ok(await exports.GetStatusAsync(userId, organizationId, cancellationToken)));

    [HttpGet("/api/v1/organizations/{organizationId:guid}/data-export/{jobId:guid}/file")]
    public Task<IActionResult> Download(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId =>
        {
            var stream = await exports.OpenAsync(userId, organizationId, jobId, cancellationToken);

            return this.File(stream, "application/zip", $"opencrm-{organizationId:N}-{jobId:N}.zip");
        });

    [HttpGet("/api/v1/organizations/{organizationId:guid}/data-export")]
    public Task<IActionResult> DownloadLatest(Guid organizationId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId =>
        {
            var status = await exports.GetStatusAsync(userId, organizationId, cancellationToken);
            if (status.State != "ready" || !status.Id.HasValue)
            {
                return await this.RedirectToExportSettingsAsync(userId, organizationId);
            }

            try
            {
                var stream = await exports.OpenAsync(userId, organizationId, status.Id.Value, cancellationToken);

                return this.File(stream, "application/zip", $"opencrm-{organizationId:N}-{status.Id:N}.zip");
            }
            catch (FileNotFoundException)
            {
                return await this.RedirectToExportSettingsAsync(userId, organizationId);
            }
        });

    private async Task<IActionResult> RedirectToExportSettingsAsync(string userId, Guid organizationId)
    {
        var organization = await organizationQueries.GetByIdAsync(userId, organizationId);
        var workspaceUrl = this.Url.Content("~" + OrganizationWorkspaceUrlHelper.Resolve(organization, "settings?tab=export"));

        return this.LocalRedirect(workspaceUrl!);
    }

    private async Task<IActionResult> ExecuteAsync(Func<string, Task<IActionResult>> action)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        try
        {
            return await action(userId);
        }
        catch (UnauthorizedAccessException)
        {
            return this.StatusCode(StatusCodes.Status403Forbidden, new { message = "Выгрузка доступна только владельцу организации." });
        }
        catch (OrganizationDataExportResourceLimitException exception)
        {
            this.Response.Headers.RetryAfter = "60";

            return this.StatusCode(StatusCodes.Status429TooManyRequests, new { message = exception.Message });
        }
        catch (FileNotFoundException)
        {
            return this.Conflict(new { message = "Готовый архив отсутствует. Обновите состояние и запросите подготовку ещё раз." });
        }
        catch (InvalidOperationException)
        {
            return this.Conflict(new { message = "Архив пока недоступен. Обновите состояние выгрузки в настройках организации." });
        }
    }
}
