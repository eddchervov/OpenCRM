namespace OpenCRM.Services.Interfaces.Imports;

/// <summary>Черновик импорта файла.</summary>
public class ImportDraftDto
{
    public string? SourceKey { get; set; }

    /// <summary>Идентификатор черновика.</summary>
    public Guid Id { get; set; }

    /// <summary>Имя загруженного файла.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Номер версии черновика.</summary>
    public int Revision { get; set; }

    /// <summary>Время последнего изменения черновика.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Время завершения импорта.</summary>
    public DateTime? ImportedAt { get; set; }

    /// <summary>Данные исходного файла и подготовленные строки.</summary>
    public ImportDraftData Data { get; set; } = new();

    /// <summary>Результат завершённого импорта.</summary>
    public ImportResultDto? Result { get; set; }

    public int StorageVersion { get; set; }
    public string State { get; set; } = "ready";
    public string? Error { get; set; }
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int FilteredRows { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public Dictionary<string, int> Counts { get; set; } = [];
}
