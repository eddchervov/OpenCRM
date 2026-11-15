namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Входящие обращения поддержки с фильтром по организации. </summary>
public class SupportInboxDto
{
    /// <summary> Доступные организации для фильтра обращений. </summary>
    public IReadOnlyList<SupportOrganizationOptionDto> Organizations { get; set; } = [];

    /// <summary> Список обращений. </summary>
    public IReadOnlyList<SupportConversationDto> Conversations { get; set; } = [];

    /// <summary> Выбранное обращение. </summary>
    public SupportConversationDto? Selected { get; set; }
}
