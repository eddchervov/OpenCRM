using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.WebApp.Models.Maintenance
{
    /// <summary> Модель формы редактирования записи об обслуживании. </summary>
    public class UpdateMaintenanceFormModel
    {
        /// <summary> Идентификатор записи об обслуживании. </summary>
        public required Guid MaintenanceId { get; set; }

        /// <summary> Данные автомобиля, к которому относится обслуживание. </summary>
        public required GetVehicleDto Vehicle { get; set; }
    }
}



