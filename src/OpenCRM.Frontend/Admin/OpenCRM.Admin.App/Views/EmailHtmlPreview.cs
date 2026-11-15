using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace OpenCRM.Admin.App.Views;

/// <summary> Показывает сохранённое письмо с исходной HTML-вёрсткой. </summary>
public sealed class EmailHtmlPreview : NativeWebView
{
    public static readonly StyledProperty<string> DocumentHtmlProperty =
        AvaloniaProperty.Register<EmailHtmlPreview, string>(nameof(DocumentHtml), string.Empty);

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<EmailHtmlPreview, bool>(nameof(HasError));

    private bool _canRenderDocument;
    private bool _isExternalNavigationBlocked;
    private string _documentAddress = string.Empty;

    public EmailHtmlPreview()
    {
        this.AdapterPreference = [WebViewAdapterType.WebView2];
        this.NavigationStarted += (_, args) =>
        {
            args.Cancel = args.Request?.AbsoluteUri != "about:blank" && args.Request?.AbsoluteUri != this._documentAddress;
            this._isExternalNavigationBlocked = args.Cancel;
        };
        this.NewWindowRequested += (_, args) => args.Handled = true;
        this.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess && this._isExternalNavigationBlocked)
            {
                return;
            }

            if (args.Request?.AbsoluteUri is null or "about:blank" || args.Request.AbsoluteUri == this._documentAddress)
            {
                this.HasError = !args.IsSuccess || !this._canRenderDocument;
            }
        };
        this.EnvironmentRequested += (_, args) =>
        {
            args.EnableDevTools = false;
            if (args is WindowsWebView2EnvironmentRequestedEventArgs windows)
            {
                windows.IsInPrivateModeEnabled = true;
                windows.ProfileName = "OpenCRMAdminEmailPreview";
                windows.UserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenCRM", "Admin", "EmailPreview");
            }
        };
    }

    public string DocumentHtml
    {
        get => this.GetValue(EmailHtmlPreview.DocumentHtmlProperty);
        set => this.SetValue(EmailHtmlPreview.DocumentHtmlProperty, value);
    }

    public bool HasError
    {
        get => this.GetValue(EmailHtmlPreview.HasErrorProperty);
        private set => this.SetValue(EmailHtmlPreview.HasErrorProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        // Без нативного окна используется адаптер Avalonia для проверки интерфейса без браузера.
        this.AdapterPreference = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.HandleDescriptor == "HWND"
            ? [WebViewAdapterType.WebView2]
            : [WebViewAdapterType.Headless];
        this.UpdatePreview();
        base.OnAttachedToVisualTree(eventArgs);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != EmailHtmlPreview.DocumentHtmlProperty)
        {
            return;
        }

        this.UpdatePreview();
    }

    private void UpdatePreview()
    {
        var hasNativeWindow = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.HandleDescriptor == "HWND";
        // WebView2 ограничивает размер документа NavigateToString двумя мегабайтами.
        this._canRenderDocument = hasNativeWindow
            && WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebView2).IsInstalled
            && Encoding.UTF8.GetByteCount(this.DocumentHtml) < 2 * 1024 * 1024;
        this.HasError = !this._canRenderDocument;
        var document = this.HasError ? "<html><body></body></html>" : this.DocumentHtml;
        this._documentAddress = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(document));
        this.NavigateToString(document);
    }
}
