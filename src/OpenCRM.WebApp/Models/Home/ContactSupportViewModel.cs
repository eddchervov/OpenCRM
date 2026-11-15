using System.ComponentModel.DataAnnotations;
using OpenCRM.Domain;

namespace OpenCRM.WebApp.Models.Home;

/// <summary> Модель формы обращения в поддержку. </summary>
public class ContactSupportViewModel
{
    /// <summary> Код типа обращения в поддержку. </summary>
    [Required(ErrorMessage = "Выберите тип обращения.")]
    public string TypeCode { get; set; } = SupportMessageTypeCodes.Question;

    /// <summary> Имя отправителя обращения. </summary>
    [Required(ErrorMessage = "Укажите имя.")]
    [StringLength(160, ErrorMessage = "Имя должно быть не длиннее 160 символов.")]
    public string Name { get; set; } = string.Empty;

    /// <summary> Электронная почта отправителя. </summary>
    [Required(ErrorMessage = "Укажите email.")]
    [EmailAddress(ErrorMessage = "Укажите корректный email.")]
    [StringLength(254, ErrorMessage = "Email должен быть не длиннее 254 символов.")]
    public string Email { get; set; } = string.Empty;

    /// <summary> Телефон отправителя, если указан. </summary>
    [StringLength(64, ErrorMessage = "Телефон должен быть не длиннее 64 символов.")]
    public string? Phone { get; set; }

    /// <summary> Тема обращения. </summary>
    [Required(ErrorMessage = "Укажите тему.")]
    [StringLength(180, MinimumLength = 4, ErrorMessage = "Тема должна быть от 4 до 180 символов.")]
    public string Topic { get; set; } = string.Empty;

    /// <summary> Текст обращения. </summary>
    [Required(ErrorMessage = "Опишите вопрос или предложение.")]
    [StringLength(4000, MinimumLength = 20, ErrorMessage = "Сообщение должно быть от 20 до 4000 символов.")]
    public string Text { get; set; } = string.Empty;

    /// <summary> Страница, с которой отправлено обращение, если известна. </summary>
    public string? SourcePage { get; set; }

    /// <summary> Адрес сайта отправителя, если указан. </summary>
    public string? Website { get; set; }

    /// <summary> Время отображения формы в тиках для защиты от ботов. </summary>
    public long FormRenderedAtTicks { get; set; } = DateTime.UtcNow.Ticks;

    /// <summary> Доступные варианты типа обращения. </summary>
    public IReadOnlyList<SupportTypeOption> TypeOptions { get; init; } =
    [
        new(SupportMessageTypeCodes.Question, "Вопрос"),
        new(SupportMessageTypeCodes.Suggestion, "Предложение"),
        new(SupportMessageTypeCodes.Bug, "Ошибка"),
        new(SupportMessageTypeCodes.Billing, "Тарифы и оплата"),
        new(SupportMessageTypeCodes.Cooperation, "Сотрудничество")
    ];

    /// <summary> Файлы, выбранные для отправки с обращением. </summary>
    public List<IFormFile> Attachments { get; set; } = [];
}

/// <summary> Вариант типа обращения в поддержку. </summary>
public sealed record SupportTypeOption(string Code, string Name);
