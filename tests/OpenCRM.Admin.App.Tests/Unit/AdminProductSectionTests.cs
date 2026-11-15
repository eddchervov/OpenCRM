using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class AdminProductSectionTests
{
    [Theory]
    [InlineData("/admin/product")]
    [InlineData("/admin/product/overview")]
    public void TryCreateAcceptsLocalPaths(string url)
    {
        var isValid = AdminProductSection.TryCreate("Аналитика", url, "Admin.Dashboard.View", out var section);

        Assert.True(isValid);
        Assert.NotNull(section);
        Assert.Equal(url, section.Url);
    }

    [Theory]
    [InlineData("https://example.invalid/page")]
    [InlineData("/https://example.invalid/page")]
    [InlineData("/../admin")]
    [InlineData("/%2e%2e/admin")]
    [InlineData("//example.invalid/page")]
    [InlineData("/\\example.invalid/page")]
    [InlineData("/admin\\page")]
    [InlineData("/admin/page?next=/users")]
    [InlineData("/admin/page#section")]
    [InlineData("/admin/\u0001page")]
    public void TryCreateRejectsNonLocalOrAmbiguousPaths(string url)
    {
        Assert.False(AdminProductSection.TryCreate("Раздел", url, "Admin.Dashboard.View", out _));
    }

    [Fact]
    public void BuildUriPreservesServerPathBase()
    {
        var section = new AdminProductSection("Аналитика", "/admin/product-analytics", "Admin.Dashboard.View");

        var uri = section.BuildUri(new Uri("https://crm.example.invalid/tenant/"));

        Assert.Equal("https://crm.example.invalid/tenant/admin/product-analytics", uri.AbsoluteUri);
    }

    [Fact]
    public void IsVisibleToUsesProfilePermissionsAndSystemAccess()
    {
        var section = new AdminProductSection("Аналитика", "/admin/product-analytics", "Admin.Dashboard.View");

        Assert.False(section.IsVisibleTo(null));
        Assert.False(section.IsVisibleTo(new AdminProfile { Permissions = ["Admin.Users.View"] }));
        Assert.True(section.IsVisibleTo(new AdminProfile { Permissions = ["Admin.Dashboard.View"] }));
        Assert.True(section.IsVisibleTo(new AdminProfile { IsSystem = true }));
    }

    [Fact]
    public void LoadProductSectionsKeepsValidLocalEntriesOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var brandFilePath = Path.Combine(root, "brand.json");

        try
        {
            File.WriteAllText(brandFilePath, """
                {
                  "Administration": {
                    "Sections": [
                      { "Title": "Аналитика", "Url": "/admin/product-analytics", "Permission": "Admin.Dashboard.View" },
                      { "Title": "Внешняя ссылка", "Url": "//example.invalid", "Permission": "Admin.Dashboard.View" },
                      { "Title": "Без права", "Url": "/admin/other", "Permission": " " }
                    ]
                  }
                }
                """);

            var sections = DesktopSettings.LoadProductSections(brandFilePath);
            File.WriteAllText(brandFilePath, "{ \"Brand\": {} }");
            var missingSectionSections = DesktopSettings.LoadProductSections(brandFilePath);
            var missingFileSections = DesktopSettings.LoadProductSections(Path.Combine(root, "missing.json"));

            var section = Assert.Single(sections);
            Assert.Equal("Аналитика", section.Title);
            Assert.Empty(missingSectionSections);
            Assert.Empty(missingFileSections);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
