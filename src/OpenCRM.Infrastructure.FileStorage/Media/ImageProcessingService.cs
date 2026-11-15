using Helpers;
using Microsoft.Extensions.Logging;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Media;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Infrastructure.FileStorage.Media;

/// <summary> Сервис обработки и сохранения изображений. </summary>
public class ImageProcessingService : IImageProcessingService
{
    /// <summary> Зависимость сервиса файлового хранилища. </summary>
    private readonly IFileStorageService _fileStorageService;

    /// <summary> Логгер сервиса. </summary>
    private readonly ILogger<ImageProcessingService> _logger;

    /// <summary> Длина хеша изображения. </summary>
    private const int HashLength = 24;

    /// <summary> Создаёт сервис с зависимостями хранилища и логгера. </summary>
    public ImageProcessingService(
        IFileStorageService fileStorageService,
        ILogger<ImageProcessingService> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    /// <summary> Обрабатывает изображение с заданными параметрами и сохраняет его. </summary>
    public async Task<string> ProcessAndSaveImageAsync(Stream imageStream, ImageProcessingOptions options)
    {
        if (imageStream == null || imageStream.Length == 0)
            throw new ArgumentException("Image stream is empty");

        try
        {
            // Обрабатываем изображение.
            imageStream.Position = 0;
            using var image = await ImageDecoding.LoadAsync(imageStream);
            using var resizedImage = ImageUtils.ResizeToMaxDimensions(image, options.MaxWidth, options.MaxHeight);

            // Сохраняем результат во временный поток.
            using var memoryStream = new MemoryStream();
            var encoder = GetImageEncoder(options.Quality);
            await resizedImage.SaveAsync(memoryStream, encoder);

            // Генерируем имя файла.
            string fileName;
            if (options.GenerateHash)
            {
                memoryStream.Position = 0;
                var hash = await ComputeImageHashAsync(memoryStream);
                fileName = $"{hash.Substring(0, HashLength)}.jpg";
            }
            else
            {
                fileName = $"{Guid.NewGuid():N}.jpg";
            }

            var storagePath = $"{options.TargetFolder.TrimEnd('/')}/{fileName}";

            // Проверяем существование файла.
            if (options.GenerateHash && await _fileStorageService.FileExistsAsync(storagePath))
            {
                _logger.LogInformation("File already exists: {Path}", storagePath);
                return storagePath;
            }

            // Сохраняем файл.
            memoryStream.Position = 0;
            var savedPath = await _fileStorageService.SaveFileAsync(storagePath, memoryStream);

            _logger.LogInformation("Image processed and saved: {Path}", savedPath);

            return savedPath;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            this._logger.LogWarning(ex, "Изображение повреждено или имеет неподдерживаемый формат.");

            throw new InvalidDataException("Изображение повреждено или имеет неподдерживаемый формат.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image");
            throw;
        }
    }

    /// <summary> Обрабатывает изображение с типом медиа и сохраняет его. </summary>
    public async Task<string> ProcessAndSaveImageAsync(Stream imageStream, MediaType mediaType, string? userId = null)
    {
        var options = mediaType switch
        {
            MediaType.VehiclePhoto => new ImageProcessingOptions
            {
                TargetFolder = userId != null ? $"img/vehicles/{userId}" : "img/system/vehicles",
                MaxWidth = 1200,
                MaxHeight = 800,
                Quality = 75,
                GenerateHash = true
            },
            MediaType.MaintenancePhoto => new ImageProcessingOptions
            {
                TargetFolder = userId != null ? $"img/maintenance/{userId}" : "img/system/maintenance",
                MaxWidth = 1200,
                MaxHeight = 800,
                Quality = 75,
                GenerateHash = true
            },
            _ => new ImageProcessingOptions
            {
                TargetFolder = userId != null ? $"img/other/{userId}" : "img/system",
                MaxWidth = 1200,
                MaxHeight = 800,
                Quality = 75,
                GenerateHash = false
            }
        };

        return await ProcessAndSaveImageAsync(imageStream, options);
    }

    /// <summary> Проверяет существование изображения по пути. </summary>
    public async Task<bool> ImageExistsAsync(string filePath)
    {
        return await _fileStorageService.FileExistsAsync(filePath);
    }

    /// <summary> Вычисляет хеш изображения из потока. </summary>
    public async Task<string> ComputeImageHashAsync(Stream stream)
    {
        using var sha256 = SHA256.Create();
        stream.Position = 0;
        var hashBytes = await sha256.ComputeHashAsync(stream);
        stream.Position = 0;
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    /// <summary> Определяет формат изображения из потока. </summary>
    public async Task<string> GetImageFormatAsync(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var mimeType = await ImageDecoding.IdentifyMimeTypeAsync(stream);
            stream.Position = 0;

            return mimeType ?? "image/jpeg";
        }
        catch
        {
            return "image/jpeg";
        }
    }

    /// <summary> Выбирает кодировщик изображения по качеству. </summary>
    private IImageEncoder GetImageEncoder(int quality)
    {
        return new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = quality };
    }
}


