using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.FileStorages.Dtos;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OpenCRM.Infrastructure.FileStorage.FileStorages;

/// <summary> Провайдер файлового хранилища на основе S3. </summary>
public class S3FileStorageProvider : IFileStorageService
{
    /// <summary> Клиент S3. </summary>
    private readonly IAmazonS3? _s3Client;

    /// <summary> Имя бакета S3. </summary>
    private readonly string _bucketName;

    private readonly string? _privateBucketName;

    /// <summary> URL сервиса S3. </summary>
    private readonly string _serviceUrl;

    /// <summary> Логгер провайдера. </summary>
    private readonly ILogger<S3FileStorageProvider> _logger;

    /// <summary> Кеш хешей файлов. </summary>
    private readonly ConcurrentDictionary<string, string> _hashCache = new();

    /// <summary> Признак освобождения клиента при удалении провайдера. </summary>
    private readonly bool _disposeClient;

    /// <summary> Срок действия подписанного URL по умолчанию. </summary>
    private readonly TimeSpan _defaultPresignedUrlExpiration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Клиент S3. Нейтральная установка может работать без внешнего хранилища:
    /// в этом случае обращение к хранилищу сообщает о незаданной настройке.
    /// </summary>
    private IAmazonS3 S3Client => this._s3Client
        ?? throw new InvalidOperationException(
            "Файловое хранилище S3 не настроено: задайте S3:AccessKey, S3:SecretKey, S3:BucketName.");

    /// <summary> Создаёт провайдер с конфигурацией, логгером и опциональным клиентом S3. </summary>
    public S3FileStorageProvider(
        IConfiguration configuration,
        ILogger<S3FileStorageProvider> logger,
        IAmazonS3? s3Client = null)
    {
        _logger = logger;

        _serviceUrl = configuration["S3:ServiceUrl"] ?? throw new ArgumentException("S3 ServiceUrl is required");
        _bucketName = configuration["S3:BucketName"] ?? throw new ArgumentException("S3 BucketName is required");
        this._privateBucketName = configuration["S3:PrivateBucketName"];
        var accessKey = configuration["S3:AccessKey"];
        var secretKey = configuration["S3:SecretKey"];

        if (s3Client != null)
        {
            _s3Client = s3Client;
            _disposeClient = false;
        }
        else
        {
            if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            {
                // Нейтральная установка запускается без внешнего хранилища:
                // отсутствие ключей не мешает старту, но обращение к хранилищу сообщит об этом.
                _logger.LogWarning("Файловое хранилище S3 не настроено: S3:AccessKey и S3:SecretKey не заданы.");
                _s3Client = null;
                _disposeClient = false;
                return;
            }

            var config = new AmazonS3Config
            {
                ServiceURL = _serviceUrl,
                ForcePathStyle = true,
                UseHttp = true,
                Timeout = TimeSpan.FromSeconds(100),
                MaxErrorRetry = 3
            };

            _s3Client = new AmazonS3Client(accessKey, secretKey, config);
            _disposeClient = true;
        }
    }

    /// <summary> Не допускает сохранение закрытых архивов в общий бакет. </summary>
    public void ValidatePrivateStorage()
    {
        if (string.IsNullOrWhiteSpace(this._privateBucketName) || this._privateBucketName == this._bucketName)
        {
            throw new InvalidOperationException("Для выгрузок требуется отдельный закрытый S3:PrivateBucketName.");
        }
    }

    /// <summary> Передаёт готовый архив без повторного чтения для хеширования. </summary>
    public async Task<string> SavePrivateFileAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        this.ValidatePrivateStorage();
        path = this.NormalizePath(path);
        var response = await this.S3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = this._privateBucketName,
            Key = path,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = "application/zip"
        }, cancellationToken);
        if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new IOException("Хранилище не подтвердило сохранение закрытого архива.");
        }

        return path;
    }

    /// <summary> Получает закрытый объект только серверными учётными данными. </summary>
    public async Task<Stream> GetPrivateFileAsync(string path, CancellationToken cancellationToken)
    {
        this.ValidatePrivateStorage();
        try
        {
            var response = await this.S3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = this._privateBucketName, Key = this.NormalizePath(path)
            }, cancellationToken);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (IsFileNotFoundError(exception))
        {
            throw new FileNotFoundException("Готовый архив отсутствует в закрытом хранилище.", exception);
        }
    }

    /// <summary> Удаляет закрытый объект с учётом отмены и ошибок хранилища. </summary>
    public async Task<bool> DeletePrivateFileAsync(string path, CancellationToken cancellationToken)
    {
        this.ValidatePrivateStorage();
        var response = await this.S3Client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = this._privateBucketName, Key = this.NormalizePath(path)
        }, cancellationToken);

        return response.HttpStatusCode == System.Net.HttpStatusCode.NoContent;
    }

    #region Основные методы (из вашего интерфейса)

    /// <summary> Сохраняет файл в хранилище и возвращает его путь. </summary>
    public async Task<string> SaveFileAsync(string path, Stream content)
    {
        return await SaveFileAsync(path, content, CancellationToken.None);
    }

    /// <summary> Возвращает поток с содержимым файла. </summary>
    public Task<Stream> GetFileAsync(string path) => this.GetFileAsync(path, CancellationToken.None);

    /// <summary> Открывает поток с возможностью отмены обращения к S3. </summary>
    public async Task<Stream> GetFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            path = NormalizePath(path);

            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = path
            };

            var response = await S3Client.GetObjectAsync(request, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (IsFileNotFoundError(ex))
        {
            throw new FileNotFoundException($"File not found in S3: {path}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file from S3: {Path}", path);
            throw;
        }
    }

    /// <summary> Генерирует подписанный URL файла со сроком по умолчанию. </summary>
    public async Task<string> GeneratePresignedUrlAsync(string path)
    {
        return await GeneratePresignedUrlAsync(path, _defaultPresignedUrlExpiration);
    }

    /// <summary> Проверяет существование файла в хранилище. </summary>
    public async Task<bool> FileExistsAsync(string path)
    {
        try
        {
            path = NormalizePath(path);

            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = path
            };

            await S3Client.GetObjectMetadataAsync(request);
            return true;
        }
        catch (AmazonS3Exception ex) when (IsFileNotFoundError(ex))
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if file exists in S3: {Path}", path);
            throw;
        }
    }

    /// <summary> Определяет, является ли ошибка S3 отсутствием файла. </summary>
    private static bool IsFileNotFoundError(AmazonS3Exception ex)
    {
        return ex.ErrorCode == "NoSuchKey" ||
               ex.ErrorCode == "NotFound" ||
               ex.StatusCode == System.Net.HttpStatusCode.NotFound;
    }

    /// <summary> Возвращает пути файлов с указанным префиксом. </summary>
    public async Task<IEnumerable<string>> FindFilesByPrefixAsync(string prefix)
    {
        var result = new List<string>();

        try
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = prefix != null ? NormalizePath(prefix) : null,
                MaxKeys = 1000
            };

            ListObjectsV2Response response;
            do
            {
                response = await S3Client.ListObjectsV2Async(listRequest);
                result.AddRange(response.S3Objects.Select(x => x.Key));

                listRequest.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated == true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding files by prefix: {Prefix}", prefix);
        }

        return result;
    }

    /// <summary> Вычисляет хеш файла, используя кеш. </summary>
    public async Task<string> GetFileHashAsync(string filePath)
    {
        try
        {
            filePath = NormalizePath(filePath);

            // Проверяем кэш
            if (_hashCache.Values.Contains(filePath))
            {
                var cachedHash = _hashCache.FirstOrDefault(x => x.Value == filePath).Key;
                if (cachedHash != null) return cachedHash;
            }

            // Получаем из метаданных
            var metadata = await GetFileMetadataAsync(filePath);
            if (metadata.TryGetValue("file-hash", out var hash))
            {
                _hashCache[hash] = filePath;
                return hash;
            }

            // Вычисляем заново
            using var stream = await GetFileAsync(filePath);
            return await ComputeFileHashAsync(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file hash: {Path}", filePath);
            throw;
        }
    }

    #endregion

    #region Дополнительные методы

    /// <summary> Сохраняет файл с метаданными и возвращает его путь. </summary>
    public Task<string> SaveFileAsync(string path, Stream content, IDictionary<string, string> metadata) =>
        this.SaveFileAsync(path, content, metadata, CancellationToken.None);

    /// <summary> Сохраняет файл с возможностью отмены хеширования и передачи. </summary>
    public Task<string> SaveFileAsync(string path, Stream content, CancellationToken cancellationToken) =>
        this.SaveFileAsync(path, content, null, cancellationToken);

    private async Task<string> SaveFileAsync(
        string path, Stream content, IDictionary<string, string>? metadata, CancellationToken cancellationToken)
    {
        try
        {
            path = NormalizePath(path);

            // Вычисляем хэш содержимого
            content.Position = 0;
            var fileHash = await ComputeFileHashAsync(content, cancellationToken);
            content.Position = 0;

            // Подготавливаем метаданные
            var requestMetadata = new Dictionary<string, string>(metadata ?? new Dictionary<string, string>())
            {
                ["file-hash"] = fileHash,
                ["upload-date"] = DateTime.UtcNow.ToString("O"),
                ["original-filename"] = Path.GetFileName(path)
            };

            // Загружаем файл в S3
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = path,
                InputStream = content,
                AutoCloseStream = false,
                ContentType = GetContentType(path)
            };

            foreach (var meta in requestMetadata)
            {
                request.Metadata.Add(meta.Key, meta.Value);
            }

            var response = await S3Client.PutObjectAsync(request, cancellationToken);

            if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                _hashCache[fileHash] = path;
                _logger.LogInformation("File saved to S3: {Path} with hash: {Hash}", path, fileHash);
                return path;
            }

            throw new Exception($"Failed to upload file to S3. Status: {response.HttpStatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving file to S3: {Path}", path);
            throw;
        }
    }

    /// <summary> Удаляет файл и сообщает об успехе. </summary>
    public Task<bool> DeleteFileAsync(string path) => this.DeleteFileAsync(path, CancellationToken.None);

    /// <summary> Удаляет объект с возможностью отмены запроса к S3. </summary>
    public async Task<bool> DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            path = NormalizePath(path);

            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = path
            };

            var response = await S3Client.DeleteObjectAsync(request, cancellationToken);
            var success = response.HttpStatusCode == System.Net.HttpStatusCode.NoContent;

            if (success)
            {
                // Удаляем из кэша хэшей
                var hashToRemove = _hashCache.FirstOrDefault(x => x.Value == path).Key;
                if (hashToRemove != null)
                {
                    _hashCache.TryRemove(hashToRemove, out _);
                }

                _logger.LogInformation("File deleted from S3: {Path}", path);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from S3: {Path}", path);
            return false;
        }
    }

    /// <summary> Возвращает метаданные файла. </summary>
    public async Task<IDictionary<string, string>> GetFileMetadataAsync(string path)
    {
        try
        {
            path = NormalizePath(path);

            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = path
            };

            var response = await S3Client.GetObjectMetadataAsync(request);
            var metadataDictionary = new Dictionary<string, string>();

            foreach (var key in response.Metadata.Keys)
            {
                metadataDictionary[key] = response.Metadata[key];
            }

            return metadataDictionary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file metadata: {Path}", path);
            throw;
        }
    }

    /// <summary> Обновляет метаданные файла. </summary>
    public async Task<bool> UpdateFileMetadataAsync(string path, IDictionary<string, string> metadata)
    {
        try
        {
            path = NormalizePath(path);

            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = path,
                DestinationBucket = _bucketName,
                DestinationKey = path,
                MetadataDirective = S3MetadataDirective.REPLACE
            };

            foreach (var meta in metadata)
            {
                copyRequest.Metadata.Add(meta.Key, meta.Value);
            }

            var response = await S3Client.CopyObjectAsync(copyRequest);
            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file metadata: {Path}", path);
            return false;
        }
    }

    /// <summary> Генерирует подписанный URL файла с заданным сроком действия. </summary>
    public async Task<string> GeneratePresignedUrlAsync(string path, TimeSpan expiration)
    {
        try
        {
            path = NormalizePath(path);

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = path,
                Expires = DateTime.UtcNow.Add(expiration),
                Protocol = new Uri(this._serviceUrl).Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
                Verb = HttpVerb.GET
            };

            return await S3Client.GetPreSignedURLAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating presigned URL for: {Path}", path);
            throw;
        }
    }

    /// <summary> Возвращает публичный URL файла по пути. </summary>
    public string GetFileUrl(string path)
    {
        path = NormalizePath(path);
        return $"{_serviceUrl.TrimEnd('/')}/{_bucketName}/{path}";
    }

    /// <summary> Возвращает информацию о файле. </summary>
    public async Task<FileInfoDto> GetFileInfoAsync(string path)
    {
        try
        {
            path = NormalizePath(path);

            var metadataRequest = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = path
            };

            var metadata = await S3Client.GetObjectMetadataAsync(metadataRequest);
            var fileMetadata = await GetFileMetadataAsync(path);

            return new FileInfoDto
            {
                Path = path,
                Name = Path.GetFileName(path),
                Size = metadata.ContentLength,
                ContentType = metadata.Headers.ContentType,
                LastModified = metadata.LastModified,
                Metadata = fileMetadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file info: {Path}", path);
            throw;
        }
    }

    #endregion

    /// <summary> Освобождает ресурсы клиента S3, если это разрешено. </summary>
    public void Dispose()
    {
        if (_disposeClient)
        {
            _s3Client?.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    #region Private Methods

    /// <summary> Нормализует путь файла для хранилища. </summary>
    private string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    /// <summary> Определяет MIME-тип файла по расширению пути. </summary>
    private string GetContentType(string path)
    {
        var extension = Path.GetExtension(path).ToLower();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    /// <summary> Вычисляет хеш содержимого потока. </summary>
    private async Task<string> ComputeFileHashAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        stream.Position = 0;
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        stream.Position = 0;
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    #endregion
}
