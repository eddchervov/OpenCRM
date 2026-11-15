using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.ViewModels;

public partial class EmailCampaignsViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    public event Action? ComposeRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<EmailCampaignRow> _campaigns = [];

    public bool IsEmpty => !this.IsBusy && this.Campaigns.Count == 0 && string.IsNullOrWhiteSpace(this.ErrorMessage);

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var result = await this.SessionClient.GetEmailCampaignsAsync(this.Page, 20, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Campaigns = result.Items.Select(item => new EmailCampaignRow(item)).ToArray();
        this.TotalCount = result.TotalCount;
        this.Page = Math.Max(1, result.Page);
        this.PageSize = result.PageSize > 0 ? result.PageSize : 20;
    }

    protected override void ApplyFilters() { }
    protected override void ResetFilters() { }

    protected override void ClearData()
    {
        this.Campaigns = [];
        this.TotalCount = 0;
    }

    [RelayCommand]
    private void Compose() => this.ComposeRequested?.Invoke();

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.PauseCommand.NotifyCanExecuteChanged();
        this.ResumeCommand.NotifyCanExecuteChanged();
        this.CancelCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanChangeCampaign() => this.CanLoad();

    [RelayCommand(CanExecute = nameof(CanChangeCampaign))]
    private Task PauseAsync(EmailCampaignRow campaign) => this.ChangeStatusAsync(campaign, this.SessionClient.PauseEmailCampaignAsync);

    [RelayCommand(CanExecute = nameof(CanChangeCampaign))]
    private Task ResumeAsync(EmailCampaignRow campaign) => this.ChangeStatusAsync(campaign, this.SessionClient.ResumeEmailCampaignAsync);

    [RelayCommand(CanExecute = nameof(CanChangeCampaign))]
    private Task CancelAsync(EmailCampaignRow campaign) => this.ChangeStatusAsync(campaign, this.SessionClient.CancelEmailCampaignAsync);

    private Task ChangeStatusAsync(EmailCampaignRow campaign, Func<Guid, CancellationToken, Task> operation) => this.RunAsync(async cancellationToken =>
    {
        await operation(campaign.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await this.LoadPageAsync(cancellationToken);
    });
}
