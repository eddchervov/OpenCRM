namespace OpenCRM.WebApp.Infrastructure.Security;

/// <summary> Имена политик ограничения частоты запросов. </summary>
public static class RateLimitPolicyNames
{
    /// <summary> Политика ограничения частоты для аутентификации. </summary>
    public const string Authentication = "authentication";

    /// <summary> Политика ограничения частоты для отправки писем. </summary>
    public const string EmailDelivery = "email-delivery";

    /// <summary> Политика ограничения частоты для формы обратной связи. </summary>
    public const string ContactForm = "contact-form";

    /// <summary> Политика ограничения частоты для платежей. </summary>
    public const string Payments = "payments";

    /// <summary> Один одновременно формируемый архив на пользователя. </summary>
    public const string OrganizationDataExport = "organization-data-export";

    /// <summary> Ограничивает отправку сообщений и число одновременных загрузок вложений. </summary>
    public const string ConversationSend = "conversation-send";

    public const string ConversationFiles = "conversation-files";

    public const string ConversationEvents = "conversation-events";
}
