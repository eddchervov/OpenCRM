using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Inquiries;

internal sealed class OrganizationInquiryOriginReader(
    OpenCRMContext context,
    InquiryAccess access) : IOrganizationInquiryOriginReader
{
    public async Task<OrganizationInquiryOriginDto?> FindSourceAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanReadOriginAsync(userId, organizationId, cancellationToken))
        {
            return null;
        }

        var inquiryId = await context.OrganizationInquiries.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.MaintenanceId == maintenanceId)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!inquiryId.HasValue)
        {
            return null;
        }

        return new OrganizationInquiryOriginDto { InquiryId = inquiryId.Value };
    }
}
