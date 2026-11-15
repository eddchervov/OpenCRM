namespace OpenCRM.Modules.Communications.Contracts;

/// <summary> Сохраняет идентификатор бота, проверенный транспортом через Telegram getMe. </summary>
public interface IConversationBotIdentityRegistry
{
    Task RegisterVerifiedBotAsync(
        Guid organizationId,
        long botId,
        string username,
        CancellationToken cancellationToken = default);
}
