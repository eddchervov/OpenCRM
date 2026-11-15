using Helpers;
using Microsoft.Extensions.Configuration;

namespace OpenCRM.Modules.Billing.Features;

/// <summary> Квоты новых тарифов коммерческого режима конкретной установки. </summary>
internal sealed class CommercialPlanDefaults
{
    /// <summary> Лимит техники нового тарифа Free организации. </summary>
    public int FreeVehicleLimit { get; init; } = PlatformDefaults.VehicleLimit;

    /// <summary> Лимит клиентов нового тарифа Free организации. </summary>
    public int FreeClientLimit { get; init; } = PlatformDefaults.ClientLimit;

    /// <summary> Лимит участников нового тарифа Free организации. </summary>
    public int FreeMembersLimit { get; init; } = PlatformDefaults.WorkspaceMembersLimit;

    /// <summary> Лимит участников в новых коммерческих тарифах организации. </summary>
    public int WorkspaceMembersLimit { get; init; } = PlatformDefaults.WorkspaceMembersLimit;

    /// <summary> Персональный лимит техники, если коммерческий тариф не задал значение. </summary>
    public int PersonalVehicleLimit { get; init; } = PlatformDefaults.VehicleLimit;

    /// <summary> Лимит техники нового демо-тарифа организации. </summary>
    public int DemoVehicleLimit { get; init; } = PlatformDefaults.VehicleLimit;

    /// <summary> Создаёт значения из настроек владельца установки. </summary>
    public static CommercialPlanDefaults FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        const string section = "PlatformPolicy:Commercial:Defaults:";
        var defaults = new CommercialPlanDefaults
        {
            FreeVehicleLimit = configuration.GetValue($"{section}FreeVehicleLimit", PlatformDefaults.VehicleLimit),
            FreeClientLimit = configuration.GetValue($"{section}FreeClientLimit", PlatformDefaults.ClientLimit),
            FreeMembersLimit = configuration.GetValue($"{section}FreeMembersLimit", PlatformDefaults.WorkspaceMembersLimit),
            WorkspaceMembersLimit = configuration.GetValue($"{section}WorkspaceMembersLimit", PlatformDefaults.WorkspaceMembersLimit),
            PersonalVehicleLimit = configuration.GetValue($"{section}PersonalVehicleLimit", PlatformDefaults.VehicleLimit),
            DemoVehicleLimit = configuration.GetValue($"{section}DemoVehicleLimit", PlatformDefaults.VehicleLimit)
        };

        defaults.Validate();

        return defaults;
    }

    /// <summary> Отклоняет отрицательные значения квот. </summary>
    public void Validate()
    {
        CommercialPlanDefaults.ValidateLimit(nameof(this.FreeVehicleLimit), this.FreeVehicleLimit);
        CommercialPlanDefaults.ValidateLimit(nameof(this.FreeClientLimit), this.FreeClientLimit);
        CommercialPlanDefaults.ValidateLimit(nameof(this.FreeMembersLimit), this.FreeMembersLimit);
        CommercialPlanDefaults.ValidateLimit(nameof(this.WorkspaceMembersLimit), this.WorkspaceMembersLimit);
        CommercialPlanDefaults.ValidateLimit(nameof(this.PersonalVehicleLimit), this.PersonalVehicleLimit);
        CommercialPlanDefaults.ValidateLimit(nameof(this.DemoVehicleLimit), this.DemoVehicleLimit);
    }

    private static void ValidateLimit(string name, int value)
    {
        if (value < 0)
        {
            throw new InvalidOperationException($"PlatformPolicy:Commercial:Defaults:{name} не может быть отрицательным.");
        }
    }
}
