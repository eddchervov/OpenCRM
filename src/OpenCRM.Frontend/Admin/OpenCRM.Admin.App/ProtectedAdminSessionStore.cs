using OpenCRM.Admin.App.Localization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App;

/// <summary> Хранит токен обновления с защитой DPAPI текущего пользователя Windows. </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedAdminSessionStore : IAdminSessionStore
{
    private readonly string _filePath;

    private readonly byte[] _entropy;

    /// <summary> Создаёт отдельное хранилище для адреса сервера. </summary>
    public ProtectedAdminSessionStore(Uri serverAddress, string? directory = null)
    {
        this._entropy = SHA256.HashData(Encoding.UTF8.GetBytes(serverAddress.AbsoluteUri));

        var sessionDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenCRM", "Admin", "Sessions");

        this._filePath = Path.Combine(sessionDirectory, Convert.ToHexString(this._entropy) + ".bin");
    }

    /// <inheritdoc />
    public SavedAdminSession? Load()
    {
        byte[]? plaintext = null;
        try
        {
            var encrypted = File.ReadAllBytes(this._filePath);
            plaintext = ProtectedData.Unprotect(encrypted, this._entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<SavedAdminSession>(plaintext);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            this.Delete();

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ApiException(AdminLocalization.Instance["core.session.readFailed"], innerException: exception);
        }
        finally
        {
            if (plaintext != null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    /// <inheritdoc />
    public void Save(SavedAdminSession session)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(session);
        var temporaryPath = this._filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, this._entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(this._filePath)!);
            try
            {
                File.WriteAllBytes(temporaryPath, encrypted);
                File.Move(temporaryPath, this._filePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new ApiException(AdminLocalization.Instance["core.session.saveFailed"], innerException: exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <inheritdoc />
    public void Delete()
    {
        try
        {
            File.Delete(this._filePath);
        }
        catch (DirectoryNotFoundException)
        {
            // Отсутствие папки означает, что сохранённой сессии уже нет.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ApiException(AdminLocalization.Instance["core.session.deleteFailed"], innerException: exception);
        }
    }
}
