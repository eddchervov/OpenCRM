using System.Net;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class CatalogSuggestionTabsTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DeleteRequiresConfirmationAndRemovesOnlySelectedSuggestion(int status)
    {
        using var handler = new SuggestionsHandler();
        using var client = await CatalogSuggestionTabsTests.CreateClientAsync(handler);
        var model = new CatalogSuggestionsViewModel(client);
        var window = new Window { Width = 900, Height = 700, Content = new CatalogSuggestionsView { DataContext = model } };
        window.Show();
        try
        {
            await model.ActivateAsync();
            model.SelectedTabIndex = status - 1;
            await model.LoadSelectedTabAsync();
            model.SelectSuggestionCommand.Execute(model.Suggestions.Single());
            var queueUpdates = 0;
            model.SuggestionReviewed += () => queueUpdates++;
            Dispatcher.UIThread.RunJobs();
            var delete = window.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, model.RequestDeleteCommand));
            Assert.True(delete.IsEffectivelyVisible);
            Assert.True(delete.IsEffectivelyEnabled);
            Assert.False(model.ConfirmDeleteCommand.CanExecute(null));
            delete.Command!.Execute(null);
            Assert.True(model.IsDeleteConfirmationOpen);
            Assert.Empty(handler.DeletedIds);
            model.CancelDeleteCommand.Execute(null);
            Assert.False(model.IsDeleteConfirmationOpen);
            Assert.Empty(handler.DeletedIds);
            delete.Command.Execute(null);
            await model.ConfirmDeleteCommand.ExecuteAsync(null);
            Assert.Empty(model.ErrorMessage);
            Assert.Equal(status, Assert.Single(handler.DeletedIds));
            Assert.Empty(model.Suggestions);
            Assert.Null(model.SelectedSuggestion);
            Assert.Equal(status == 1 ? 1 : 0, queueUpdates);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [Fact]
    public async Task FailedDeleteKeepsSuggestionAndViewOnlyAccessCannotDelete()
    {
        using var handler = new SuggestionsHandler { FailDelete = true };
        using var client = await CatalogSuggestionTabsTests.CreateClientAsync(handler);
        var model = new CatalogSuggestionsViewModel(client);
        await model.ActivateAsync();
        model.SelectSuggestionCommand.Execute(model.Suggestions.Single());
        model.RequestDeleteCommand.Execute(null);
        await model.ConfirmDeleteCommand.ExecuteAsync(null);
        Assert.NotEmpty(model.ErrorMessage);
        Assert.NotNull(model.SelectedSuggestion);
        Assert.Single(model.Suggestions);
        Assert.Empty(handler.DeletedIds);
        await model.DeactivateAsync(true);

        using var readOnlyHandler = new SuggestionsHandler { ReadOnly = true };
        using var readOnlyClient = await CatalogSuggestionTabsTests.CreateClientAsync(readOnlyHandler);
        var readOnlyModel = new CatalogSuggestionsViewModel(readOnlyClient);
        await readOnlyModel.ActivateAsync();
        readOnlyModel.SelectSuggestionCommand.Execute(readOnlyModel.Suggestions.Single());
        Assert.False(readOnlyModel.RequestDeleteCommand.CanExecute(null));
        Assert.False(readOnlyModel.ConfirmDeleteCommand.CanExecute(null));
        await readOnlyModel.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task TabsLoadOnlyTheirStatusAndReopeningStartsWithPending()
    {
        using var handler = new SuggestionsHandler();
        using var client = await CatalogSuggestionTabsTests.CreateClientAsync(handler);
        var model = new CatalogSuggestionsViewModel(client);
        var view = new CatalogSuggestionsView { DataContext = model };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        try
        {
            await model.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 1 }, handler.RequestedStatuses);
            Assert.Equal(0, model.SelectedTabIndex);
            Assert.Equal(1, Assert.Single(model.Suggestions).Status);
            Assert.Empty(view.GetVisualDescendants().OfType<ComboBox>());
            var tabs = view.GetVisualDescendants().OfType<TabStrip>().Single();
            Assert.Equal(3, tabs.ItemCount);
            tabs.SelectedIndex = 1;
            await CatalogSuggestionTabsTests.WaitForAsync(() => !model.IsBusy && model.Suggestions.Count == 1);
            Assert.Equal(new[] { 1, 2 }, handler.RequestedStatuses);
            Assert.Equal(2, Assert.Single(model.Suggestions).Status);
            tabs.SelectedIndex = 2;
            await CatalogSuggestionTabsTests.WaitForAsync(() => !model.IsBusy && model.Suggestions.Count == 1);
            Assert.Equal(new[] { 1, 2, 3 }, handler.RequestedStatuses);
            Assert.Equal(3, Assert.Single(model.Suggestions).Status);
            await model.DeactivateAsync();
            await model.ActivateAsync();
            Assert.Equal(0, model.SelectedTabIndex);
            Assert.Equal(1, Assert.Single(model.Suggestions).Status);
            Assert.Equal(new[] { 1, 2, 3, 1 }, handler.RequestedStatuses);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [Fact]
    public async Task FastTabSwitchDiscardsOldListAndLoadsLatestTab()
    {
        using var handler = new SuggestionsHandler { HoldApproved = true };
        using var client = await CatalogSuggestionTabsTests.CreateClientAsync(handler);
        var model = new CatalogSuggestionsViewModel(client);
        await model.ActivateAsync();
        model.SelectSuggestionCommand.Execute(model.Suggestions[0]);
        model.SelectedTabIndex = 1;
        Assert.Null(model.SelectedSuggestion);
        Assert.Empty(model.Suggestions);
        var approved = model.LoadSelectedTabAsync();
        await handler.ApprovedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        model.SelectedTabIndex = 2;
        var rejected = model.LoadSelectedTabAsync();
        handler.ReleaseApproved.TrySetResult();
        await Task.WhenAll(approved, rejected);
        Assert.Equal(2, model.SelectedTabIndex);
        Assert.Equal(3, Assert.Single(model.Suggestions).Status);
        Assert.Equal(new[] { 1, 2, 3 }, handler.RequestedStatuses);
        await model.DeactivateAsync(true);
    }

    private static async Task<AdminSessionClient> CreateClientAsync(SuggestionsHandler handler)
    {
        var client = new AdminSessionClient(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);

        return client;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(condition());
    }

    private sealed class SuggestionsHandler : HttpMessageHandler
    {
        public List<int> RequestedStatuses { get; } = [];
        public bool HoldApproved { get; init; }
        public bool FailDelete { get; init; }
        public bool ReadOnly { get; init; }
        public HashSet<int> DeletedIds { get; } = [];
        public TaskCompletionSource ApprovedStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseApproved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return SuggestionsHandler.Json(new
                {
                    accessToken = "access-token", refreshToken = "refresh",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1), user = new { id = "admin" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return SuggestionsHandler.Json(new AdminProfile { Id = "admin", IsSystem = !this.ReadOnly, Permissions = ["Admin.Access", "Admin.Content.View"] });
            }

            if (request.Method == HttpMethod.Delete)
            {
                if (this.FailDelete)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                this.DeletedIds.Add(int.Parse(path.Split('/').Last()));

                return SuggestionsHandler.Json(new { message = "Предложение удалено." });
            }

            Assert.Equal("/api/v1/admin/catalog-suggestions", path);
            var status = int.Parse(request.RequestUri.Query["?status=".Length..]);
            this.RequestedStatuses.Add(status);
            if (status == 2 && this.HoldApproved)
            {
                this.ApprovedStarted.TrySetResult();
                await this.ReleaseApproved.Task.WaitAsync(cancellationToken);
            }

            return SuggestionsHandler.Json(this.DeletedIds.Contains(status) ? [] : new[]
            {
                new AdminCatalogSuggestion { Id = status, Status = status, BrandName = "Toyota", ModelName = "Camry" }
            });
        }

        private static HttpResponseMessage Json<T>(T data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
    }
}
