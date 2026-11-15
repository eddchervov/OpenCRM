using OpenCRM.Domain;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MediaPhotoDescriptionTests
{
    [Fact]
    public void DescriptionPreservesFileOrderAndTrimsNamesWithoutIncludingEmptyValues()
    {
        var description = MediaPhotoDescription.Build("Файлы", [null, "", "  ", " до.jpg ", "после.png"]);

        Assert.Equal("Файлы: до.jpg, после.png", description);
    }

    [Fact]
    public void EmptyFileListProducesAnExplicitMissingName()
    {
        Assert.Equal("Файл: не указано", MediaPhotoDescription.Build("Файл", []));
    }
}
