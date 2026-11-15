namespace OpenCRM.Admin.Api.Models;

/// <summary> Варианты сортировки списка пользователей. </summary>
public enum AdminUsersSort
{
    /// <summary> Сортировка по дате по убыванию. </summary>
    ByDateDesc = 1,

    /// <summary> Сортировка по дате по возрастанию. </summary>
    ByDateAsc = 2,

    /// <summary> Сортировка по email по возрастанию. </summary>
    ByEmailAsc = 3,

    /// <summary> Сортировка по email по убыванию. </summary>
    ByEmailDesc = 4,

    /// <summary> Сортировка по последнему входу по убыванию. </summary>
    ByLastLoginDesc = 5,

    /// <summary> Сортировка по последнему входу по возрастанию. </summary>
    ByLastLoginAsc = 6,

    /// <summary> Сортировка по числу транспортных средств по убыванию. </summary>
    ByVehiclesDesc = 7,

    /// <summary> Сортировка по числу транспортных средств по возрастанию. </summary>
    ByVehiclesAsc = 8,

    /// <summary> Сортировка по числу обслуживаний по убыванию. </summary>
    ByMaintenanceDesc = 9,

    /// <summary> Сортировка по числу обслуживаний по возрастанию. </summary>
    ByMaintenanceAsc = 10,

    /// <summary> Сортировка по числу организаций по убыванию. </summary>
    ByOrganizationsDesc = 11,

    /// <summary> Сортировка по числу организаций по возрастанию. </summary>
    ByOrganizationsAsc = 12,

    /// <summary> Сортировка по тарифу по возрастанию. </summary>
    ByPlanAsc = 13,

    /// <summary> Сортировка по тарифу по убыванию. </summary>
    ByPlanDesc = 14,

    /// <summary> Сортировка по источнику по возрастанию. </summary>
    BySourceAsc = 15,

    /// <summary> Сортировка по источнику по убыванию. </summary>
    BySourceDesc = 16
}
