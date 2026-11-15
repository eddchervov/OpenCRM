using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Модерация предложений по справочнику техники. </summary>
public interface IAdminCatalogSuggestionService
{
    /// <summary> Возвращает предложения по справочнику техники с фильтром по статусу. </summary>
    Task<IReadOnlyList<AdminCatalogSuggestionDto>> GetAsync(VehicleCatalogSuggestionStatus? status);

    /// <summary> Согласует предложение по справочнику техники. </summary>
    Task<AdminOperationResult> ApproveAsync(int id, string adminUserId, string? comment);

    /// <summary> Отклоняет предложение по справочнику техники. </summary>
    Task<AdminOperationResult> RejectAsync(int id, string adminUserId, string? comment);

    /// <summary> Удаляет предложение по справочнику техники. </summary>
    Task<AdminOperationResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
