using OpenCRM.Domain;
using OpenCRM.Modules.Organizations.Rules;

namespace OpenCRM.Modules.Organizations.Tests.Unit;

public sealed class OrganizationRulesTests
{
    [Theory]
    [InlineData(OrganizationInvitationStatusCodes.Pending, "Ожидает ответа")]
    [InlineData(OrganizationInvitationStatusCodes.Accepted, "Принято")]
    [InlineData(OrganizationInvitationStatusCodes.Declined, "Отклонено")]
    [InlineData(OrganizationInvitationStatusCodes.Revoked, "Отозвано")]
    [InlineData("custom", "custom")]
    public void GetInvitationStatusNameReturnsRussianNameOrOriginalCode(string code, string expected)
    {
        Assert.Equal(expected, OrganizationRules.GetInvitationStatusName(code));
    }

    [Fact]
    public void EnsureOwnerAllowsOnlyTheConfiguredOwner()
    {
        var organization = new Organization { OwnerUserId = "owner" };

        OrganizationRules.EnsureOwner("owner", organization);

        var exception = Assert.Throws<InvalidOperationException>(() => OrganizationRules.EnsureOwner("employee", organization));
        Assert.Equal("Изменять критичные настройки может только владелец организации.", exception.Message);
    }

    [Fact]
    public void BuildOrganizationChangeDescriptionIncludesOnlyChangedTrimmedValues()
    {
        Assert.Equal(
            "Название: Старое -> Новое; Тип организации: service_station -> detailing",
            OrganizationRules.BuildOrganizationChangeDescription(" Старое ", " Новое ", " service_station ", " detailing "));
        Assert.Equal("Изменены настройки организации.",
            OrganizationRules.BuildOrganizationChangeDescription("Название", " Название ", "detailing", "detailing"));
    }

    [Fact]
    public void AddChangeUsesPlaceholderForMissingValuesAndOrdinalComparison()
    {
        var changes = new List<string>();

        OrganizationRules.AddChange(changes, "Поле", null, " значение ");
        OrganizationRules.AddChange(changes, "Поле", "Значение", "значение");
        OrganizationRules.AddChange(changes, "Поле", " ", "");

        Assert.Equal(["Поле: не указано -> значение", "Поле: Значение -> значение"], changes);
    }

    [Theory]
    [InlineData(OrganizationRoleCodes.Owner, OrganizationRoleCodes.Owner)]
    [InlineData(" ADMIN ", OrganizationRoleCodes.Admin)]
    [InlineData(OrganizationRoleCodes.Employee, OrganizationRoleCodes.Employee)]
    [InlineData(OrganizationRoleCodes.Performer, OrganizationRoleCodes.Performer)]
    [InlineData(OrganizationRoleCodes.Viewer, OrganizationRoleCodes.Viewer)]
    [InlineData("unknown", OrganizationRoleCodes.Employee)]
    [InlineData(null, OrganizationRoleCodes.Employee)]
    public void NormalizeRoleReturnsKnownLowercaseRoleOrEmployee(string? value, string expected)
    {
        Assert.Equal(expected, OrganizationRules.NormalizeRole(value));
    }

    [Theory]
    [InlineData(" Code ", "code")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void NormalizeCodeTrimsAndLowercases(string? value, string expected)
    {
        Assert.Equal(expected, OrganizationRules.NormalizeCode(value));
    }

    [Theory]
    [InlineData(" AUTO ", "auto")]
    [InlineData("detailing", "detailing")]
    [InlineData("fleet", "fleet")]
    [InlineData("minimal", "minimal")]
    [InlineData("unsupported", "auto")]
    [InlineData(null, "auto")]
    public void NormalizeDocumentStyleKeepsSupportedStylesAndUsesDefaultOtherwise(string? value, string expected)
    {
        Assert.Equal(expected, OrganizationRules.NormalizeDocumentStyle(value));
    }

    [Theory]
    [InlineData(" SIMPLE ", "simple")]
    [InlineData("staged", "staged")]
    [InlineData("unknown", "staged")]
    [InlineData(null, "staged")]
    public void NormalizeMaintenanceWorkflowModeKeepsSupportedValues(string? value, string expected)
    {
        Assert.Equal(expected, OrganizationRules.NormalizeMaintenanceWorkflowMode(value));
    }

    [Theory]
    [InlineData("sto", "service-station")]
    [InlineData("detail", "detailing")]
    [InlineData("wrap", "wrapping")]
    [InlineData("school", "driving-school")]
    [InlineData("unknown", "unknown")]
    public void NormalizeOrganizationTypeAliasReturnsCatalogAlias(string input, string expected)
    {
        Assert.Equal(expected, OrganizationRules.NormalizeOrganizationTypeAlias(input));
    }

    [Fact]
    public void NormalizeEmailTrimsAndUppercasesInvariantly()
    {
        Assert.Equal("USER@EXAMPLE.TEST", OrganizationRules.NormalizeEmail(" user@example.test "));
    }

    [Theory]
    [InlineData("  required  ", "required")]
    public void NormalizeRequiredReturnsTrimmedValue(string? value, string expected)
    {
        Assert.Equal(expected, OrganizationRules.NormalizeRequired(value, "Ошибка."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NormalizeRequiredRejectsMissingValue(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OrganizationRules.NormalizeRequired(value, "Обязательное поле."));

        Assert.Equal("Обязательное поле.", exception.Message);
    }

    [Theory]
    [InlineData(" value ", "value")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void NormalizeNullableTrimsAndConvertsMissingValuesToNull(string? value, string? expected)
    {
        Assert.Equal(expected, OrganizationRules.NormalizeNullable(value));
    }

    [Theory]
    [InlineData(OrganizationRoleCodes.Owner, 0, "Владелец")]
    [InlineData(OrganizationRoleCodes.Admin, 1, "Администратор")]
    [InlineData(OrganizationRoleCodes.Employee, 2, "Сотрудник")]
    [InlineData(OrganizationRoleCodes.Performer, 3, "Исполнитель")]
    [InlineData(OrganizationRoleCodes.Viewer, 4, "Только просмотр")]
    [InlineData("unknown", 10, "unknown")]
    public void RoleMetadataMapsKnownRolesAndPreservesUnknownCodes(string role, int sortOrder, string name)
    {
        Assert.Equal(sortOrder, OrganizationRules.RoleSortOrder(role));
        Assert.Equal(name, OrganizationRules.GetRoleName(role));
    }
}
