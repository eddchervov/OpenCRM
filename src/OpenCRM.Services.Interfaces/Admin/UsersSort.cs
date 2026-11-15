namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Варианты сортировки списка пользователей. </summary>
public enum UsersSort
{
    /// <summary> Сортировка по дате создания по убыванию. </summary>
    ByDateDesc = 1,

    /// <summary> Сортировка по дате создания по возрастанию. </summary>
    ByDateAsc = 2,

    /// <summary> Сортировка по email по возрастанию. </summary>
    ByEmailAsc = 3,

    /// <summary> Сортировка по email по убыванию. </summary>
    ByEmailDesc = 4,

    /// <summary> Сортировка по дате последнего входа по убыванию. </summary>
    ByLastLoginDesc = 5,

    /// <summary> Сортировка по дате последнего входа по возрастанию. </summary>
    ByLastLoginAsc = 6,

    /// <summary> Сортировка по количеству техники по убыванию. </summary>
    ByVehiclesDesc = 7,

    /// <summary> Сортировка по количеству техники по возрастанию. </summary>
    ByVehiclesAsc = 8,

    /// <summary> Сортировка по количеству обслуживаний по убыванию. </summary>
    ByMaintenanceDesc = 9,

    /// <summary> Сортировка по количеству обслуживаний по возрастанию. </summary>
    ByMaintenanceAsc = 10,

    /// <summary> Сортировка по количеству организаций по убыванию. </summary>
    ByOrganizationsDesc = 11,

    /// <summary> Сортировка по количеству организаций по возрастанию. </summary>
    ByOrganizationsAsc = 12,

    /// <summary> Сортировка по тарифному плану по возрастанию. </summary>
    ByPlanAsc = 13,

    /// <summary> Сортировка по тарифному плану по убыванию. </summary>
    ByPlanDesc = 14,

    /// <summary> Сортировка по UTM-источнику по возрастанию. </summary>
    BySourceAsc = 15,

    /// <summary> Сортировка по UTM-источнику по убыванию. </summary>
    BySourceDesc = 16
}
