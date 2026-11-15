
using OpenCRM.Services.Interfaces.FileStorages.Dtos;

namespace OpenCRM.Services.Interfaces.FileStorages;

/// <summary> Контракт сервиса работы с файловым хранилищем. </summary>
public interface IFileStorageService : IDisposable
{
    // Базовые методы (из вашего текущего интерфейса)
    /// <summary> Сохраняет файл по указанному пути и возвращает его путь. </summary>
    Task<string> SaveFileAsync(string path, Stream content);

    /// <summary> Получает содержимое файла по пути. </summary>
    Task<Stream> GetFileAsync(string path);

    /// <summary> Открывает поток чтения с возможностью отмены запроса к хранилищу. </summary>
    Task<Stream> GetFileAsync(string path, CancellationToken cancellationToken);

    /// <summary> Сохраняет файл с возможностью отмены чтения и загрузки. </summary>
    Task<string> SaveFileAsync(string path, Stream content, CancellationToken cancellationToken);

    /// <summary> Проверяет наличие отдельного закрытого хранилища до подготовки больших файлов. </summary>
    void ValidatePrivateStorage();

    /// <summary> Сохраняет временный закрытый объект потоково, без вычисления хеша для дедупликации. </summary>
    Task<string> SavePrivateFileAsync(string path, Stream content, CancellationToken cancellationToken);

    /// <summary> Открывает объект из закрытого хранилища. </summary>
    Task<Stream> GetPrivateFileAsync(string path, CancellationToken cancellationToken);

    /// <summary> Удаляет временный закрытый объект. </summary>
    Task<bool> DeletePrivateFileAsync(string path, CancellationToken cancellationToken);

    /// <summary> Генерирует предварительно подписанный URL для файла. </summary>
    Task<string> GeneratePresignedUrlAsync(string path);

    /// <summary> Проверяет существование файла по пути. </summary>
    Task<bool> FileExistsAsync(string path);

    /// <summary> Находит файлы по префиксу пути. </summary>
    Task<IEnumerable<string>> FindFilesByPrefixAsync(string prefix);

    /// <summary> Вычисляет хеш файла. </summary>
    Task<string> GetFileHashAsync(string filePath);

    // Дополнительные методы (опционально)
    /// <summary> Сохраняет файл с метаданными и возвращает его путь. </summary>
    Task<string> SaveFileAsync(string path, Stream content, IDictionary<string, string> metadata);

    /// <summary> Удаляет файл по пути. </summary>
    Task<bool> DeleteFileAsync(string path);

    /// <summary> Удаляет файл с возможностью отмены запроса к хранилищу. </summary>
    Task<bool> DeleteFileAsync(string path, CancellationToken cancellationToken);

    /// <summary> Получает метаданные файла. </summary>
    Task<IDictionary<string, string>> GetFileMetadataAsync(string path);

    /// <summary> Обновляет метаданные файла. </summary>
    Task<bool> UpdateFileMetadataAsync(string path, IDictionary<string, string> metadata);

    /// <summary> Генерирует предварительно подписанный URL с ограниченным сроком действия. </summary>
    Task<string> GeneratePresignedUrlAsync(string path, TimeSpan expiration);

    /// <summary> Возвращает URL файла. </summary>
    string GetFileUrl(string path);

    /// <summary> Получает сведения о файле. </summary>
    Task<FileInfoDto> GetFileInfoAsync(string path);
}
