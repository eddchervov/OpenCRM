using System.Net;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> История отправленных писем с безопасным текстовым просмотром. </summary>
public partial class EmailHistoryViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    private string _appliedSearch = string.Empty;
    private string _appliedStatus = string.Empty;
    private int _appliedPageSize = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<EmailHistoryRow> _rows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    private EmailHistoryRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHtml))]
    private EmailMessageDetails? _detail;

    [ObservableProperty]
    private EmailHistoryRow? _detailSummary;

    [ObservableProperty]
    private IReadOnlyList<EmailHistoryEventRow> _events = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewHtml))]
    private string _bodyText = string.Empty;

    [ObservableProperty]
    private bool _isDetailOpen;

    [ObservableProperty]
    private CommunicationOption _selectedStatus = new("", "Все статусы");

    public IReadOnlyList<int> PageSizes { get; } = [25, 50, 100];

    public string PreviewHtml => EmailPreviewDocument.Create(this.Detail?.HtmlBody, this.BodyText);

    public bool IsEmpty => !this.IsBusy && this.Rows.Count == 0 && string.IsNullOrWhiteSpace(this.ErrorMessage);

    public IReadOnlyList<CommunicationOption> StatusOptions { get; } =
    [
        new("", "Все статусы"), new("pending", "Ожидает отправки"), new("queued", "В очереди"),
        new("retrying", "Повторная отправка"), new("sent", "Отправлено"),
        new("delivered", "Доставлено"), new("opened", "Открыто"), new("clicked", "Переход по ссылке"),
        new("failed", "Ошибка"), new("hard_bounced", "Адрес недоступен"), new("soft_bounced", "Временная ошибка"),
        new("spam", "Спам"), new("spam_block", "Блокировка спама"), new("unsubscribed", "Отписка"),
        new("simulated", "Отправка отключена")
    ];

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var result = await this.SessionClient.GetEmailMessagesAsync(
            this._appliedSearch, this._appliedStatus, this.Page, this._appliedPageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Rows = result.Items.Select(item => new EmailHistoryRow(item)).ToArray();
        this.TotalCount = result.TotalCount;
        this.Page = Math.Max(1, result.Page);
        this.PageSize = result.PageSize > 0 ? result.PageSize : this._appliedPageSize;
        this.SelectedRow = null;
    }

    protected override void ApplyFilters()
    {
        this._appliedSearch = this.Search.Trim();
        this._appliedStatus = this.SelectedStatus.Code;
        this._appliedPageSize = this.PageSizes.Contains(this.PageSize) ? this.PageSize : 50;
    }

    protected override void ResetFilters()
    {
        this.SelectedStatus = this.StatusOptions[0];
        this.PageSize = 50;
    }

    protected override void ClearData()
    {
        this.Rows = [];
        this.SelectedRow = null;
        this.Detail = null;
        this.DetailSummary = null;
        this.Events = [];
        this.BodyText = string.Empty;
        this.IsDetailOpen = false;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.OpenDetailCommand.NotifyCanExecuteChanged();
        this.CloseDetailCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanOpenDetail() => this.CanLoad() && this.SelectedRow != null;

    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private Task OpenDetailAsync() => this.RunAsync(async cancellationToken =>
    {
        var id = this.SelectedRow!.Id;
        var detail = await this.SessionClient.GetEmailMessageAsync(id, cancellationToken);
        var events = await this.SessionClient.GetEmailMessageEventsAsync(id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Detail = detail;
        this.DetailSummary = new EmailHistoryRow(detail);
        this.Events = events.Items.Select(item => new EmailHistoryEventRow(item)).ToArray();
        this.BodyText = EmailHistoryViewModel.GetBodyText(detail);
        this.IsDetailOpen = true;
    });

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void CloseDetail() => this.IsDetailOpen = false;

    /// <summary> Показывает HTML как текст, не выполняя код и не загружая внешние ресурсы. </summary>
    public static string GetBodyText(EmailMessageDetails message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return message.TextBody;
        }

        try
        {
            var text = Regex.Replace(message.HtmlBody, @"<(script|style)\b[^>]*>.*?</\1\s*>", string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
            text = Regex.Replace(text, @"<br\s*/?>|</(?:p|div|tr|h[1-6])\s*>", "\n",
                RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            text = Regex.Replace(text, "<[^>]*>", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1));

            return WebUtility.HtmlDecode(text).Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            // Сложный HTML остаётся буквальным текстом без выполнения кода.
            return message.HtmlBody;
        }
    }
}
