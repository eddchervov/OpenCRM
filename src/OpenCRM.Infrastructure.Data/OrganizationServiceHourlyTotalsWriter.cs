using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data;

/// <summary> Поддерживает почасовые суммы услуг в транзакции сохранения исходных заказов. </summary>
internal static class OrganizationServiceHourlyTotalsWriter
{
    internal static async Task<Dictionary<ServiceKey, Contribution>> ReadAsync(
        OpenCRMContext context,
        Guid[] maintenanceIds,
        CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQuery<ContributionRow>($"""
            SELECT total."OrganizationId", normalized.utc_hour AS "UtcHour", total."CurrencyCode", detail."Name" COLLATE "C" AS "Name",
                COUNT(*) AS "Count", SUM(detail."Price") AS "Amount",
                MIN((total."FirstKey" || ':' || LPAD(detail."Position"::text, 10, '0')
                    || ':' || detail."DetailId"::text) COLLATE "C") AS "FirstKey"
            FROM "MaintenanceFinancialTotals" AS total
            CROSS JOIN LATERAL (
                SELECT FLOOR(EXTRACT(EPOCH FROM GREATEST(
                    LEAST(total."EffectiveDate", TIMESTAMPTZ '9999-12-31 23:59:59.999999Z'),
                    TIMESTAMPTZ '0001-01-01 00:00:00Z')) / 3600)::integer + 17259888 AS utc_hour
            ) AS normalized
            JOIN "MaintenanceDetails" AS detail ON detail."MaintenanceId" = total."MaintenanceId"
            WHERE total."MaintenanceId" = ANY ({maintenanceIds}) AND total."IsAccruable"
                AND detail."Price" > 0 AND detail."Name" IS NOT NULL
            GROUP BY total."OrganizationId", normalized.utc_hour, total."CurrencyCode", detail."Name" COLLATE "C"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.ToDictionary(row => new ServiceKey(row.OrganizationId, row.UtcHour, row.CurrencyCode, row.Name),
            row => new Contribution(row.Count, row.Amount, row.FirstKey));
    }

    internal static async Task ApplyAsync(
        OpenCRMContext context,
        Guid[] maintenanceIds,
        Dictionary<ServiceKey, Contribution> previous,
        CancellationToken cancellationToken)
    {
        var current = await OrganizationServiceHourlyTotalsWriter.ReadAsync(context, maintenanceIds, cancellationToken)
            .ConfigureAwait(false);
        var changes = previous.Keys.Concat(current.Keys).Distinct()
            .Where(key => previous.GetValueOrDefault(key) != current.GetValueOrDefault(key))
            .Select(key => new Change(key, OrganizationServiceHourlyTotalsWriter.Hash(key.Name),
                previous.GetValueOrDefault(key), current.GetValueOrDefault(key))).ToArray();
        if (changes.Length == 0)
        {
            return;
        }

        var names = new Dictionary<(Guid, int, string, string), string>();
        foreach (var change in changes)
        {
            var hashKey = (change.Key.OrganizationId, change.Key.UtcHour, change.Key.CurrencyCode, change.NameHash);
            if (names.TryGetValue(hashKey, out var name) && !StringComparer.Ordinal.Equals(name, change.Key.Name))
            {
                throw new InvalidOperationException("Обнаружена коллизия хеша названий услуг.");
            }

            names[hashKey] = change.Key.Name;
        }

        var lockIds = changes.Select(OrganizationServiceHourlyTotalsWriter.GetLockId).Distinct().Order().ToArray();
        // Барьер подзапроса сохраняет порядок захвата блокировок при вычислении функции.
        await context.Database.ExecuteSqlAsync($"""
            SELECT pg_advisory_xact_lock(ordered.value)
            FROM (SELECT value FROM UNNEST({lockIds}) AS locks(value) ORDER BY value OFFSET 0) AS ordered
            """, cancellationToken).ConfigureAwait(false);

        var organizationIds = changes.Select(change => change.Key.OrganizationId).ToArray();
        var hours = changes.Select(change => change.Key.UtcHour).ToArray();
        var currencies = changes.Select(change => change.Key.CurrencyCode).ToArray();
        var hashes = changes.Select(change => change.NameHash).ToArray();
        var totals = await context.Set<OrganizationServiceHourlyTotal>().FromSql($"""
            SELECT total.* FROM "OrganizationServiceHourlyTotals" AS total
            JOIN UNNEST({organizationIds}, {hours}, {currencies}, {hashes}) AS changed(organization_id, utc_hour, currency_code, name_hash)
                ON total."OrganizationId" = changed.organization_id AND total."UtcHour" = changed.utc_hour
                    AND total."CurrencyCode" = changed.currency_code AND total."NameHash" = changed.name_hash
            """).AsNoTracking().ToDictionaryAsync(row => (row.OrganizationId, row.UtcHour, row.CurrencyCode, row.NameHash), cancellationToken)
            .ConfigureAwait(false);
        var survivingOrganizations = await context.Organizations.AsNoTracking()
            .Where(organization => organizationIds.Contains(organization.Id)).Select(organization => organization.Id)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var survivingIds = survivingOrganizations.ToHashSet();
        var prepared = new List<PreparedChange>(changes.Length);
        foreach (var change in changes)
        {
            if (!survivingIds.Contains(change.Key.OrganizationId))
            {
                continue;
            }

            totals.TryGetValue((change.Key.OrganizationId, change.Key.UtcHour, change.Key.CurrencyCode, change.NameHash), out var total);
            if (total != null && !StringComparer.Ordinal.Equals(total.Name, change.Key.Name))
            {
                throw new InvalidOperationException("Обнаружена коллизия хеша названий услуг.");
            }

            prepared.Add(await OrganizationServiceHourlyTotalsWriter.PrepareChangeAsync(context, change, total, cancellationToken)
                .ConfigureAwait(false));
        }

        await OrganizationServiceHourlyTotalsWriter.WriteAsync(context, prepared, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PreparedChange> PrepareChangeAsync(
        OpenCRMContext context,
        Change change,
        OrganizationServiceHourlyTotal? total,
        CancellationToken cancellationToken)
    {
        var count = checked((total?.Count ?? 0) + (change.Current?.Count ?? 0) - (change.Previous?.Count ?? 0));
        var amount = (total?.Amount ?? 0) + (change.Current?.Amount ?? 0) - (change.Previous?.Amount ?? 0);
        if (count == 0 && amount == 0)
        {
            return new PreparedChange(change.Key, change.NameHash, 0, 0, string.Empty);
        }
        if (count <= 0 || amount <= 0)
        {
            throw new InvalidOperationException("Нарушена согласованность почасовых сумм услуг.");
        }

        var firstKey = total?.FirstKey;
        if (firstKey != null && StringComparer.Ordinal.Equals(firstKey, change.Previous?.FirstKey)
            && (change.Current == null || StringComparer.Ordinal.Compare(change.Current.FirstKey, firstKey) > 0))
        {
            firstKey = await OrganizationServiceHourlyTotalsWriter.ReadFirstKeyAsync(context, change.Key, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (change.Current != null && (firstKey == null
            || StringComparer.Ordinal.Compare(change.Current.FirstKey, firstKey) < 0))
        {
            firstKey = change.Current.FirstKey;
        }
        if (firstKey == null)
        {
            throw new InvalidOperationException("Не найдена исходная строка почасовой суммы услуг.");
        }

        return new PreparedChange(change.Key, change.NameHash, count, amount, firstKey);
    }

    private static async Task WriteAsync(
        OpenCRMContext context,
        List<PreparedChange> prepared,
        CancellationToken cancellationToken)
    {
        var deleted = prepared.Where(change => change.Count == 0).ToArray();
        if (deleted.Length > 0)
        {
            var deletedOrganizations = deleted.Select(change => change.Key.OrganizationId).ToArray();
            var deletedHours = deleted.Select(change => change.Key.UtcHour).ToArray();
            var deletedCurrencies = deleted.Select(change => change.Key.CurrencyCode).ToArray();
            var deletedHashes = deleted.Select(change => change.NameHash).ToArray();
            var deletedNames = deleted.Select(change => change.Key.Name).ToArray();
            await context.Database.ExecuteSqlAsync($"""
                DELETE FROM "OrganizationServiceHourlyTotals" AS total
                USING UNNEST({deletedOrganizations}, {deletedHours}, {deletedCurrencies}, {deletedHashes}, {deletedNames})
                    AS deleted(organization_id, utc_hour, currency_code, name_hash, name)
                WHERE total."OrganizationId" = deleted.organization_id AND total."UtcHour" = deleted.utc_hour
                    AND total."CurrencyCode" = deleted.currency_code AND total."NameHash" = deleted.name_hash
                    AND total."Name" = deleted.name COLLATE "C"
                """, cancellationToken).ConfigureAwait(false);
        }

        var upserts = prepared.Where(change => change.Count > 0).ToArray();
        if (upserts.Length == 0)
        {
            return;
        }

        var organizationIds = upserts.Select(change => change.Key.OrganizationId).ToArray();
        var hours = upserts.Select(change => change.Key.UtcHour).ToArray();
        var currencies = upserts.Select(change => change.Key.CurrencyCode).ToArray();
        var hashes = upserts.Select(change => change.NameHash).ToArray();
        var names = upserts.Select(change => change.Key.Name).ToArray();
        var counts = upserts.Select(change => change.Count).ToArray();
        var amounts = upserts.Select(change => change.Amount).ToArray();
        var firstKeys = upserts.Select(change => change.FirstKey).ToArray();
        var affected = await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "OrganizationServiceHourlyTotals" ("OrganizationId", "UtcHour", "CurrencyCode", "NameHash", "Name", "Count", "Amount", "FirstKey")
            SELECT organization_id, utc_hour, currency_code, name_hash, name, count, amount, first_key
            FROM UNNEST({organizationIds}, {hours}, {currencies}, {hashes}, {names}, {counts}, {amounts}, {firstKeys})
                AS changed(organization_id, utc_hour, currency_code, name_hash, name, count, amount, first_key)
            WHERE TRUE
            ON CONFLICT ("OrganizationId", "UtcHour", "CurrencyCode", "NameHash") DO UPDATE SET
                "Count" = EXCLUDED."Count", "Amount" = EXCLUDED."Amount", "FirstKey" = EXCLUDED."FirstKey"
            WHERE "OrganizationServiceHourlyTotals"."Name" = EXCLUDED."Name" COLLATE "C"
            """, cancellationToken).ConfigureAwait(false);
        if (affected != upserts.Length)
        {
            throw new InvalidOperationException("Обнаружена коллизия хеша названий услуг.");
        }
    }

    private static Task<string?> ReadFirstKeyAsync(OpenCRMContext context, ServiceKey key, CancellationToken cancellationToken)
    {
        var hourStart = new DateTime((long)key.UtcHour * TimeSpan.TicksPerHour, DateTimeKind.Utc);
        var hourEnd = key.UtcHour == 87649415 ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : hourStart.AddHours(1);

        // Уникальный ключ заказа задаёт порядок строк; OFFSET 0 сохраняет раннюю остановку на первом совпадении.
        return context.Database.SqlQuery<string?>($"""
            SELECT (
                SELECT details.first_key
                FROM (
                    SELECT "MaintenanceId", "FirstKey" FROM "MaintenanceFinancialTotals"
                    WHERE "OrganizationId" = {key.OrganizationId} AND "IsVisible" AND "IsAccruable"
                        AND "CurrencyCode" = {key.CurrencyCode} AND "EffectiveDate" >= {hourStart}
                        AND ("EffectiveDate" < {hourEnd} OR {key.UtcHour} = 87649415)
                    ORDER BY "FirstKey" COLLATE "C" OFFSET 0
                ) AS total
                CROSS JOIN LATERAL (
                    SELECT total."FirstKey" || ':' || LPAD(detail."Position"::text, 10, '0')
                        || ':' || detail."DetailId"::text AS first_key
                    FROM "MaintenanceDetails" AS detail
                    WHERE detail."MaintenanceId" = total."MaintenanceId" AND detail."Price" > 0
                        AND detail."Name" = {key.Name} COLLATE "C"
                    ORDER BY detail."Position", detail."DetailId" LIMIT 1
                ) AS details
                ORDER BY total."FirstKey" COLLATE "C" LIMIT 1
            ) AS "Value"
            """).SingleAsync(cancellationToken);
    }

    private static string Hash(string name) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)));

    private static long GetLockId(Change change)
    {
        var key = change.Key.OrganizationId.ToString("D") + ":"
            + change.Key.UtcHour.ToString(CultureInfo.InvariantCulture) + ":" + change.Key.CurrencyCode + ":" + change.NameHash;

        return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    internal sealed record ServiceKey(Guid OrganizationId, int UtcHour, string CurrencyCode, string Name);

    internal sealed record Contribution(long Count, decimal Amount, string FirstKey);

    private sealed record Change(ServiceKey Key, string NameHash, Contribution? Previous, Contribution? Current);

    private sealed record PreparedChange(ServiceKey Key, string NameHash, long Count, decimal Amount, string FirstKey);

    private sealed class ContributionRow
    {
        public Guid OrganizationId { get; set; }

        public int UtcHour { get; set; }

        public string CurrencyCode { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public long Count { get; set; }

        public decimal Amount { get; set; }

        public string FirstKey { get; set; } = string.Empty;
    }
}
