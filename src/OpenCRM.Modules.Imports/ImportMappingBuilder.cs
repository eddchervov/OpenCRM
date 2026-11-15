using System.Globalization;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

/// <summary>Преобразует исходные столбцы в редактируемые записи с сохранением происхождения строк.</summary>
public static class ImportMappingBuilder
{
    private static readonly HashSet<string> s_supportedDateFormats =
    [
        "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "MM/dd/yyyy", "dd/MM/yyyy", "yyyyMMdd",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ",
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy H:mm", "dd.MM.yyyy HH:mm",
        "dd/MM/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss"
    ];

    private static readonly Dictionary<string, string[]> s_aliases = new()
    {
        ["clientId"] = ["clientid", "customerid", "идклиента", "кодклиента"],
        ["clientName"] = ["clientname", "customername", "клиент", "фио", "имяклиента", "имя", "name"],
        ["clientPhone"] = ["clientphone", "phone", "телефон", "мобильный", "телефонклиента"],
        ["clientComment"] = ["comment", "notes", "комментарий", "примечание"],
        ["vehicleName"] = ["vehiclename", "carname", "автомобиль", "названиеавтомобиля", "техника"],
        ["brand"] = ["brand", "марка", "бренд"],
        ["model"] = ["model", "модель"],
        ["vin"] = ["vin", "вин"],
        ["plate"] = ["plate", "registrationplate", "госномер", "регзнак", "номер"],
        ["year"] = ["year", "год", "годвыпуска"],
        ["vehicleClientId"] = ["ownerid", "vehicleclientid", "идвладельца", "кодвладельца"],
        ["vehicleClientPhone"] = ["ownerphone", "vehicleclientphone", "телефонвладельца"],
        ["vehicleId"] = ["vehicleid", "carid", "кодавтомобиля", "идтехники"],
        ["partyClientId"] = ["buyerclientid", "payerclientid", "идпокупателя", "идплательщика"],
        ["partyName"] = ["buyername", "payername", "покупатель", "плательщик"],
        ["vehicleExternalId"] = ["vehicleid", "carid", "кодавтомобиля", "идтехники"],
        ["orderId"] = ["orderid", "externalorderid", "идзаказа", "кодзаказа"],
        ["orderExternalId"] = ["orderid", "externalorderid", "идзаказа", "кодзаказа"],
        ["orderStatus"] = ["orderstatus", "статусзаказа", "status"],
        ["orderComment"] = ["ordercomment", "comment", "notes", "комментарий", "примечание"],
        ["occurredAt"] = ["occurredat", "датаобслуживания", "датаоплаты", "датаивремясобытия", "date"],
        ["orderType"] = ["ordertype", "типобслуживания"],
        ["detailName"] = ["servicename", "workname", "названиеработы", "названиеуслуги", "услуга", "работа"],
        ["detailDescription"] = ["description", "описаниестроки", "описаниеработы"],
        ["quantity"] = ["quantity", "количество"],
        ["unitPrice"] = ["unitprice", "priceperunit", "ценазаединицу", "ценазаед", "ценаединицы"],
        ["amount"] = ["amount", "paymentamount", "суммаоплаты", "суммаплатежа", "ordertotal", "totalamount", "итогозаказа"],
        ["discountAmount"] = ["discountamount", "скидка", "суммаскидки"],
        ["paymentMethod"] = ["paymentmethod", "способоплаты", "методоплаты"],
        ["cancelledAt"] = ["cancelledat", "датаотмены"],
        ["actorName"] = ["actorname", "исполнитель", "авторсобытия"],
        ["eventType"] = ["eventtype", "типсобытия"],
        ["historyComment"] = ["historycomment", "описаниесобытия", "comment", "notes", "комментарий", "примечание"]
    };

    /// <summary>Предлагает соответствия только для однозначно распознанных заголовков.</summary>
    public static List<ImportTableMapping> Suggest(IReadOnlyList<ImportSourceTable> tables)
    {
        var mappings = new List<ImportTableMapping>();
        foreach (var table in tables)
        {
            var normalizedColumns = table.Columns.Select(Normalize).ToArray();
            var kind = SuggestKind(normalizedColumns);
            var fields = new Dictionary<string, int[]>();
            foreach (var field in FieldsFor(kind))
            {
                var index = FindUniqueColumn(normalizedColumns, GetAliases(kind, field));
                if (index >= 0)
                {
                    fields[field] = [index];
                }
            }

            if (kind == "client" && !fields.ContainsKey("clientId"))
            {
                var idIndex = FindUniqueColumn(normalizedColumns, ["id"]);
                if (idIndex >= 0)
                {
                    fields["clientId"] = [idIndex];
                }
            }

            mappings.Add(new ImportTableMapping { TableId = table.Id, Kind = kind, Fields = fields });
        }

        return mappings;
    }

    /// <summary>Создаёт подготовленные строки из серверной копии исходных таблиц.</summary>
    public static List<ImportCandidate> Build(IReadOnlyList<ImportSourceTable> tables, List<ImportTableMapping> mappings)
    {
        if (mappings == null || mappings.Any(mapping => mapping == null) || mappings.Count != tables.Count ||
            mappings.Select(mapping => mapping.TableId).Distinct().Count() != mappings.Count)
        {
            throw new InvalidDataException("Укажите соответствие для каждой исходной таблицы.");
        }

        var result = new List<ImportCandidate>();
        foreach (var mapping in mappings)
        {
            var table = tables.SingleOrDefault(item => item.Id == mapping.TableId)
                ?? throw new InvalidDataException("Исходная таблица не найдена.");
            if (mapping.Fields == null || mapping.Transformations == null || !AllowedKinds.Contains(mapping.Kind) ||
                mapping.Fields.Any(field => !FieldsFor(mapping.Kind).Contains(field.Key) || field.Value == null ||
                    field.Value.Length > table.Columns.Count || field.Value.Distinct().Count() != field.Value.Length ||
                    field.Value.Any(index => index < 0 || index >= table.Columns.Count)) ||
                mapping.Transformations.Any(transformation => !mapping.Fields.ContainsKey(transformation.Key) ||
                    !IsValidTransformation(transformation.Value)))
            {
                throw new InvalidDataException("Проверьте тип таблицы и соответствия столбцов.");
            }

            if (mapping.Kind == "ignore")
            {
                continue;
            }

            var knownCombinedClients = new HashSet<(string? ExternalId, string Name, string? Phone, string? Comment)>();
            foreach (var source in table.Rows)
            {
                string? Read(string key)
                {
                    var values = mapping.Fields.GetValueOrDefault(key, [])
                        .Select(index => source.Values.ElementAtOrDefault(index)?.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value));
                    var value = string.Join(" ", values);
                    if (value.Length > 10000)
                    {
                        throw new InvalidDataException("Объединённое значение ячейки превышает 10000 символов. Выберите меньше столбцов.");
                    }

                    if (value.Length == 0)
                    {
                        return null;
                    }

                    if (!mapping.Transformations.TryGetValue(key, out var transformation))
                    {
                        return value;
                    }

                    try
                    {
                        return Transform(value, transformation);
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new InvalidDataException(
                            $"Таблица «{table.Name}», строка {source.Number}, поле «{key}»: {exception.Message}", exception);
                    }
                }

                if (mapping.Kind is "client" or "combined")
                {
                    var clientId = Read("clientId");
                    var clientName = Read("clientName");
                    var clientPhone = Read("clientPhone");
                    var hasClient = mapping.Kind == "client" || clientId != null || clientName != null || clientPhone != null;
                    if (hasClient && mapping.Kind == "combined" && clientId == null)
                    {
                        var phoneKey = new string((clientPhone ?? string.Empty).Where(char.IsDigit).ToArray());
                        clientId = phoneKey.Length > 0 ? $"@phone:{phoneKey}" : $"@row:{table.Id}:{source.Number}";
                    }

                    if (hasClient)
                    {
                        var candidate = new ImportCandidate
                        {
                            Id = $"{table.Id}:{source.Number}:client",
                            TableId = table.Id,
                            SourceRow = source.Number,
                            Kind = "client",
                            Name = clientName ?? string.Empty,
                            Phone = clientPhone,
                            Comment = Read("clientComment"),
                            ExternalId = clientId
                        };
                        var duplicate = mapping.Kind == "combined" &&
                            !knownCombinedClients.Add((candidate.ExternalId, candidate.Name, candidate.Phone, candidate.Comment));
                        if (!duplicate)
                        {
                            result.Add(candidate);
                        }
                    }

                    if (mapping.Kind == "client")
                    {
                        continue;
                    }

                    result.Add(new ImportCandidate
                    {
                        Id = $"{table.Id}:{source.Number}:vehicle",
                        TableId = table.Id,
                        SourceRow = source.Number,
                        Kind = "vehicle",
                        Name = Read("vehicleName") ?? string.Empty,
                        ExternalId = Read("vehicleId"),
                        Brand = Read("brand"),
                        Model = Read("model"),
                        Vin = Read("vin"),
                        Plate = Read("plate"),
                        Year = Read("year"),
                        ClientExternalId = Read("vehicleClientId") ?? (hasClient ? clientId : null),
                        ClientPhone = Read("vehicleClientPhone") ?? (hasClient && clientId == null ? clientPhone : null)
                    });
                    continue;
                }

                if (mapping.Kind == "vehicle")
                {
                    result.Add(new ImportCandidate
                    {
                        Id = $"{table.Id}:{source.Number}:vehicle",
                        TableId = table.Id,
                        SourceRow = source.Number,
                        Kind = "vehicle",
                        Name = Read("vehicleName") ?? string.Empty,
                        ExternalId = Read("vehicleId"),
                        Brand = Read("brand"),
                        Model = Read("model"),
                        Vin = Read("vin"),
                        Plate = Read("plate"),
                        Year = Read("year"),
                        ClientExternalId = Read("vehicleClientId"),
                        ClientPhone = Read("vehicleClientPhone")
                    });
                    continue;
                }

                result.Add(BuildBusinessCandidate(mapping.Kind, table.Id, source.Number, Read));
            }
        }

        return result;
    }

    private static ImportCandidate BuildBusinessCandidate(string kind, string tableId, int sourceRow, Func<string, string?> read)
    {
        return new ImportCandidate
        {
            Id = $"{tableId}:{sourceRow}:{kind}",
            TableId = tableId,
            SourceRow = sourceRow,
            Kind = kind,
            ExternalId = kind == "order" ? read("orderId") : null,
            VehicleExternalId = kind == "order" ? read("vehicleExternalId") : null,
            ClientExternalId = kind is "order" or "payment" ? read("partyClientId") : null,
            PartyName = kind is "order" or "payment" ? read("partyName") : null,
            OrderExternalId = kind == "order" ? null : read("orderExternalId"),
            OccurredAt = read("occurredAt"),
            OrderStatus = read("orderStatus"),
            OrderType = read("orderType"),
            Name = read("detailName") ?? string.Empty,
            DetailDescription = read("detailDescription"),
            Quantity = read("quantity"),
            UnitPrice = read("unitPrice"),
            Amount = read("amount"),
            DiscountAmount = read("discountAmount"),
            PaymentMethod = read("paymentMethod"),
            CancelledAt = read("cancelledAt"),
            ActorName = read("actorName"),
            EventType = read("eventType"),
            OrderComment = read("orderComment"),
            HistoryComment = read("historyComment")
        };
    }

    private static string SuggestKind(string[] columns)
    {
        var hasOrderReference = FindUniqueColumn(columns, s_aliases["orderExternalId"]) >= 0;
        if (hasOrderReference && FindUniqueColumn(columns, s_aliases["detailName"]) >= 0)
        {
            return "order-line";
        }

        if (hasOrderReference && FindUniqueColumn(columns, s_aliases["paymentMethod"]) >= 0)
        {
            return "payment";
        }

        if (hasOrderReference && FindUniqueColumn(columns, s_aliases["eventType"]) >= 0 && FindUniqueColumn(columns, s_aliases["occurredAt"]) >= 0)
        {
            return "history";
        }

        if (FindUniqueColumn(columns, s_aliases["orderId"]) >= 0 &&
            FindUniqueColumn(columns, s_aliases["vehicleExternalId"]) >= 0 &&
            FindUniqueColumn(columns, OrderStatusAliases) >= 0)
        {
            return "order";
        }

        var hasVehicle = FindUniqueColumn(columns, s_aliases["vin"]) >= 0 || FindUniqueColumn(columns, s_aliases["plate"]) >= 0;
        var hasClient = FindUniqueColumn(columns, s_aliases["clientName"]) >= 0 || FindUniqueColumn(columns, s_aliases["clientPhone"]) >= 0;

        return hasVehicle ? hasClient ? "combined" : "vehicle" : "client";
    }

    private static int FindUniqueColumn(string[] columns, string[] aliases)
    {
        var matches = columns.Select((column, index) => (column, index))
            .Where(item => aliases.Contains(item.column, StringComparer.Ordinal))
            .Select(item => item.index)
            .ToArray();

        return matches.Length == 1 ? matches[0] : -1;
    }

    private static string[] GetAliases(string kind, string field)
    {
        if (kind == "order" && field == "amount")
        {
            return OrderAmountAliases;
        }

        if (kind == "payment" && field == "amount")
        {
            return PaymentAmountAliases;
        }

        return s_aliases[field];
    }

    private static string[] FieldsFor(string kind) => kind switch
    {
        "client" => ["clientId", "clientName", "clientPhone", "clientComment"],
        "vehicle" => ["vehicleId", "vehicleName", "brand", "model", "vin", "plate", "year", "vehicleClientId", "vehicleClientPhone"],
        "combined" => ["clientId", "clientName", "clientPhone", "clientComment", "vehicleId", "vehicleName", "brand", "model", "vin", "plate", "year", "vehicleClientId", "vehicleClientPhone"],
        "order" => ["orderId", "vehicleExternalId", "orderStatus", "occurredAt", "orderType", "amount", "discountAmount", "orderComment", "partyClientId", "partyName"],
        "order-line" => ["orderExternalId", "detailName", "detailDescription", "quantity", "unitPrice"],
        "payment" => ["orderExternalId", "occurredAt", "amount", "paymentMethod", "cancelledAt", "actorName", "historyComment", "partyClientId", "partyName"],
        "history" => ["orderExternalId", "occurredAt", "actorName", "eventType", "historyComment"],
        "ignore" => [],
        _ => []
    };

    private static readonly HashSet<string> AllowedKinds = ["client", "vehicle", "combined", "order", "order-line", "payment", "history", "ignore"];
    private static readonly string[] PaymentAmountAliases = ["paymentamount", "amount", "суммаоплаты", "суммаплатежа"];
    private static readonly string[] OrderAmountAliases = ["ordertotal", "totalamount", "итогозаказа", "суммаитого"];
    private static readonly string[] OrderStatusAliases = ["orderstatus", "статусзаказа", "status"];

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsValidTransformation(ImportFieldTransformation? transformation)
    {
        if (transformation == null || transformation.Values == null)
        {
            return false;
        }

        if (transformation.Type == "phone")
        {
            return transformation.Format == null && transformation.Values.Count == 0;
        }

        if (transformation.Type == "date-exact")
        {
            return transformation.Format != null && s_supportedDateFormats.Contains(transformation.Format) && transformation.Values.Count == 0;
        }

        if (transformation.Type == "decimal")
        {
            return transformation.Format == null && transformation.Values.Count == 0;
        }

        return transformation.Type == "value-map" && transformation.Format == null &&
            transformation.Values.Count is > 0 and <= 100 &&
            transformation.Values.All(pair => pair.Key.Length is > 0 and <= 256 && pair.Value != null && pair.Value.Length <= 256);
    }

    private static string Transform(string value, ImportFieldTransformation transformation)
    {
        if (transformation.Type == "phone")
        {
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length == 11 && digits[0] == '8')
            {
                digits = $"7{digits[1..]}";
            }

            return digits;
        }

        if (transformation.Type == "decimal")
        {
            return value.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("\u00a0", string.Empty, StringComparison.Ordinal)
                .Replace("\u202f", string.Empty, StringComparison.Ordinal)
                .Replace(',', '.');
        }

        if (transformation.Type == "value-map")
        {
            if (!transformation.Values.TryGetValue(value, out var mappedValue))
            {
                throw new InvalidDataException("Для исходного значения нет пары в словаре.");
            }

            return mappedValue.Trim();
        }

        var format = transformation.Format!;
        if (format.Contains('H') || format.Contains('h') || format.Contains('m') || format.Contains('s'))
        {
            if (!DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            {
                throw new InvalidDataException($"Значение не соответствует формату «{format}».");
            }

            return dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (!DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new InvalidDataException($"Значение не соответствует формату «{format}».");
        }

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
