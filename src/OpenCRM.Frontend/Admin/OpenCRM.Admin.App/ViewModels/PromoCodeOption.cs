namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Вариант типа бонуса промокода. </summary>
public sealed class PromoCodeOption(string code, string name)
{
    public string Code { get; } = code;

    public string Name { get; } = name;
}
