using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.WebApp.Models.Maintenance;

/// <summary> Модель формы создания записи об обслуживании. </summary>
public class CreateMaintenanceModel
{
    /// <summary> Загружаемые файлы, приложенные к обслуживанию. </summary>
    public IFormFileCollection? Files { get; set; }

    /// <summary> Данные для создания записи об обслуживании. </summary>
    public required CreateMaintenanceDto Dto { get; set; }
}

