//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.Logging;
//using OpenCRM.Services.Interfaces.FileStorages;

//namespace OpenCRM.Infrastructure.FileStorage.FileStorages;

//public class LocalFileStorageProvider : IFileStorageProvider
//{
//    private readonly string _basePath;
//    private readonly ILogger<LocalFileStorageProvider> _logger;
//    private readonly string _hashMapPath;
//    private Dictionary<string, string> _hashMap; // hash -> filePath

//    public LocalFileStorageProvider(IConfiguration configuration, ILogger<LocalFileStorageProvider> logger)
//    {
//        _basePath = configuration["StorageFilesPath"] ?? "wwwroot";
//        _logger = logger;

//        // Создаем базовую директорию если не существует
//        if (!Directory.Exists(_basePath))
//            Directory.CreateDirectory(_basePath);
//    }

//    public async Task SaveFileAsync(string path, Stream content)
//    {
//        var filePath = GetFilePath(path);
//        var directory = Path.GetDirectoryName(filePath);

//        if (!Directory.Exists(directory))
//            Directory.CreateDirectory(directory);

//        try
//        {
//            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
//            await content.CopyToAsync(fileStream);
//            await fileStream.FlushAsync();

//            _logger.LogInformation("File saved: {FilePath}", filePath);
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Error saving file: {Path}", path);
//            throw;
//        }
//    }

//    public async Task<Stream> GetFileAsync(string path)
//    {
//        var filePath = GetFilePath(path);

//        if (!File.Exists(filePath))
//            throw new FileNotFoundException($"File not found: {path}");

//        try
//        {
//            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Error reading file: {Path}", path);
//            throw;
//        }
//    }

//    public Task<bool> DeleteFileAsync(string path)
//    {
//        var filePath = GetFilePath(path);

//        try
//        {
//            if (File.Exists(filePath))
//            {
//                File.Delete(filePath);
//                _logger.LogInformation("File deleted: {FilePath}", filePath);
//                return Task.FromResult(true);
//            }
//            return Task.FromResult(false);
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Error deleting file: {Path}", path);
//            return Task.FromResult(false);
//        }
//    }

//    public Task<bool> FileExistsAsync(string path)
//    {
//        var filePath = GetFilePath(path);
//        return Task.FromResult(File.Exists(filePath));
//    }

//    public string GetFileUrl(string path)
//    {
//        // Убираем базовый путь из URL
//        if (path.StartsWith(_basePath))
//        {
//            return path.Substring(_basePath.Length).Replace('\\', '/').TrimStart('/');
//        }
//        return path.Replace('\\', '/');
//    }

//    private string GetFilePath(string path)
//    {
//        // Защита от path traversal attacks
//        var safePath = Path.GetFullPath(Path.Combine(_basePath, path))
//            .Replace("..", string.Empty)
//            .TrimStart(Path.DirectorySeparatorChar);

//        return Path.Combine(_basePath, safePath);
//    }

//    public Task<IEnumerable<string>> FindFilesByPrefixAsync(string prefix)
//    {
//        throw new NotImplementedException();
//    }

//    public Task<string> GeneratePresignedUrlAsync(string path, TimeSpan expiryTime)
//    {
//        throw new NotImplementedException();
//    }
//}
