
namespace OpenCRM.Modules.Vehicles.Contracts.Dtos
{
    /// <summary> Данные тарифного плана для отображения. </summary>
    public class SubscriptionPlanDisplayDto
    {
        /// <summary> Идентификатор тарифного плана. </summary>
        public int Id { get; set; }

        /// <summary> Название тарифного плана. </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> Описание тарифного плана. </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary> Цена тарифного плана. </summary>
        public decimal Price { get; set; }

        /// <summary> Лимит техники для плана. </summary>
        public int VehicleLimit { get; set; }

        /// <summary> Порядок отображения плана. </summary>
        public int Order { get; set; }
    }
}
