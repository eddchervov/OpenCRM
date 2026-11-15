namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Данные для изменения комментария клиента организации. </summary>
public class UpdateOrganizationClientCommentDto
{
    /// <summary> Комментарий клиента. </summary>
    public string? Comment { get; set; }
}
