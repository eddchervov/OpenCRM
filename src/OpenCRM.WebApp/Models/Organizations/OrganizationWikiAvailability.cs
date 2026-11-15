using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Models.Organizations;

/// <summary> Предоставляет модулю рабочего места независимый от WebApp признак содержимого wiki. </summary>
public sealed class OrganizationWikiAvailability(IOrganizationWikiContentProvider contentProvider)
    : IOrganizationWikiAvailability
{
    /// <inheritdoc />
    public bool HasContent => contentProvider.HasContent;
}
