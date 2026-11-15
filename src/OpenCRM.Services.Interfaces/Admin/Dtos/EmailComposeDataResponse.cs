namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Данные для подготовки email-рассылки. </summary>
public sealed class EmailComposeDataResponse
{
    /// <summary> Варианты типов организаций для выбора. </summary>
    public IReadOnlyList<EmailOrganizationTypeOption> OrganizationTypes { get; init; } = [];

    /// <summary> Варианты пользователей для выбора. </summary>
    public IReadOnlyList<EmailUserOption> Users { get; init; } = [];
}
