namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Значение фильтра с понятной пользователю подписью. </summary>
public sealed record CommunicationOption(string Code, string Name)
{
    public override string ToString() => this.Name;
}
