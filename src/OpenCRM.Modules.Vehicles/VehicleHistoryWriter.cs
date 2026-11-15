using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.History;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Сервис записи событий в историю техники. </summary>
public sealed class VehicleHistoryWriter : IVehicleDocumentHistoryService
{
    /// <summary> Тип события истории о генерации документа по технике. </summary>
    private const string VehicleEventDocumentGenerated = "document_generated";

    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Системные часы для фиксации времени. </summary>
    private readonly ISystemClock _systemClock;

    /// <summary> Репозиторий техники. </summary>
    private readonly IVehicleRepository _vehicleRepository;

    /// <summary> Сервис подписки организации для проверок. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Создаёт сервис записи истории техники с зависимостями. </summary>
    public VehicleHistoryWriter(
        OpenCRMContext context,
        ISystemClock systemClock,
        IVehicleRepository vehicleRepository,
        IOrganizationSubscriptionService organizationSubscriptionService)
    {
        this._context = context;
        this._systemClock = systemClock;
        this._vehicleRepository = vehicleRepository;
        this._organizationSubscriptionService = organizationSubscriptionService;
    }

    /// <summary> Фиксирует событие генерации документа по технике. </summary>
    public async Task TrackDocumentGenerated(string userId, Guid? organizationId, Guid vehicleId, string title, string? description = null)
    {
        await this.EnsureCanReadWorkspaceAsync(userId, organizationId);

        _ = await this._vehicleRepository.Get(userId, organizationId, vehicleId)
            ?? throw new Exception($"Не найдена техника с vehicleId={vehicleId} и userId={userId}");

        await this.AddAsync(
            userId,
            organizationId,
            vehicleId,
            VehicleHistoryWriter.VehicleEventDocumentGenerated,
            title,
            description,
            "vehicle",
            vehicleId);
    }

    /// <summary> Добавляет событие в историю техники. </summary>
    public Task AddAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        string eventType,
        string title,
        string? description,
        string? relatedEntityType,
        Guid? relatedEntityId)
    {
        return this.SaveEventAsync(
            userId,
            organizationId,
            vehicleId,
            eventType,
            title,
            VehicleHistoryWriter.Truncate(description, 1000),
            relatedEntityType,
            relatedEntityId);
    }

    /// <summary> Добавляет событие истории по фотографиям техники. </summary>
    internal Task AddPhotoEventAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        string eventType,
        string title,
        string? description)
    {
        // Перенос сохраняет прежнее поведение истории фото при превышении ограничения БД.
        return this.SaveEventAsync(userId, organizationId, vehicleId, eventType, title, description, null, null);
    }

    /// <summary> Сохраняет событие истории техники. </summary>
    private async Task SaveEventAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        string eventType,
        string title,
        string? description,
        string? relatedEntityType,
        Guid? relatedEntityId)
    {
        var actorDisplayName = await this.ResolveActorDisplayNameAsync(userId);
        this._context.VehicleHistoryEvents.Add(new VehicleHistoryEvent
        {
            VehicleId = vehicleId,
            OrganizationId = organizationId,
            UserId = userId,
            ActorDisplayName = actorDisplayName,
            EventType = eventType,
            Title = title,
            Description = description,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = this._systemClock.UtcNow
        });

        await this._context.SaveChangesAsync();
    }

    /// <summary> Определяет отображаемое имя автора действия. </summary>
    private async Task<string> ResolveActorDisplayNameAsync(string userId)
    {
        return await HistoryActorDisplayNameQuery.ResolveAsync(this._context, userId);
    }

    /// <summary> Обрезает строку до максимальной длины. </summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    /// <summary> Проверяет право пользователя читать данные рабочего пространства. </summary>
    private async Task EnsureCanReadWorkspaceAsync(
        string userId,
        Guid? organizationId,
        string permissionCode = OrganizationPermissionCodes.VehiclesView)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        await this._organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId.Value, permissionCode);
    }
}
