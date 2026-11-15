using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Media.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Media;

/// <summary> Контракт сервиса обработки изображений. </summary>
public interface IImageProcessingService
{
    /// <summary> Обрабатывает и сохраняет изображение с заданными параметрами. </summary>
    Task<string> ProcessAndSaveImageAsync(Stream imageStream, ImageProcessingOptions options);

    /// <summary> Обрабатывает и сохраняет изображение указанного типа медиа. </summary>
    Task<string> ProcessAndSaveImageAsync(Stream imageStream, MediaType mediaType, string? userId = null);

    /// <summary> Проверяет существование изображения по пути. </summary>
    Task<bool> ImageExistsAsync(string filePath);

    /// <summary> Вычисляет хеш изображения. </summary>
    Task<string> ComputeImageHashAsync(Stream imageStream);

    /// <summary> Определяет формат изображения. </summary>
    Task<string> GetImageFormatAsync(Stream imageStream);
}
