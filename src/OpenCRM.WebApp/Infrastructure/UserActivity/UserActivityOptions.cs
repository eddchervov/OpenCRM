namespace OpenCRM.WebApp.Infrastructure.UserActivity;

/// <summary> Независимые настройки сбора и хранения активности. </summary>
public sealed class UserActivityOptions
{
    public bool Enabled { get; set; }

    public int RetentionDays { get; set; } = 90;
}
