namespace OpenCRM.Domain;

public static class FeatureCatalogCodes
{
    public static class Scopes
    {
        public const string Personal = "personal";
        public const string Organization = "organization";
        public const string Both = "both";

        public static readonly IReadOnlyCollection<string> All =
        [
            Personal,
            Organization,
            Both
        ];

        public static string Normalize(string? code)
        {
            var normalized = code?.Trim().ToLowerInvariant();
            return All.Contains(normalized ?? string.Empty) ? normalized! : Both;
        }

        public static string GetName(string? code) => Normalize(code) switch
        {
            Personal => "Личный",
            Organization => "Рабочее пространство",
            _ => "Личный и workspace"
        };
    }

    public static class Audiences
    {
        public const string User = "user";
        public const string Admin = "admin";

        public static readonly IReadOnlyCollection<string> All =
        [
            User,
            Admin
        ];

        public static string Normalize(string? code)
        {
            var normalized = code?.Trim().ToLowerInvariant();
            return All.Contains(normalized ?? string.Empty) ? normalized! : User;
        }

        public static string GetName(string? code) => Normalize(code) switch
        {
            Admin => "Админская",
            _ => "Обычная"
        };
    }

    public static class Types
    {
        public const string Module = "module";
        public const string Limit = "limit";

        public static readonly IReadOnlyCollection<string> All =
        [
            Module,
            Limit
        ];

        public static string Normalize(string? code)
        {
            var normalized = code?.Trim().ToLowerInvariant();
            return All.Contains(normalized ?? string.Empty) ? normalized! : Module;
        }

        public static string GetName(string? code) => Normalize(code) switch
        {
            Limit => "Лимит",
            _ => "Функция"
        };
    }
}
