using OpenCRM.Modules.Organizations.Contracts;
using System.Data;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Organizations.Export;

internal sealed class OrganizationDataArchiveBuilder(
    OpenCRMContext context,
    IFileStorageService storage,
    OrganizationDataExportOptions options)
{
    private const long DiskCheckInterval = 16L * 1024 * 1024;

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() }
    };

    internal async Task<Stream> CreateAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        this.ValidateOptions();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var operationToken = linkedCancellation.Token;
        var directory = options.GetSpoolDirectory();
        Directory.CreateDirectory(directory);
        this.EnsureFreeDiskSpace(directory);

        using var archiveFile = TemporaryExportFile.Create(directory, ".zip");
        using var inventoryFile = TemporaryExportFile.Create(directory, ".files.jsonl");
        using var missingFile = TemporaryExportFile.Create(directory, ".missing.jsonl");

        FileStream? output = null;
        try
        {
            output = new FileStream(archiveFile.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            var limits = new ExportLimits(options, directory);

            await using (var readerContext = this.CreateReaderContext())
            {
                using var archive = new ZipArchive(new BoundedWriteStream(output, limits), ZipArchiveMode.Create, leaveOpen: true);
                var datasets = new Dictionary<string, int>();

                await using (var transaction = await readerContext.Database.BeginTransactionAsync(
                    IsolationLevel.RepeatableRead, operationToken))
                {
                    await this.WriteDataAsync(readerContext, archive, organizationId, datasets, limits,
                        inventoryFile.Path, operationToken);
                    await transaction.CommitAsync(operationToken);
                }

                await this.WriteFilesAsync(archive, inventoryFile.Path, missingFile.Path, limits, operationToken);
                await this.WriteManifestAsync(archive, organizationId, datasets, missingFile.Path, limits, operationToken);
            }

            await output.FlushAsync(operationToken);
            output.Position = 0;
            archiveFile.PreserveForCaller();
            var result = output;
            output = null;

            return result;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new OrganizationDataExportResourceLimitException(
                $"Выгрузка данных превысила лимит времени {options.TimeoutSeconds} секунд.");
        }
        finally
        {
            if (output != null)
            {
                await output.DisposeAsync();
            }
        }
    }

    private OpenCRMContext CreateReaderContext()
    {
        var contextOptions = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(context.Database.GetDbConnection())
            .Options;

        return new OpenCRMContext(contextOptions);
    }

    private async Task WriteDataAsync(
        OpenCRMContext readerContext,
        ZipArchive archive,
        Guid organizationId,
        Dictionary<string, int> datasets,
        ExportLimits limits,
        string inventoryPath,
        CancellationToken cancellationToken)
    {
        var vehicles = readerContext.Vehicles.Where(value => value.OrganizationId == organizationId);
        var orders = readerContext.Maintenance.Where(value => vehicles.Any(vehicle => vehicle.Id == value.VehicleId));
        var clients = readerContext.OrganizationClients.Where(value => value.OrganizationId == organizationId);
        var operations = readerContext.MaintenanceWorkOperations.Where(value => value.OrganizationId == organizationId
            && orders.Any(order => order.Id == value.MaintenanceId));

        async Task AddAsync<T>(string name, IQueryable<T> query) where T : class
        {
            datasets[name] = await this.WriteRowsAsync(
                readerContext, archive, name, query, organizationId, limits, cancellationToken);
        }

        await AddAsync("organization", readerContext.Organizations.Where(value => value.Id == organizationId));
        await AddAsync("members", readerContext.OrganizationMembers.Where(value => value.OrganizationId == organizationId));
        await AddAsync("employees", readerContext.OrganizationEmployees.Where(value => value.OrganizationId == organizationId));
        await AddAsync("shifts", readerContext.OrganizationEmployeeShifts.Where(value => value.OrganizationId == organizationId));
        await AddAsync("working-days", readerContext.OrganizationWorkingDays.Where(value => value.OrganizationId == organizationId));
        await AddAsync("working-day-breaks", readerContext.OrganizationWorkingDayBreaks.Where(value => value.OrganizationId == organizationId));
        await AddAsync("role-permissions", readerContext.OrganizationRolePermissionOverrides.Where(value => value.OrganizationId == organizationId));
        await AddAsync("clients", clients);
        await AddAsync("client-tags", readerContext.OrganizationClientTags.Where(value => value.OrganizationId == organizationId));
        await AddAsync("client-tag-assignments", readerContext.OrganizationClientTagAssignments.Where(value =>
            clients.Any(client => client.Id == value.OrganizationClientId)
            && value.OrganizationClientTag.OrganizationId == organizationId));
        await AddAsync("client-history", readerContext.OrganizationClientHistoryEvents.Where(value => value.OrganizationId == organizationId));
        await AddAsync("vehicles", vehicles);
        await AddAsync("brands", readerContext.Brands.Where(value => vehicles.Any(vehicle => vehicle.BrandId == value.Id)));
        await AddAsync("models", readerContext.Models.Where(value => vehicles.Any(vehicle => vehicle.ModelId == value.Id)));
        await AddAsync("vehicle-history", readerContext.VehicleHistoryEvents.Where(value => value.OrganizationId == organizationId
            && vehicles.Any(vehicle => vehicle.Id == value.VehicleId)));
        await AddAsync("vehicle-counters", readerContext.VehicleCounterEntries.Where(value => vehicles.Any(vehicle => vehicle.Id == value.VehicleId)));
        await AddAsync("vehicle-defects", readerContext.VehicleDefects.Where(value => value.OrganizationId == organizationId
            && vehicles.Any(vehicle => vehicle.Id == value.VehicleId)));
        await AddAsync("orders", orders);
        await AddAsync("order-lines", readerContext.MaintenanceDetails.Where(value => orders.Any(order => order.Id == value.MaintenanceId)));
        await AddAsync("order-history", readerContext.MaintenanceHistoryEvents.Where(value => orders.Any(order => order.Id == value.MaintenanceId)));
        await AddAsync("payments", readerContext.MaintenancePayments.Where(value => orders.Any(order => order.Id == value.MaintenanceId)));
        await AddAsync("order-defects", readerContext.MaintenanceDefects.Where(value => orders.Any(order => order.Id == value.MaintenanceId)));
        await AddAsync("work-operations", operations);
        await AddAsync("work-events", readerContext.MaintenanceWorkEvents.Where(value => value.OrganizationId == organizationId
            && orders.Any(order => order.Id == value.MaintenanceId)));
        await AddAsync("work-intervals", readerContext.MaintenanceWorkIntervals.Where(value => operations.Any(operation => operation.Id == value.OperationId)));
        await AddAsync("work-reports", readerContext.MaintenanceWorkReports.Where(value => value.OrganizationId == organizationId
            && orders.Any(order => order.Id == value.MaintenanceId)));
        await AddAsync("catalog", readerContext.OrganizationScenarioCatalogItems.Where(value => value.OrganizationId == organizationId));
        await AddAsync("service-bundle-items", readerContext.OrganizationServiceBundleItems.Where(value => value.OrganizationId == organizationId));
        await AddAsync("material-stocks", readerContext.OrganizationMaterialStocks.Where(value => value.OrganizationId == organizationId));
        await AddAsync("material-movements", readerContext.OrganizationMaterialMovements.Where(value => value.OrganizationId == organizationId));
        await AddAsync("scheduler-tasks", readerContext.OrganizationSchedulerTasks.Where(value => value.OrganizationId == organizationId));
        await AddAsync("scheduler-states", readerContext.OrganizationSchedulerTaskStates.Where(value => value.OrganizationId == organizationId));
        await AddAsync("audit", readerContext.OrganizationAuditEvents.Where(value => value.OrganizationId == organizationId));
        await AddAsync("photo-albums", readerContext.VehiclePhotoAlbums.Where(value => vehicles.Any(vehicle => vehicle.Id == value.VehicleId)));

        var vehiclePhotos = readerContext.VehiclePhotos.Where(value => !value.IsDeleted
            && vehicles.Any(vehicle => vehicle.Id == value.VehicleId));
        var orderPhotos = readerContext.MaintenancePhotos.Where(value => !value.IsDeleted
            && orders.Any(order => order.Id == value.MaintenanceId)
            && vehicles.Any(vehicle => vehicle.Id == value.VehicleId));
        var documents = readerContext.MaintenanceDocuments.Where(value => !value.IsDeleted && value.OrganizationId == organizationId
            && orders.Any(order => order.Id == value.MaintenanceId));

        await AddAsync("vehicle-photos", vehiclePhotos);
        await AddAsync("order-photos", orderPhotos);
        await AddAsync("documents", documents);

        await using var inventory = new FileStream(inventoryPath, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var inventoryWriter = new StreamWriter(inventory, new UTF8Encoding(false), 81920, leaveOpen: true);
        await this.WriteFileInventoryAsync(vehiclePhotos.Select(value => new ExportFile(
            value.Id, value.Path, value.OriginalFileName, value.ContentType)), "vehicle-photos", inventoryWriter,
            limits, cancellationToken);
        await this.WriteFileInventoryAsync(orderPhotos.Select(value => new ExportFile(
            value.Id, value.Path, value.OriginalFileName, value.ContentType)), "order-photos", inventoryWriter,
            limits, cancellationToken);
        await this.WriteFileInventoryAsync(documents.Select(value => new ExportFile(
            value.Id, value.StoragePath, value.FileName, value.ContentType)), "documents", inventoryWriter,
            limits, cancellationToken);
        await inventoryWriter.FlushAsync(cancellationToken);
    }

    private async Task<int> WriteRowsAsync<T>(
        OpenCRMContext readerContext,
        ZipArchive archive,
        string name,
        IQueryable<T> query,
        Guid organizationId,
        ExportLimits limits,
        CancellationToken cancellationToken) where T : class
    {
        var fields = OrganizationExportFields.ByType[typeof(T)]
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(fieldName =>
            {
                var property = typeof(T).GetProperty(fieldName)!;
                return (Name: JsonNamingPolicy.CamelCase.ConvertName(property.Name), Property: property);
            })
            .ToArray();

        await this.EnsureTextFieldsWithinLimitsAsync(readerContext, query, fields, organizationId, cancellationToken);

        await using var entry = archive.CreateEntry($"data/{name}.json", CompressionLevel.Fastest).Open();
        await using var stream = new SourceLimitWriteStream(entry, limits);
        await using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartArray();
        var count = 0;
        var rows = query.IgnoreAutoIncludes().AsNoTracking().AsAsyncEnumerable();

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            limits.AddRow();
            writer.WriteStartObject();
            foreach (var field in fields)
            {
                writer.WritePropertyName(field.Name);
                JsonSerializer.Serialize(writer, field.Property.GetValue(row), field.Property.PropertyType, s_jsonOptions);
            }

            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken);
            count++;
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);

        return count;
    }

    private async Task EnsureTextFieldsWithinLimitsAsync<T>(
        OpenCRMContext readerContext,
        IQueryable<T> query,
        IReadOnlyList<(string Name, System.Reflection.PropertyInfo Property)> fields,
        Guid organizationId,
        CancellationToken cancellationToken) where T : class
    {
        var entityType = readerContext.Model.FindEntityType(typeof(T));
        var longTextFields = fields
            .Where(field => field.Property.PropertyType == typeof(string)
                && !(typeof(T) == typeof(MaintenanceHistoryEvent)
                    && field.Property.Name == nameof(MaintenanceHistoryEvent.PayloadJson)))
            .Where(field =>
            {
                var maxLength = entityType?.FindProperty(field.Property.Name)?.GetMaxLength();

                return maxLength == null || maxLength > options.MaxTextCharacters;
            })
            .ToArray();

        if (longTextFields.Length > 0)
        {
            var parameter = Expression.Parameter(typeof(T), "row");
            Expression? predicate = null;
            foreach (var field in longTextFields)
            {
                var property = Expression.Property(parameter, field.Property);
                var notNull = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));
                var tooLong = Expression.GreaterThan(
                    Expression.Property(property, nameof(string.Length)),
                    Expression.Constant(options.MaxTextCharacters));
                var fieldPredicate = Expression.AndAlso(notNull, tooLong);
                predicate = predicate == null ? fieldPredicate : Expression.OrElse(predicate, fieldPredicate);
            }

            var oversized = Expression.Lambda<Func<T, bool>>(predicate!, parameter);
            if (await query.IgnoreAutoIncludes().AsNoTracking().AnyAsync(oversized, cancellationToken))
            {
                throw new OrganizationDataExportResourceLimitException(
                    $"Выгрузка содержит текстовое поле длиннее {options.MaxTextCharacters} символов.");
            }
        }

        if (typeof(T) == typeof(MaintenanceHistoryEvent)
            && await this.HasOversizedMaintenanceHistoryPayloadAsync(readerContext, organizationId, cancellationToken))
        {
            throw new OrganizationDataExportResourceLimitException(
                $"История обслуживания содержит JSON-поле больше {options.MaxJsonFieldBytes} байт.");
        }
    }

    private async Task<bool> HasOversizedMaintenanceHistoryPayloadAsync(
        OpenCRMContext readerContext,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        return await readerContext.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1
                FROM "MaintenanceHistoryEvents" AS history
                INNER JOIN "Maintenance" AS maintenance ON maintenance."Id" = history."MaintenanceId"
                INNER JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                WHERE vehicle."OrganizationId" = {organizationId}
                    AND octet_length(history."PayloadJson"::text) > {options.MaxJsonFieldBytes}
            ) AS "Value"
            """).SingleAsync(cancellationToken);
    }

    private async Task WriteFileInventoryAsync(
        IQueryable<ExportFile> query,
        string section,
        StreamWriter writer,
        ExportLimits limits,
        CancellationToken cancellationToken)
    {
        await foreach (var file in query.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            limits.AddFile();
            var record = new InventoryRecord(section, file.Id,
                GetFileExtension(file), file.Path);
            await writer.WriteLineAsync(JsonSerializer.Serialize(record, s_jsonOptions).AsMemory(), cancellationToken);
        }
    }

    private async Task WriteFilesAsync(
        ZipArchive archive,
        string inventoryPath,
        string missingPath,
        ExportLimits limits,
        CancellationToken cancellationToken)
    {
        await using var inventory = new FileStream(inventoryPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var inventoryReader = new StreamReader(inventory, Encoding.UTF8);
        await using var missing = new FileStream(missingPath, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var missingWriter = new StreamWriter(missing, new UTF8Encoding(false), 81920, leaveOpen: true);

        while (await inventoryReader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = JsonSerializer.Deserialize<InventoryRecord>(line, s_jsonOptions)
                ?? throw new InvalidDataException("Не удалось прочитать временный список файлов выгрузки.");
            var entryName = $"files/{file.Section}/{file.Id:N}{file.Extension}";
            Stream source;
            try
            {
                source = await storage.GetFileAsync(file.Path, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                await missingWriter.WriteLineAsync(JsonSerializer.Serialize(entryName, s_jsonOptions).AsMemory(), cancellationToken);
                continue;
            }

            await using var ownedSource = source;
            await using var boundedSource = new SourceLimitReadStream(ownedSource, limits);
            await using var destination = archive.CreateEntry(entryName, CompressionLevel.NoCompression).Open();
            await boundedSource.CopyToAsync(destination, 81920, cancellationToken);
        }

        await missingWriter.FlushAsync(cancellationToken);
    }

    private async Task WriteManifestAsync(
        ZipArchive archive,
        Guid organizationId,
        Dictionary<string, int> datasets,
        string missingPath,
        ExportLimits limits,
        CancellationToken cancellationToken)
    {
        await using var entry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest).Open();
        await using var stream = new SourceLimitWriteStream(entry, limits);
        await using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("organizationId", organizationId);
        writer.WriteString("createdAtUtc", DateTime.UtcNow);
        writer.WritePropertyName("datasets");
        writer.WriteStartObject();
        foreach (var dataset in datasets)
        {
            writer.WriteNumber(dataset.Key, dataset.Value);
        }

        writer.WriteEndObject();
        writer.WritePropertyName("missingFiles");
        writer.WriteStartArray();
        using (var missing = new FileStream(missingPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var missingReader = new StreamReader(missing, Encoding.UTF8))
        {
            while (await missingReader.ReadLineAsync(cancellationToken) is { } line)
            {
                writer.WriteStringValue(JsonSerializer.Deserialize<string>(line, s_jsonOptions));
            }
        }

        writer.WriteEndArray();
        writer.WritePropertyName("notes");
        writer.WriteStartArray();
        writer.WriteStringValue("Данные находятся в data/*.json; поля соответствуют сохранённым значениям, связи задаются идентификаторами.");
        writer.WriteStringValue("Файлы находятся в files/<раздел>/<id без дефисов>.<расширение>; исходное имя и тип указаны в соответствующей записи JSON.");
        writer.WriteStringValue("Расширение определяется по MIME-типу, затем по исходному имени или пути; для неизвестного формата используется .bin.");
        writer.WriteStringValue("Даты указаны в UTC; перечисления представлены именами значений приложения.");
        writer.WriteStringValue("Включены архивные и мягко удалённые записи; удалённые фотографии и документы не включены.");
        writer.WriteStringValue("Не включены учётные записи, пароли, секреты ботов, уведомления, приглашения, пакеты импорта и расчёты подписки CRM.");
        writer.WriteStringValue("MissingFiles перечисляет вложения, которые отсутствовали в хранилище на момент выгрузки.");
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private void ValidateOptions()
    {
        if (options.MaxArchiveBytes <= 0 || options.MaxSourceBytes <= 0 || options.MaxFiles <= 0
            || options.MaxRows <= 0 || options.MaxTextCharacters <= 0 || options.MaxJsonFieldBytes <= 0
            || options.TimeoutSeconds <= 0 || options.MinimumFreeDiskBytes < 0)
        {
            throw new InvalidOperationException("Параметры ограничений выгрузки должны быть положительными.");
        }
    }

    private void EnsureFreeDiskSpace(string directory)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        if (root == null)
        {
            throw new OrganizationDataExportResourceLimitException("Не удалось определить диск для временных файлов выгрузки.");
        }

        var freeBytes = new DriveInfo(root).AvailableFreeSpace;
        if (freeBytes < options.MinimumFreeDiskBytes)
        {
            throw new OrganizationDataExportResourceLimitException(
                $"Для выгрузки требуется не менее {options.MinimumFreeDiskBytes} свободных байт на диске.");
        }
    }

    private static string GetFileExtension(ExportFile file)
    {
        var extension = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            "image/avif" => ".avif",
            "image/svg+xml" => ".svg",
            "application/pdf" => ".pdf",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            _ => null
        };

        if (extension != null)
        {
            return extension;
        }

        foreach (var name in new[] { file.FileName, file.Path })
        {
            extension = Path.GetExtension(name);
            if (extension is not { Length: > 1 and <= 11 }
                || extension.AsSpan(1).ContainsAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"))
            {
                continue;
            }

            return extension.ToLowerInvariant();
        }

        return ".bin";
    }

    private sealed record ExportFile(Guid Id, string Path, string? FileName, string? ContentType);

    private sealed record InventoryRecord(string Section, Guid Id, string Extension, string Path);

    private sealed class ExportLimits(OrganizationDataExportOptions options, string directory)
    {
        private long _sourceBytes;
        private long _archiveBytes;
        private long _nextDiskCheck = DiskCheckInterval;
        private long _rows;
        private int _files;

        internal void AddSourceBytes(int count)
        {
            _sourceBytes = checked(_sourceBytes + count);
            if (_sourceBytes > options.MaxSourceBytes)
            {
                throw new OrganizationDataExportResourceLimitException(
                    $"Данные для выгрузки превысили лимит {options.MaxSourceBytes} байт.");
            }
        }

        internal void AddArchiveBytes(int count)
        {
            _archiveBytes = checked(_archiveBytes + count);
            if (_archiveBytes > options.MaxArchiveBytes)
            {
                throw new OrganizationDataExportResourceLimitException(
                    $"ZIP-архив превысил лимит {options.MaxArchiveBytes} байт.");
            }

            if (_archiveBytes >= _nextDiskCheck)
            {
                this.EnsureFreeDiskSpace();
                _nextDiskCheck = _archiveBytes + DiskCheckInterval;
            }
        }

        internal void AddRow()
        {
            if (++_rows > options.MaxRows)
            {
                throw new OrganizationDataExportResourceLimitException(
                    $"Выгрузка превысила лимит {options.MaxRows} записей.");
            }
        }

        internal void AddFile()
        {
            if (++_files > options.MaxFiles)
            {
                throw new OrganizationDataExportResourceLimitException(
                    $"Выгрузка превысила лимит {options.MaxFiles} файлов.");
            }
        }

        private void EnsureFreeDiskSpace()
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (root == null || new DriveInfo(root).AvailableFreeSpace < options.MinimumFreeDiskBytes)
            {
                throw new OrganizationDataExportResourceLimitException(
                    $"Во время выгрузки осталось меньше {options.MinimumFreeDiskBytes} свободных байт на диске.");
            }
        }
    }

    private sealed class TemporaryExportFile : IDisposable
    {
        private TemporaryExportFile(string path)
        {
            this.Path = path;
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.None);
        }

        internal string Path { get; }

        private bool Preserve { get; set; }

        internal static TemporaryExportFile Create(string directory, string extension)
        {
            return new TemporaryExportFile(System.IO.Path.Combine(directory,
                $"opencrm-export-{Guid.NewGuid():N}{extension}"));
        }

        internal void PreserveForCaller()
        {
            this.Preserve = true;
        }

        public void Dispose()
        {
            if (this.Preserve)
            {
                return;
            }

            try
            {
                File.Delete(this.Path);
            }
            catch (FileNotFoundException)
            {
            }
        }
    }

    private sealed class BoundedWriteStream(Stream inner, ExportLimits limits) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            limits.AddArchiveBytes(count);
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            limits.AddArchiveBytes(buffer.Length);
            inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            limits.AddArchiveBytes(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            limits.AddArchiveBytes(count);
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SourceLimitWriteStream(Stream inner, ExportLimits limits) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            limits.AddSourceBytes(count);
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            limits.AddSourceBytes(buffer.Length);
            inner.Write(buffer);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            limits.AddSourceBytes(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            limits.AddSourceBytes(count);
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SourceLimitReadStream(Stream inner, ExportLimits limits) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            limits.AddSourceBytes(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            limits.AddSourceBytes(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            limits.AddSourceBytes(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer, offset, count, cancellationToken);
            limits.AddSourceBytes(read);
            return read;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
