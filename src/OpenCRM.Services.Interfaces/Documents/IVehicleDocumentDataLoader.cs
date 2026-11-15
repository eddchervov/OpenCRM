using OpenCRM.Services.Interfaces.Documents.Dtos;
namespace OpenCRM.Services.Interfaces.Documents;

/// <summary>Загружает снимок техники и истории для отчётов независимо от формата файла.</summary>
public interface IVehicleDocumentDataLoader
{
    /// <summary> Загружает данные документа по технике. </summary>
    Task<VehicleDocumentData> LoadAsync(string userId, Guid? organizationId, Guid vehicleId);
}
