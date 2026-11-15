namespace OpenCRM.Domain;

/// <summary> Аудитории уведомлений на сайте. </summary>
public static class SiteNotificationAudiences
{
    /// <summary> Аудитория: все пользователи. </summary>
    public const int All = 1;

    /// <summary> Аудитория: администраторы. </summary>
    public const int Admins = 2;

    /// <summary> Аудитория: владельцы организаций. </summary>
    public const int OrganizationOwners = 4;

    /// <summary> Преобразует маску аудитории в список кодов. </summary>
    public static IReadOnlyCollection<string> ToCodes(int mask)
    {
        var codes = new List<string>();
        if ((mask & All) != 0)
        {
            codes.Add("all");
        }

        if ((mask & Admins) != 0)
        {
            codes.Add("admins");
        }

        if ((mask & OrganizationOwners) != 0)
        {
            codes.Add("organization-owners");
        }

        return codes;
    }
}
