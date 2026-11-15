using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.WebApp.Models.Personal;

/// <summary> Модель страницы поддержки с обращениями пользователя. </summary>
public class SupportViewModel
{
    /// <summary> Список организаций, доступных для выбора при обращении в поддержку. </summary>
    public IReadOnlyList<SupportOrganizationOptionDto> Organizations { get; set; } = [];

    /// <summary> Список обращений в поддержку. </summary>
    public IReadOnlyList<SupportConversationDto> Conversations { get; set; } = [];

    /// <summary> Выбранное обращение в поддержку. </summary>
    public SupportConversationDto? Selected { get; set; }

    /// <summary> Текст ошибки при работе с обращениями. </summary>
    public string? Error { get; set; }
}

/// <summary> Модель создания нового обращения в поддержку. </summary>
public class CreateSupportRequest
{
    /// <summary> Файлы, выбранные для отправки с сообщением. </summary>
    public List<IFormFile> Attachments { get; set; } = [];

    /// <summary> Код типа обращения. </summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary> Тема обращения. </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary> Текст обращения. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Признак вопроса, относящегося к организации. </summary>
    public bool IsOrganizationQuestion { get; set; }

    /// <summary> Идентификатор организации, к которой относится обращение. </summary>
    public Guid? OrganizationId { get; set; }
}

/// <summary> Модель ответа на обращение в поддержку. </summary>
public class ReplySupportRequest
{
    /// <summary> Файлы, выбранные для отправки с сообщением. </summary>
    public List<IFormFile> Attachments { get; set; } = [];

    /// <summary> Идентификатор обращения, к которому даётся ответ. </summary>
    public int Id { get; set; }

    /// <summary> Текст ответа на обращение. </summary>
    public string Text { get; set; } = string.Empty;
}
