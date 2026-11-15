using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> Справочник групп и шаблонов переписки организации. </summary>
[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/message-templates")]
public sealed class OrganizationMessageTemplatesApiController(
    UserManager<ApplicationUser> users,
    IOrganizationMessageTemplateService templates,
    IOrganizationPermissionService permissions) : ControllerBase
{
    [HttpGet("access")]
    public Task<IActionResult> Access(Guid organizationId) =>
        this.ExecuteAsync(async userId => new
        {
            CanUseTemplates = await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MessagesView),
            CanManage = await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MessagesTemplatesManage),
            CanViewWorkspaceCatalog = await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceCatalogView)
        });

    [HttpGet]
    public Task<IActionResult> Catalog(Guid organizationId, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        this.ExecuteAsync(async userId => new
        {
            Groups = await templates.GetGroupsAsync(userId, organizationId, includeArchived, cancellationToken),
            CanManage = await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MessagesTemplatesManage),
            CanViewWorkspaceCatalog = await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceCatalogView)
        });

    [HttpPost("groups")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateGroup(Guid organizationId, SaveMessageTemplateGroupDto request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => templates.SaveGroupAsync(userId, organizationId, request with { Id = null }, cancellationToken));

    [HttpPut("groups/{groupId:guid}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateGroup(Guid organizationId, Guid groupId, SaveMessageTemplateGroupDto request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => templates.SaveGroupAsync(userId, organizationId, request with { Id = groupId }, cancellationToken));

    [HttpPost("groups/{groupId:guid}/archive")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ArchiveGroup(Guid organizationId, Guid groupId, ArchiveTemplateRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => templates.ArchiveGroupAsync(userId, organizationId, groupId, request.ExpectedVersion, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Create(Guid organizationId, SaveMessageTemplateDto request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => templates.SaveTemplateAsync(userId, organizationId, request with { Id = null }, cancellationToken));

    [HttpPut("{templateId:guid}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Update(Guid organizationId, Guid templateId, SaveMessageTemplateDto request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => templates.SaveTemplateAsync(userId, organizationId, request with { Id = templateId }, cancellationToken));

    [HttpPost("{templateId:guid}/archive")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Archive(Guid organizationId, Guid templateId, ArchiveTemplateRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => templates.ArchiveTemplateAsync(userId, organizationId, templateId, request.ExpectedVersion, cancellationToken));

    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Preview(Guid organizationId, PreviewMessageTemplateDto request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => templates.PreviewAsync(userId, organizationId, request, cancellationToken));

    private async Task<IActionResult> ExecuteAsync<T>(Func<string, Task<T>> action)
    {
        var userId = users.GetUserId(this.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        this.Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await action(userId);

            return result is null ? this.NotFound() : this.Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (KeyNotFoundException)
        {
            return this.NotFound();
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return this.Conflict(new { message = exception.Message });
        }
    }

    public sealed record ArchiveTemplateRequest(int ExpectedVersion);
}
