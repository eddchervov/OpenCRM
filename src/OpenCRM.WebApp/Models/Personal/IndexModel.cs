using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Personal;

#nullable disable

/// <summary> Страница просмотра профиля пользователя. </summary>
public class IndexModel
{
    /// <summary> Электронная почта пользователя. </summary>
    public string Email { get; set; }

    /// <summary> Имя пользователя. </summary>
    public string Username { get; set; }

    /// <summary> Номер телефона пользователя из профиля. </summary>
    [Display(Name = "Мобильный телефон")]
    public string PhoneNumber { get; set; }

    /// <summary> Количество автомобилей пользователя. </summary>
    public int CountVehicles { get; set; }

    /// <summary> Количество записей об обслуживании пользователя. </summary>
    public int CountMaintenance { get; set; }

    /// <summary> Дата и время создания учётной записи. </summary>
    public DateTime DatetimeCreation { get; set; }

    /// <summary> Признак подтверждённого адреса электронной почты. </summary>
    public bool IsEmailConfirmed { get; set; }

    /// <summary> Сообщение о результате операции для отображения на странице. </summary>
    public string StatusMessage { get; set; }
}
