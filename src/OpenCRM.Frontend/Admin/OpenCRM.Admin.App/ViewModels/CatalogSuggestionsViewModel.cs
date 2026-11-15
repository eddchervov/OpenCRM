using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public partial class CatalogSuggestionsViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    public event Action<int>? PendingCountUpdated;

    public event Action? SuggestionReviewed;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(this.ErrorMessage))
        {
            this.OnPropertyChanged(nameof(this.HasError));
        }
    }

    [ObservableProperty] private IReadOnlyList<AdminCatalogSuggestion> _suggestions = [];
    [ObservableProperty] private AdminCatalogSuggestion? _selectedSuggestion;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _comment = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isConfirmationOpen;
    [ObservableProperty] private bool _approveOnConfirm;
    [ObservableProperty] private bool _isDeleteConfirmationOpen;
    private bool _canManage = sessionClient.CurrentProfile?.IsSystem == true
        || sessionClient.CurrentProfile?.Permissions.Contains("Admin.Content.Manage") == true;

    public IReadOnlyList<string> Tabs { get; } = ["На модерации", "Одобренные", "Отклонённые"];

    public string ListTitle => this.SelectedTabIndex switch
    {
        1 => "Одобренные предложения",
        2 => "Отклонённые предложения",
        _ => "Очередь модерации"
    };

    public bool CanManage => this._canManage;
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);
    public bool HasMessage => !string.IsNullOrWhiteSpace(this.Message);
    public bool HasSelection => this.SelectedSuggestion != null;
    public bool IsEmpty => !this.IsBusy && this.Suggestions.Count == 0 && !this.HasError;
    public bool CanReview => this.CanManage && this.SelectedSuggestion is { Status: 1 };
    public bool CanDelete => this.CanManage && this.CanLoad() && this.HasSelection;
    public string ConfirmationText => this.ApproveOnConfirm
        ? "Одобрить предложение и добавить марку и модель в каталог?"
        : "Отклонить выбранное предложение?";

    public void RefreshAccess()
    {
        var profile = this.SessionClient.CurrentProfile;
        this._canManage = profile?.IsSystem == true || profile?.Permissions.Contains("Admin.Content.Manage") == true;
        this.OnPropertyChanged(nameof(CanManage));
        this.NotifyCommands();
    }

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var tabIndex = this.SelectedTabIndex;
        var status = tabIndex + 1;
        var suggestions = await this.SessionClient.GetCatalogSuggestionsAsync(status, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (status == 1)
        {
            this.PendingCountUpdated?.Invoke(suggestions.Count(item => item.Status == 1));
        }
        if (tabIndex != this.SelectedTabIndex)
        {
            return;
        }

        this.Suggestions = suggestions;
        if (this.SelectedSuggestion != null)
        {
            this.SelectedSuggestion = suggestions.FirstOrDefault(item => item.Id == this.SelectedSuggestion.Id);
        }
    }

    protected override void ApplyFilters() { }
    protected override void ResetFilters() => this.SelectedTabIndex = 0;

    protected override void ClearData()
    {
        this.SelectedTabIndex = 0;
        this.Suggestions = [];
        this.SelectedSuggestion = null;
        this.Comment = string.Empty;
        this.Message = string.Empty;
        this.IsConfirmationOpen = false;
        this.IsDeleteConfirmationOpen = false;
    }

    public async Task LoadSelectedTabAsync()
    {
        var tabIndex = this.SelectedTabIndex;
        if (this.IsBusy)
        {
            await this.CurrentOperation;
        }

        if (this.CanLoad() && tabIndex == this.SelectedTabIndex)
        {
            await this.RunAsync(this.LoadPageAsync);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void SelectSuggestion(AdminCatalogSuggestion suggestion)
    {
        this.SelectedSuggestion = suggestion;
        this.Comment = suggestion.AdminComment ?? string.Empty;
    }

    [RelayCommand]
    private void CloseDetails()
    {
        this.SelectedSuggestion = null;
        this.IsConfirmationOpen = false;
        this.IsDeleteConfirmationOpen = false;
    }

    [RelayCommand]
    private void RequestApprove() => this.RequestReview(true);

    [RelayCommand]
    private void RequestReject() => this.RequestReview(false);

    private void RequestReview(bool approve)
    {
        if (!this.CanReview)
        {
            return;
        }

        this.ApproveOnConfirm = approve;
        this.IsConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelConfirmation() => this.IsConfirmationOpen = false;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void RequestDelete() => this.IsDeleteConfirmationOpen = true;

    [RelayCommand]
    private void CancelDelete() => this.IsDeleteConfirmationOpen = false;

    private bool CanConfirmDelete() => this.CanDelete && this.IsDeleteConfirmationOpen;

    [RelayCommand(CanExecute = nameof(CanConfirmDelete))]
    private Task ConfirmDeleteAsync() => this.RunAsync(async token =>
    {
        var selected = this.SelectedSuggestion!;
        this.IsDeleteConfirmationOpen = false;
        var result = await this.SessionClient.DeleteCatalogSuggestionAsync(selected.Id, token);
        token.ThrowIfCancellationRequested();
        if (selected.Status == 1)
        {
            this.SuggestionReviewed?.Invoke();
        }

        this.Suggestions = this.Suggestions.Where(item => item.Id != selected.Id).ToArray();
        this.SelectedSuggestion = null;
        this.Message = result.Message;
        await this.LoadPageAsync(token);
    });

    private bool CanConfirmReview() => this.CanLoad() && this.IsConfirmationOpen && this.CanReview;

    [RelayCommand(CanExecute = nameof(CanConfirmReview))]
    private Task ConfirmReviewAsync() => this.RunAsync(async token =>
    {
        var selectedId = this.SelectedSuggestion!.Id;
        this.IsConfirmationOpen = false;
        var result = await this.SessionClient.ReviewCatalogSuggestionAsync(
            selectedId, this.ApproveOnConfirm, this.Comment.Trim(), token);
        token.ThrowIfCancellationRequested();
        this.SuggestionReviewed?.Invoke();
        this.Message = result.Message;
        await this.LoadPageAsync(token);
    });

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.SelectSuggestionCommand.NotifyCanExecuteChanged();
        this.ConfirmReviewCommand.NotifyCanExecuteChanged();
        this.RequestDeleteCommand.NotifyCanExecuteChanged();
        this.ConfirmDeleteCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(HasError));
        this.OnPropertyChanged(nameof(HasMessage));
        this.OnPropertyChanged(nameof(HasSelection));
        this.OnPropertyChanged(nameof(IsEmpty));
        this.OnPropertyChanged(nameof(CanReview));
        this.OnPropertyChanged(nameof(CanDelete));
        this.OnPropertyChanged(nameof(ConfirmationText));
    }

    partial void OnSelectedSuggestionChanged(AdminCatalogSuggestion? value)
    {
        this.OnPropertyChanged(nameof(HasSelection));
        this.OnPropertyChanged(nameof(IsEmpty));
        this.OnPropertyChanged(nameof(CanReview));
        this.NotifyCommands();
    }
    partial void OnIsConfirmationOpenChanged(bool value) => this.ConfirmReviewCommand.NotifyCanExecuteChanged();
    partial void OnApproveOnConfirmChanged(bool value) => this.OnPropertyChanged(nameof(ConfirmationText));
    partial void OnIsDeleteConfirmationOpenChanged(bool value) => this.ConfirmDeleteCommand.NotifyCanExecuteChanged();
    partial void OnSuggestionsChanged(IReadOnlyList<AdminCatalogSuggestion> value) => this.OnPropertyChanged(nameof(IsEmpty));
    partial void OnSelectedTabIndexChanged(int value)
    {
        this.Suggestions = [];
        this.SelectedSuggestion = null;
        this.Comment = string.Empty;
        this.Message = string.Empty;
        this.IsConfirmationOpen = false;
        this.IsDeleteConfirmationOpen = false;
        this.OnPropertyChanged(nameof(ListTitle));
    }
}
