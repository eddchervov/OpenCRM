using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка пользователей для отображения. </summary>
public sealed class AdminUserRow(AdminUserListItem user)
{
    /// <summary> Идентификатор пользователя. </summary>
    public string Id => user.Id;

    /// <summary> Email пользователя. </summary>
    public string Email => string.IsNullOrWhiteSpace(user.Email) ? "Без email" : user.Email;

    /// <summary> Контактные данные пользователя. </summary>
    public string Contact => string.IsNullOrWhiteSpace(user.Phone) ? "—" : user.Phone;

    /// <summary> Статус пользователя. </summary>
    public string Status => user.IsDeleted ? "Удалён" : user.EmailConfirmed ? "Email подтверждён" : "Email не подтверждён";

    /// <summary> Признак удалённого пользователя. </summary>
    public bool IsDeleted => user.IsDeleted;

    /// <summary> Дата регистрации. </summary>
    public string Registered => AdminUserRow.FormatDate(user.DatetimeCreation);

    /// <summary> Дата последнего входа. </summary>
    public string LastLogin => AdminUserRow.FormatDate(user.LastLoginDate, "Не входил");

    /// <summary> Число транспортных средств. </summary>
    public int VehiclesCount => user.CountVehicles;

    /// <summary> Число обслуживаний. </summary>
    public int MaintenanceCount => user.CountMaintenance;

    /// <summary> Число организаций. </summary>
    public int OrganizationsCount => user.OrganizationsCount;

    /// <summary> Источник регистрации. </summary>
    public string Source => string.IsNullOrWhiteSpace(user.FirstUtmSource) ? "—" : user.FirstUtmSource;

    /// <summary> Форматирует дату или возвращает прочерк. </summary>
    private static string FormatDate(DateTime? date, string empty = "—") => date is { Year: > 1 } value
        ? value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
        : empty;
}
