using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.WebApp.Models.Maintenance;

/// <summary> Модель обновления записи об обслуживании. </summary>
public class UpdateMaintenanceModel
{
    /// <summary> Загружаемые файлы, приложенные к обслуживанию. </summary>
    public IFormFileCollection? Files { get; set; }

    /// <summary> Данные для обновления записи об обслуживании. </summary>
    public required UpdateMaintenanceDto Dto { get; set; }
}
