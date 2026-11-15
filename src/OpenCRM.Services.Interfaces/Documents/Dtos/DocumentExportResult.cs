using System;

namespace OpenCRM.Services.Interfaces.Documents.Dtos;

/// <summary> Результат экспорта документа. </summary>
public sealed class DocumentExportResult
{
    /// <summary> Создаёт результат экспорта с указанным исходом. </summary>
    private DocumentExportResult(
        DocumentExportOutcome outcome,
        string? message = null,
        byte[]? content = null,
        string? contentType = null,
        string? fileName = null)
    {
        this.Outcome = outcome;
        this.Message = message;
        this.Content = content;
        this.ContentType = contentType;
        this.FileName = fileName;
    }

    /// <summary> Исход экспорта документа. </summary>
    public DocumentExportOutcome Outcome { get; }

    /// <summary> Сообщение об исходе, если есть. </summary>
    public string? Message { get; }

    /// <summary> Содержимое файла, если документ сформирован. </summary>
    public byte[]? Content { get; }

    /// <summary> MIME-тип содержимого, если документ сформирован. </summary>
    public string? ContentType { get; }

    /// <summary> Имя файла, если задано. </summary>
    public string? FileName { get; }

    /// <summary> Создаёт результат с содержимым файла документа. </summary>
    public static DocumentExportResult File(byte[] content, string contentType, string? fileName = null)
    {
        return new DocumentExportResult(DocumentExportOutcome.File, content: content, contentType: contentType, fileName: fileName);
    }

    /// <summary> Создаёт результат с отказом в доступе. </summary>
    public static DocumentExportResult AccessDenied(string message)
    {
        return new DocumentExportResult(DocumentExportOutcome.AccessDenied, message);
    }

    /// <summary> Создаёт результат с отсутствием документа. </summary>
    public static DocumentExportResult NotFound(string message)
    {
        return new DocumentExportResult(DocumentExportOutcome.NotFound, message);
    }

    /// <summary> Создаёт результат с некорректным запросом. </summary>
    public static DocumentExportResult InvalidRequest(string message)
    {
        return new DocumentExportResult(DocumentExportOutcome.InvalidRequest, message);
    }
}
