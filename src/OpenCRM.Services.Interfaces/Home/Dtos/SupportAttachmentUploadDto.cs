namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Ограниченное по размеру содержимое файла для отправки вместе с сообщением. </summary>
public sealed class SupportAttachmentUploadDto
{
    public string FileName { get; init; } = string.Empty;

    public byte[] Content { get; init; } = [];
}
