namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Содержимое вложения после проверки доступа. </summary>
public sealed class SupportAttachmentContentDto
{
    public string FileName { get; init; } = string.Empty;

    public byte[] Content { get; init; } = [];
}
