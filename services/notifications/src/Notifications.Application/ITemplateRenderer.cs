using System.Text.Json;

using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Подставляет данные уведомления в текст шаблона.
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>Проверяет синтаксис переменных без отправки сообщения.</summary>
    void Validate(string templateText);

    /// <summary>Заменяет переменные шаблона и при необходимости HTML-кодирует значения.</summary>
    string Render(string templateText, IReadOnlyDictionary<string, string> data, bool htmlEncodeValues);
}
