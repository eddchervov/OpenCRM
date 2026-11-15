using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Rules;

namespace OpenCRM.Modules.Clients.Commands;

/// <summary> Писатель тегов клиентов организации. </summary>
internal sealed class OrganizationClientTagWriter(OpenCRMContext context)
{
    /// <summary> Синхронизирует теги клиента с переданным списком. </summary>
    internal async Task SyncAsync(Guid organizationId, OrganizationClient client, IReadOnlyList<string> tagNames)
    {
        var normalizedTagNames = ClientRules.NormalizeTagNames(tagNames);
        var existingAssignments = client.TagAssignments.ToList();

        if (normalizedTagNames.Count == 0)
        {
            context.OrganizationClientTagAssignments.RemoveRange(existingAssignments);
            client.TagAssignments.Clear();
            return;
        }

        var normalizedNames = normalizedTagNames.Select(tag => tag.NormalizedName).ToList();
        var existingTags = await context.OrganizationClientTags
            .Where(tag => tag.OrganizationId == organizationId && normalizedNames.Contains(tag.NormalizedName))
            .ToListAsync();
        var tagsByNormalizedName = existingTags.ToDictionary(tag => tag.NormalizedName, StringComparer.Ordinal);

        foreach (var tagName in normalizedTagNames)
        {
            if (tagsByNormalizedName.ContainsKey(tagName.NormalizedName))
            {
                continue;
            }

            var tag = new OrganizationClientTag
            {
                OrganizationId = organizationId,
                Name = tagName.Name,
                NormalizedName = tagName.NormalizedName,
                CreatedAt = DateTime.UtcNow
            };
            context.OrganizationClientTags.Add(tag);
            tagsByNormalizedName[tag.NormalizedName] = tag;
        }

        var targetTagIds = tagsByNormalizedName.Values.Select(tag => tag.Id).ToHashSet();
        var assignmentsToRemove = existingAssignments
            .Where(assignment => !targetTagIds.Contains(assignment.OrganizationClientTagId))
            .ToList();
        context.OrganizationClientTagAssignments.RemoveRange(assignmentsToRemove);

        foreach (var assignment in assignmentsToRemove)
        {
            client.TagAssignments.Remove(assignment);
        }

        var existingTagIds = existingAssignments
            .Except(assignmentsToRemove)
            .Select(assignment => assignment.OrganizationClientTagId)
            .ToHashSet();

        foreach (var tag in tagsByNormalizedName.Values.Where(tag => !existingTagIds.Contains(tag.Id)))
        {
            client.TagAssignments.Add(new OrganizationClientTagAssignment
            {
                OrganizationClientId = client.Id,
                OrganizationClientTagId = tag.Id,
                OrganizationClientTag = tag
            });
        }
    }
}
