using System.Globalization;
using OpenCRM.Domain;

namespace OpenCRM.Modules.Maintenance.Reminders;

/// <summary> Формирует текст напоминания из разрешённых полей записи. </summary>
internal static class VisitReminderContent
{
    private static readonly string[] s_placeholders = ["{organization}", "{client}", "{date}", "{time}", "{vehicle}"];

    internal static void Validate(string template)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 1000)
        {
            throw new ArgumentException("Укажите текст напоминания длиной до 1000 символов.");
        }

        var remaining = template;
        foreach (var placeholder in VisitReminderContent.s_placeholders)
        {
            remaining = remaining.Replace(placeholder, string.Empty, StringComparison.Ordinal);
        }

        if (remaining.Contains('{') || remaining.Contains('}'))
        {
            throw new ArgumentException("Доступные поля: {organization}, {client}, {date}, {time}, {vehicle}.");
        }

        var maximumLength = remaining.Length
            + (template.Length - template.Replace("{organization}", string.Empty).Length) / "{organization}".Length * 160
            + (template.Length - template.Replace("{client}", string.Empty).Length) / "{client}".Length * 250
            + (template.Length - template.Replace("{date}", string.Empty).Length) / "{date}".Length * 10
            + (template.Length - template.Replace("{time}", string.Empty).Length) / "{time}".Length * 5
            + (template.Length - template.Replace("{vehicle}", string.Empty).Length) / "{vehicle}".Length * 250;
        if (maximumLength > 4000)
        {
            throw new ArgumentException("Сократите текст или число подстановок: итоговое сообщение должно помещаться в 4000 символов.");
        }
    }

    internal static string Build(MaintenanceEntity maintenance, Organization organization, string? clientName)
    {
        var template = organization.VisitReminderTemplate ?? MaintenanceVisitReminderRules.DefaultTemplate;
        var values = new Dictionary<string, string>
        {
            ["{organization}"] = organization.Name,
            ["{client}"] = clientName ?? "Клиент",
            ["{date}"] = maintenance.PlannedDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—",
            ["{time}"] = maintenance.PlannedStartTime?.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "—",
            ["{vehicle}"] = maintenance.Vehicle.RegistrationPlate ?? "Автомобиль"
        };

        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\w+\}", match => values.GetValueOrDefault(match.Value, match.Value));
    }
}
