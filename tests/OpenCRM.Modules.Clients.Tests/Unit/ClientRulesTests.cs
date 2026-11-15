using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;

namespace OpenCRM.Modules.Clients.Tests.Unit;

public sealed class ClientRulesTests
{
    [Theory]
    [InlineData(null, null, null)]
    [InlineData(" ", " ", null)]
    [InlineData(" Текст ", null, "Текст")]
    [InlineData(null, " Заметка ", "Заметка")]
    [InlineData(" Текст ", "Текст", "Текст")]
    [InlineData("Первый", "Второй", "Первый\n\nВторой")]
    public void CombinesLegacyNotesIntoOneComment(string? comment, string? notes, string? expected)
    {
        Assert.Equal(expected, ClientRules.NormalizeComment(comment, notes));
    }

    [Fact]
    public void CommentLimitIncludesLegacyNotesAndPreservesTheBoundary()
    {
        Assert.Equal(4000, ClientRules.NormalizeComment(new string('я', 4000), null)!.Length);
        Assert.Equal(4000, ClientRules.NormalizeComment(new string('я', 1998), new string('а', 2000))!.Length);
        Assert.Equal("Комментарий не должен превышать 4000 символов.", Assert.Throws<InvalidOperationException>(() =>
            ClientRules.NormalizeComment(new string('я', 4001), null)).Message);
        Assert.Equal("Комментарий не должен превышать 4000 символов.", Assert.Throws<InvalidOperationException>(() =>
            ClientRules.NormalizeComment(new string('я', 2000), new string('а', 2000))).Message);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("  Клиент  ", "Клиент")]
    public void OptionalTextIsTrimmedAndEmptyValuesAreAbsent(string? value, string? expected)
    {
        Assert.Equal(expected, ClientRules.NormalizeNullable(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RequiredTextKeepsTheCallingScenariosError(string? value)
    {
        Assert.Equal("Укажите имя", Assert.Throws<InvalidOperationException>(() =>
            ClientRules.NormalizeRequired(value, "Укажите имя")).Message);
    }

    [Fact]
    public void RequiredTextIsTrimmed()
    {
        Assert.Equal("Имя", ClientRules.NormalizeRequired(" Имя ", "Ошибка"));
    }

    [Theory]
    [InlineData(" person ", "person")]
    [InlineData("company", "company")]
    public void PartyTypeAcceptsOnlyExistingCodes(string value, string expected)
    {
        Assert.Equal(expected, ClientRules.NormalizePartyTypeCode(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("PERSON")]
    [InlineData("unknown")]
    public void PartyTypeRejectsInvalidCodes(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => ClientRules.NormalizePartyTypeCode(value));
    }

    [Fact]
    public void AccountingFieldsRespectTrimmedLengthAndAllowAbsentValues()
    {
        Assert.Null(ClientRules.NormalizeAccountingField(null, 3, "ИНН"));
        Assert.Null(ClientRules.NormalizeAccountingField(" ", 3, "ИНН"));
        Assert.Equal("123", ClientRules.NormalizeAccountingField(" 123 ", 3, "ИНН"));
        Assert.Equal("Поле «ИНН» не должно превышать 3 символов.", Assert.Throws<InvalidOperationException>(() =>
            ClientRules.NormalizeAccountingField("1234", 3, "ИНН")).Message);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ManualHistoryKeepsWallClockTimeAndMarksItAsUtc(DateTimeKind kind)
    {
        var input = new DateTime(2026, 10, 12, 9, 30, 0, kind);
        var normalized = ClientRules.NormalizeManualHistoryDate(input);

        Assert.Equal(input.Ticks, normalized.Ticks);
        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
    }

    [Fact]
    public void AbsentManualHistoryDateUsesCurrentUtcTime()
    {
        var before = DateTime.UtcNow;
        var actual = ClientRules.NormalizeManualHistoryDate(null);

        Assert.InRange(actual, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
    }

    [Fact]
    public void TagsSplitDeduplicateTruncateAndKeepTheFirstSpelling()
    {
        var tags = ClientRules.NormalizeTagNames([null!, " , ", " VIP , vip, Новый ", new string('я', 49)]);

        Assert.Equal(3, tags.Count);
        Assert.Contains(("VIP", "VIP"), tags);
        Assert.Contains(("Новый", "НОВЫЙ"), tags);
        Assert.Contains((new string('я', 48), new string('Я', 48)), tags);
        Assert.Empty(ClientRules.NormalizeTagNames([]));
    }

    [Theory]
    [InlineData(null, " ", null)]
    [InlineData(" Старое ", "Новое", "Поле: Старое -> Новое")]
    [InlineData(null, "Новое", "Поле: не указано -> Новое")]
    [InlineData("Старое", null, "Поле: Старое -> не указано")]
    [InlineData(" Имя ", "Имя", null)]
    public void ChangeDescriptionsIgnoreWhitespaceOnlyChanges(string? previous, string? current, string? expected)
    {
        var changes = new List<string>();
        ClientRules.AddChange(changes, "Поле", previous, current);

        Assert.Equal(expected == null ? [] : new[] { expected }, changes);
    }

    [Theory]
    [InlineData("Марка", "Модель", 2020, " Рабочая ", " А123 ", "Марка Модель • 2020 г. • Рабочая • А123")]
    [InlineData("", "", null, null, "А123", "Автомобиль · А123")]
    [InlineData("", "", null, " ", null, "Автомобиль")]
    [InlineData("Марка", "", null, null, " ", "Марка")]
    public void VehicleTitlesKeepExistingPartsWithoutRepeatingPlate(
        string brand, string model, int? year, string? name, string? plate, string expected)
    {
        Assert.Equal(expected, ClientRules.BuildVehicleDisplayName(brand, model, year, name, plate));
    }

    [Theory]
    [InlineData("Снимок", "email", "login", "old", false, "Снимок")]
    [InlineData(null, "email", "login", "old", false, "email")]
    [InlineData(" ", "email", null, null, true, "email (удаленный пользователь)")]
    [InlineData(null, null, "login", "old", false, "login")]
    [InlineData(null, " ", null, "old", false, "old")]
    [InlineData(null, null, null, null, false, null)]
    [InlineData(null, null, null, " ", true, "Удаленный пользователь")]
    public void ActorNamesPreserveHistoricalNamesAndDeletedUsers(
        string? snapshot, string? email, string? login, string? original, bool deleted, string? expected)
    {
        Assert.Equal(expected, ClientRules.ResolveActorName(snapshot, email, login, original, deleted));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData("missing", null)]
    [InlineData("owner", "Владелец")]
    public void ResponsibleNamesRequireAMatchingUser(string? userId, string? expected)
    {
        Assert.Equal(expected, ClientRules.ResolveResponsibleDisplayName(
            userId, new Dictionary<string, string> { ["owner"] = "Владелец" }));
    }

    [Theory]
    [InlineData(null, "Авто")]
    [InlineData(" ", "Авто")]
    [InlineData(" Работа ", "Авто: Работа")]
    public void HistoryDescriptionOmitsEmptyDetails(string? details, string expected)
    {
        Assert.Equal(expected, ClientRules.BuildHistoryDescription("Авто", details));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Completed, true)]
    [InlineData(MaintenanceStatus.Completed, false)]
    [InlineData(MaintenanceStatus.Planned, true)]
    [InlineData(MaintenanceStatus.InProgress, false)]
    public void HistoryDateUsesActualCompletionOrPlannedDateWithLegacyFallback(
        MaintenanceStatus status, bool hasSpecialDate)
    {
        var date = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        var item = new OrganizationClientMaintenanceItemDto
        {
            Status = status,
            Date = date,
            CompletedDate = hasSpecialDate ? date.AddDays(1) : null,
            PlannedDate = hasSpecialDate ? date.AddDays(2) : null
        };

        Assert.Equal(hasSpecialDate ? date.AddDays(status == MaintenanceStatus.Completed ? 1 : 2) : date,
            ClientRules.ResolveMaintenanceHistoryDate(item));
    }

    [Theory]
    [InlineData(MaintenanceType.Maintenance, "Обслуживание")]
    [InlineData(MaintenanceType.Repair, "Ремонт")]
    [InlineData(MaintenanceType.Refill, "Заправка")]
    [InlineData((MaintenanceType)999, "Другое")]
    public void HistoryNamesAllMaintenanceTypes(MaintenanceType type, string expected)
    {
        Assert.Equal(expected, ClientRules.MaintenanceTypeName(type));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned, "Запланировано")]
    [InlineData(MaintenanceStatus.InProgress, "В работе")]
    [InlineData(MaintenanceStatus.Completed, "Выполнено")]
    [InlineData(MaintenanceStatus.Cancelled, "Отменено")]
    [InlineData((MaintenanceStatus)999, "Неизвестно")]
    public void HistoryNamesAllMaintenanceStatuses(MaintenanceStatus status, string expected)
    {
        Assert.Equal(expected, ClientRules.MaintenanceStatusName(status));
    }

    [Theory]
    [InlineData(null, "active", "Активный")]
    [InlineData("UNKNOWN", "active", "Активный")]
    [InlineData(" LEAD ", "lead", "Лид")]
    [InlineData("attention", "attention", "Требует внимания")]
    [InlineData("paused", "paused", "Пауза")]
    [InlineData("lost", "lost", "Потерян")]
    public void StatusCodesKeepTheirPublicNormalization(string? value, string code, string name)
    {
        Assert.Equal(code, OrganizationClientStatusCodes.Normalize(value));
        Assert.Equal(name, OrganizationClientStatusCodes.GetName(value));
    }
}
