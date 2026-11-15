using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Infrastructure.FileStorage.FileStorages;
using OpenCRM.Infrastructure.FileStorage.Media;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;

namespace OpenCRM.Infrastructure.FileStorage;

/// <summary> Методы расширения регистрации инфраструктуры файлового хранилища. </summary>
public static class FileStorageInfrastructureServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы файлового хранилища в контейнере. </summary>
    public static IServiceCollection AddOpenCRMFileStorageInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IFileStorageService, S3FileStorageProvider>();
        services.AddScoped<IMediaAccessService, MediaAccessService>();
        services.AddScoped<IMediaWorkspaceAccess, MediaWorkspaceAccess>();
        services.AddScoped<IImageProcessingService, ImageProcessingService>();
        return services;
    }
}
