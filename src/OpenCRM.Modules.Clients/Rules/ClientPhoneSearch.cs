using OpenCRM.Domain;

namespace OpenCRM.Modules.Clients.Rules;

/// <summary> Общий поиск телефона без изменения сохранённого отображения. </summary>
internal static class ClientPhoneSearch
{
    internal static string GetDigits(string? value) => value == null
        ? string.Empty
        : new string(value.Where(character => character is >= '0' and <= '9').ToArray());

    internal static string Normalize(string? value)
    {
        var digits = ClientPhoneSearch.GetDigits(value);
        if (value?.TrimStart().StartsWith("+", StringComparison.Ordinal) == true)
        {
            return digits;
        }

        if (digits.Length == 11 && digits[0] == '8')
        {
            return "7" + digits[1..];
        }

        if (digits.Length == 10 && digits[0] == '9')
        {
            return "7" + digits;
        }

        return digits;
    }

    internal static string[] GetSearchPatterns(string? value)
    {
        var digits = ClientPhoneSearch.GetDigits(value);
        if (digits.Length < 2)
        {
            return [];
        }

        var normalized = ClientPhoneSearch.Normalize(value);
        if (value?.TrimStart().StartsWith("+", StringComparison.Ordinal) == true)
        {
            return [$"{normalized}%"];
        }

        if (digits.Length < 11 && digits[0] == '8')
        {
            normalized = "7" + digits[1..];
        }

        return [$"%{normalized}%"];
    }

    internal static IQueryable<CanonicalPhone> GetCanonicalPhones(IQueryable<OrganizationClient> clients) => clients
        .Where(client => client.Phone != null)
        .Select(client => new
        {
            client.Id,
            HasPlus = client.Phone!.TrimStart().StartsWith("+"),
            Digits = client.Phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "")
                .Replace(".", "").Replace("+", "").Replace("\u00a0", "").Replace("\t", "").Replace("\r", "").Replace("\n", "")
        })
        .Select(phone => new CanonicalPhone
        {
            Id = phone.Id,
            Number = !phone.HasPlus && phone.Digits.Length == 11 && phone.Digits.StartsWith("8")
                ? "7" + phone.Digits.Substring(1)
                : !phone.HasPlus && phone.Digits.Length == 10 && phone.Digits.StartsWith("9")
                    ? "7" + phone.Digits
                    : phone.Digits
        });

    internal sealed class CanonicalPhone
    {
        public Guid Id { get; init; }

        public string Number { get; init; } = string.Empty;
    }
}
