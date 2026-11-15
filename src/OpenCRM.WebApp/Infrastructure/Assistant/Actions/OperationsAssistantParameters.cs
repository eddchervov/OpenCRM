using System.Globalization;
using System.Text.Json;
using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Actions;

internal sealed class OperationsAssistantParameters(AiPilotCommand command)
{
    public bool Has(string name) => command.Parameters.ContainsKey(name);

    public string? OptionalText(string name)
    {
        if (!command.Parameters.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public string RequiredText(string name, int maximum)
    {
        var value = OptionalText(name);
        if (value is null || value.Length > maximum)
        {
            throw new InvalidOperationException($"Укажите корректное значение поля «{name}».");
        }

        return value;
    }

    public string RequiredTextEither(string firstName, string secondName, int maximum)
    {
        var value = OptionalText(firstName) ?? OptionalText(secondName);
        if (value is null || value.Length > maximum)
        {
            throw new InvalidOperationException($"Укажите корректное значение поля «{firstName}».");
        }

        return value;
    }

    public decimal? OptionalDecimal(string name)
    {
        if (!command.Parameters.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number >= 0)
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number) && number >= 0)
        {
            return number;
        }

        throw new InvalidOperationException($"Поле «{name}» должно быть неотрицательным числом.");
    }

    public int? OptionalInt(string name)
    {
        if (!command.Parameters.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0)
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0)
        {
            return number;
        }

        throw new InvalidOperationException($"Поле «{name}» должно быть целым неотрицательным числом.");
    }

    public int RequiredInt(string name)
    {
        return OptionalInt(name) ?? throw new InvalidOperationException($"Укажите поле «{name}».");
    }

    public int RequiredSignedInt(string name)
    {
        if (!command.Parameters.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Укажите поле «{name}».");
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new InvalidOperationException($"Поле «{name}» должно быть целым числом.");
    }

    public decimal RequiredDecimal(string name)
    {
        return OptionalDecimal(name) ?? throw new InvalidOperationException($"Укажите поле «{name}».");
    }

    public decimal RequiredPositiveDecimal(string name)
    {
        var value = RequiredDecimal(name);
        if (value <= 0 || value > 1_000_000)
        {
            throw new InvalidOperationException($"Поле «{name}» должно быть больше нуля.");
        }

        return value;
    }

    public decimal RequiredPositiveDecimalEither(string firstName, string secondName)
    {
        var value = OptionalDecimal(firstName) ?? OptionalDecimal(secondName);
        if (value is null || value <= 0 || value > 1_000_000)
        {
            throw new InvalidOperationException($"Укажите поле «{firstName}» больше нуля.");
        }

        return value.Value;
    }

    public decimal RequiredNonnegativeDecimalEither(string firstName, string secondName)
    {
        var value = OptionalDecimal(firstName) ?? OptionalDecimal(secondName);
        if (value is null || value > 10_000_000)
        {
            throw new InvalidOperationException($"Укажите поле «{firstName}» неотрицательным числом.");
        }

        return value.Value;
    }

    public bool? OptionalBool(string name)
    {
        if (!command.Parameters.TryGetValue(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result))
        {
            return result;
        }

        throw new InvalidOperationException($"Поле «{name}» должно быть логическим значением.");
    }

    public DateOnly? OptionalDate(string name)
    {
        var value = OptionalText(name);
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidOperationException($"Поле «{name}» должно содержать дату в формате ГГГГ-ММ-ДД.");
    }

    public DateOnly? OptionalDateFromCommand()
    {
        if (string.IsNullOrWhiteSpace(command.Date))
        {
            return null;
        }

        if (DateOnly.TryParseExact(command.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidOperationException("Проверьте дату команды.");
    }

    public DateTime? OptionalDateTime(string name)
    {
        var value = OptionalText(name);
        if (value is null)
        {
            return null;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
        {
            return dateTime;
        }

        throw new InvalidOperationException($"Поле «{name}» должно содержать дату и время.");
    }

    public string RequiredTimeEither(string name, string? fallback)
    {
        var value = OptionalText(name) ?? fallback;
        if (value is null || !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new InvalidOperationException($"Укажите время «{name}» в формате ЧЧ:ММ.");
        }

        return value;
    }
}
