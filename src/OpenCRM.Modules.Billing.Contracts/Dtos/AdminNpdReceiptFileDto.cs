namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Файл чека самозанятого для скачивания. </summary>
public sealed class AdminNpdReceiptFileDto
{
    /// <summary> Содержимое файла чека. </summary>
    public Stream Content { get; init; } = Stream.Null;

    /// <summary> Имя файла чека. </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary> MIME-тип содержимого файла чека. </summary>
    public string ContentType { get; init; } = "application/octet-stream";
}
