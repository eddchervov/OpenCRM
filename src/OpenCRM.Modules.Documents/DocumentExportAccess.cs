using OpenCRM.Modules.Billing.Contracts;
using Helpers;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Documents;

/// <summary> Проверка доступа к экспорту документов организации. </summary>
public sealed class DocumentExportAccess(
    IFeatureAccessService featureAccessService,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IPlatformCommercialPolicy? platformCommercialPolicy = null)
{
    /// <summary> Возвращает доступ пользователя к рабочему пространству организации. </summary>
    public Task<OrganizationWorkspaceAccessDto> GetWorkspaceAccessAsync(string userId, Guid organizationId) =>
        organizationSubscriptionService.GetWorkspaceAccessAsync(userId, organizationId);

    /// <summary> Проверяет доступ к экспорту документа в PDF. </summary>
    public Task<bool> HasPdfAccessAsync(string userId, Guid? organizationId, bool includePhotos) =>
        this.HasAccessAsync(userId, organizationId, includePhotos, FeatureCodes.ExportPdf, FeatureCodes.ExportPdfWithPhotos);

    /// <summary> Проверяет ролевой доступ к ранее сохранённому акту приёмки после окончания PRO. </summary>
    public async Task<bool> HasIntakeHistoryAccessAsync(string userId, Guid organizationId)
    {
        if (platformCommercialPolicy?.CommercialEnabled == true)
        {
            var access = await organizationSubscriptionService.GetWorkspaceAccessAsync(userId, organizationId);
            if (!access.IsFreePlan)
            {
                return false;
            }
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.DocumentsExport);

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary> Проверяет доступ к экспорту документа в Word. </summary>
    public Task<bool> HasWordAccessAsync(string userId, Guid? organizationId, bool includePhotos) =>
        this.HasAccessAsync(userId, organizationId, includePhotos, FeatureCodes.ExportWord, FeatureCodes.ExportWordWithPhotos);

    /// <summary> Проверяет доступ к экспорту с учётом включения фотографий. </summary>
    private async Task<bool> HasAccessAsync(
        string userId,
        Guid? organizationId,
        bool includePhotos,
        string exportFeatureCode,
        string photoFeatureCode)
    {
        var featureCode = includePhotos ? photoFeatureCode : exportFeatureCode;
        if (!organizationId.HasValue)
        {
            return await featureAccessService.HasFeatureAsync(userId, featureCode);
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, featureCode))
        {
            return false;
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(
                userId,
                organizationId.Value,
                OrganizationPermissionCodes.DocumentsExport);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
