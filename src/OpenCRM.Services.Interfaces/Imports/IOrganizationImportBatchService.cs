namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Постраничная подготовка и возобновляемая обработка больших переносов. </summary>
public interface IOrganizationImportBatchService
{
    Task<IReadOnlyList<ImportProfileDto>> GetProfilesAsync(string userId, Guid organizationId, CancellationToken cancellationToken);
    Task<ImportProfileDto> SaveProfileAsync(string userId, Guid organizationId, ImportProfileDto profile, CancellationToken cancellationToken);
    Task DeleteProfileAsync(string userId, Guid organizationId, Guid id, CancellationToken cancellationToken);
    Task<ImportDraftDto> SetSourceAsync(string userId, Guid organizationId, Guid id, int revision, string? sourceKey, CancellationToken cancellationToken);
    Task<ImportDraftDto> UploadAsync(string userId, Guid organizationId, string fileName, byte[] content, CancellationToken cancellationToken);
    Task<ImportDraftDto> GetPageAsync(string userId, Guid organizationId, Guid id, int page, int pageSize, string? kind, string? status, string? search, CancellationToken cancellationToken);
    Task<ImportDraftDto> QueueMappingAsync(string userId, Guid organizationId, Guid id, int revision, List<ImportTableMapping> mappings, CancellationToken cancellationToken);
    Task<ImportDraftDto> PatchRowsAsync(string userId, Guid organizationId, Guid id, int revision, List<ImportCandidate> rows, CancellationToken cancellationToken);
    Task<ImportDraftDto> StartAsync(string userId, Guid organizationId, Guid id, int revision, bool resume, CancellationToken cancellationToken);
    Task<ImportDraftDto> PauseAsync(string userId, Guid organizationId, Guid id, int revision, CancellationToken cancellationToken);
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}
