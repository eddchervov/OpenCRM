using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.WebApp.Models.Personal;

/// <summary> Модель отдельной формы создания обращения в поддержку. </summary>
public class CreateSupportViewModel
{
    /// <summary> Организации пользователя, доступные для выбора в обращении. </summary>
    public IReadOnlyList<SupportOrganizationOptionDto> Organizations { get; set; } = [];

    /// <summary> Введённые значения формы. </summary>
    public CreateSupportRequest Request { get; set; } = new();

    /// <summary> Ошибка проверки или отправки обращения. </summary>
    public string? Error { get; set; }
}
