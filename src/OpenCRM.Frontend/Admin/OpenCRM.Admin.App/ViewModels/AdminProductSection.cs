using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Дополнительная ссылка продукта в административной панели. </summary>
public sealed record AdminProductSection(string Title, string Url, string Permission)
{
    /// <summary> Создаёт ссылку из проверенных настроек. </summary>
    public static bool TryCreate(string? title, string? url, string? permission, out AdminProductSection? section)
    {
        section = null;
        if (string.IsNullOrWhiteSpace(title) || !IsLocalPath(url) || string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        section = new AdminProductSection(title.Trim(), url!, permission.Trim());
        return true;
    }

    /// <summary> Проверяет доступ к ссылке для текущего профиля. </summary>
    public bool IsVisibleTo(AdminProfile? profile) => profile is not null &&
        (profile.IsSystem || profile.Permissions.Contains(this.Permission));

    /// <summary> Создаёт локальный адрес с сохранением PathBase сервера. </summary>
    public Uri BuildUri(Uri serverAddress)
    {
        if (!AdminProductSection.IsLocalPath(this.Url))
        {
            throw new InvalidOperationException("Ссылка раздела должна быть локальным путём.");
        }

        var baseAddress = new Uri(serverAddress.AbsoluteUri.TrimEnd('/') + "/");

        return new Uri(baseAddress, this.Url.TrimStart('/'));
    }

    /// <summary> Проверяет, что значение является локальным абсолютным путём без query и fragment. </summary>
    public static bool IsLocalPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/' || url.StartsWith("//", StringComparison.Ordinal) ||
            url.Contains('\\') || url.Contains('?') || url.Contains('#') || url.Any(char.IsControl))
        {
            return false;
        }

        var decoded = Uri.UnescapeDataString(url);

        return !decoded.Contains(':') && !decoded.Contains('\\') && !decoded.Any(char.IsControl)
            && !decoded.StartsWith("//", StringComparison.Ordinal)
            && !decoded.Split('/').Any(segment => segment is "." or "..");
    }
}
