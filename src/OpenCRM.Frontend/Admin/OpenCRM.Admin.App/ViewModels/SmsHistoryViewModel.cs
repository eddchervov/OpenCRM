using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.ViewModels;

public partial class SmsHistoryViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredRows))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<SmsHistoryRow> _rows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredRows))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _search = string.Empty;

    public IReadOnlyList<SmsHistoryRow> FilteredRows
    {
        get
        {
            var search = this.Search.Trim();
            return string.IsNullOrEmpty(search)
                ? this.Rows
                : this.Rows.Where(row => row.Phone.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.Text.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.Response.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }

    public bool IsEmpty => !this.IsBusy && this.FilteredRows.Count == 0 && string.IsNullOrWhiteSpace(this.ErrorMessage);

    public override void RefreshLocalizedValues()
    {
        base.RefreshLocalizedValues();
        foreach (var row in this.Rows)
        {
            row.RefreshLocalizedValues();
        }
    }

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var result = await this.SessionClient.GetSmsHistoryAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Rows = result.Histories.Select(item => new SmsHistoryRow(item)).ToArray();
        this.TotalCount = result.TotalCount;
        this.Page = 1;
        this.PageSize = Math.Max(1, result.TotalCount);
    }

    protected override void ApplyFilters() { }
    protected override void ResetFilters() => this.Search = string.Empty;

    protected override void ClearData()
    {
        this.Rows = [];
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.OnPropertyChanged(nameof(IsEmpty));
    }
}
