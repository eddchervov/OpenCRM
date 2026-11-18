using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class SupportMessageRowTests
{
    [Fact]
    public void BuiltInTypeAndStatusRefreshByCodeAndUnknownLabelsStayRaw()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-support-row-{Guid.NewGuid():N}.txt");
        using var preferenceScope = localization.UsePreferencePath(preferencePath);

        try
        {
            localization.Language = "ru";
            var row = new SupportMessageRow(new AdminSupportMessage
            {
                TypeCode = "bug",
                TypeName = "Ошибка",
                StatusCode = "closed",
                StatusName = "Закрыто"
            });

            localization.Language = "en";
            row.RefreshLocalizedValues();
            Assert.Equal("Bug report", row.Type);
            Assert.Equal("Closed", row.Status);

            localization.Language = "zh-Hans";
            row.RefreshLocalizedValues();
            Assert.Equal("错误报告", row.Type);
            Assert.Equal("已关闭", row.Status);

            var unknown = new SupportMessageRow(new AdminSupportMessage
            {
                TypeCode = "custom",
                TypeName = "Custom type",
                StatusCode = "custom",
                StatusName = "Custom status"
            });
            Assert.Equal("Custom type", unknown.Type);
            Assert.Equal("Custom status", unknown.Status);
        }
        finally
        {
            localization.Language = previousLanguage;
            if (File.Exists(preferencePath))
            {
                File.Delete(preferencePath);
            }
        }
    }
}
