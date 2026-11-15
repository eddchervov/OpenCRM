namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Доступные пользователю сведения о вложении без адреса хранилища. </summary>
public class SupportAttachmentDto
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}
