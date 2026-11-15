namespace OpenCRM.Admin.Api.Models;

/// <summary> Скачанный файл чека НПД. </summary>
public sealed record AdminNpdReceiptFile(byte[] Content, string ContentType, string FileName);
