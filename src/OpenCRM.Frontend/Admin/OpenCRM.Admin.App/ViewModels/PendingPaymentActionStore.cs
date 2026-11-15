using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Локально сохраняет единственную принятую к отправке команду на случай неизвестного ответа сервера. </summary>
public sealed class PendingPaymentActionStore
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private readonly string _directory;

    public PendingPaymentActionStore(string? directory = null)
    {
        this._directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencrm.example.invalid", "AdminPendingActions");
    }

    public async Task<PendingPaymentActionDraft?> LoadAsync(string scope, CancellationToken cancellationToken = default)
    {
        var path = this.GetPath(scope);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PendingPaymentActionDraft>(stream, s_json, cancellationToken);
    }

    public async Task SaveAsync(string scope, PendingPaymentActionDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var path = this.GetPath(scope);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, draft, s_json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, path, true);
    }

    public Task DeleteAsync(string scope)
    {
        var path = this.GetPath(scope);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var scopeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));

        return Path.Combine(this._directory, scopeHash + ".json");
    }
}

/// <summary> Повторяемая команда и идентификатор платежа, к которому она относится. </summary>
public sealed record PendingPaymentActionDraft(int PaymentId, AdminBillingPaymentServiceActionRequest Request);
