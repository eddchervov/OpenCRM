using System.Text;
using OpenCRM.Modules.Support.Personal;
using OpenCRM.Services.Interfaces.Home.Dtos;

namespace OpenCRM.Modules.Support.Tests.Unit;

public sealed class SupportAttachmentRulesTests
{
    public static TheoryData<string, byte[]> SupportedFiles => new()
    {
        { "photo.jpg", [0xff, 0xd8, 0xff, 0x00] },
        { "photo.jpeg", [0xff, 0xd8, 0xff, 0x00] },
        { "photo.png", [137, 80, 78, 71, 13, 10, 26, 10] },
        { "photo.webp", "RIFF\0\0\0\0WEBP"u8.ToArray() },
        { "report.pdf", "%PDF-1.7"u8.ToArray() },
        { "details.txt", Encoding.UTF8.GetBytes("Ошибка на странице") },
        { "details.log", Encoding.UTF8.GetBytes("Ошибка 404\n") }
    };

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public void ValidateAcceptsSupportedSignatures(string fileName, byte[] content)
    {
        SupportAttachmentRules.Validate([new SupportAttachmentUploadDto { FileName = fileName, Content = content }]);
    }

    [Theory]
    [InlineData("photo.jpg", new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })]
    [InlineData("photo.png", new byte[] { 255, 216, 255 })]
    [InlineData("photo.webp", new byte[] { 82, 73, 70, 70, 0, 0, 0, 0, 80, 78, 71 })]
    [InlineData("report.pdf", new byte[] { 60, 104, 116, 109, 108, 62 })]
    [InlineData("page.txt", new byte[] { 255, 254 })]
    [InlineData("page.svg", new byte[] { 60, 115, 118, 103, 62 })]
    [InlineData("program.exe", new byte[] { 77, 90, 0, 0 })]
    public void ValidateRejectsUnsupportedFilesAndMismatchedSignatures(string fileName, byte[] content)
    {
        Assert.Throws<ArgumentException>(() => SupportAttachmentRules.Validate(
            [new SupportAttachmentUploadDto { FileName = fileName, Content = content }]));
    }

    [Theory]
    [InlineData("C:\\upload\\screen.png", "screen.png")]
    [InlineData("folder/screen.png", "screen.png")]
    public void NormalizeFileNameRemovesDirectoryPath(string fileName, string expected)
    {
        Assert.Equal(expected, SupportAttachmentRules.NormalizeFileName(fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("file\nname.png")]
    [InlineData("file\u007fname.png")]
    public void NormalizeFileNameRejectsEmptyOrControlCharacters(string fileName)
    {
        Assert.Throws<ArgumentException>(() => SupportAttachmentRules.NormalizeFileName(fileName));
    }

    [Fact]
    public void NormalizeFileNameRejectsNamesLongerThanTwoHundredCharacters()
    {
        Assert.Throws<ArgumentException>(() => SupportAttachmentRules.NormalizeFileName(new string('a', 197) + ".png"));
    }

    [Fact]
    public void ValidateAllowsFiveFilesAndRejectsSix()
    {
        var uploads = Enumerable.Range(0, 5)
            .Select(index => Upload($"photo-{index}.jpg", [0xff, 0xd8, 0xff]))
            .ToList();

        SupportAttachmentRules.Validate(uploads);

        uploads.Add(Upload("sixth.jpg", [0xff, 0xd8, 0xff]));
        Assert.Throws<ArgumentException>(() => SupportAttachmentRules.Validate(uploads));
    }

    [Fact]
    public void ValidateAllowsMaximumFileAndTotalSizeAndRejectsLargerFiles()
    {
        var maximumFile = new byte[SupportAttachmentRules.MaximumFileSize];
        maximumFile[0] = 0xff;
        maximumFile[1] = 0xd8;
        maximumFile[2] = 0xff;
        var uploads = new[]
        {
            Upload("first.jpg", maximumFile),
            Upload("second.jpg", maximumFile)
        };

        SupportAttachmentRules.Validate(uploads);

        Assert.Throws<ArgumentException>(() => SupportAttachmentRules.Validate(
            [Upload("large.jpg", new byte[SupportAttachmentRules.MaximumFileSize + 1])]));
    }

    [Fact]
    public void ValidateRejectsTotalSizeOverLimit()
    {
        var content = new byte[SupportAttachmentRules.MaximumFileSize];
        content[0] = 0xff;
        content[1] = 0xd8;
        content[2] = 0xff;
        var uploads = Enumerable.Range(0, 3).Select(index => Upload($"photo-{index}.jpg", content)).ToArray();

        Assert.Throws<ArgumentException>(() => SupportAttachmentRules.Validate(uploads));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("details.txt")]
    public void ValidateRejectsEmptyFiles(string fileName)
    {
        Assert.Throws<ArgumentException>(() => SupportAttachmentRules.Validate([Upload(fileName, [])]));
    }

    private static SupportAttachmentUploadDto Upload(string fileName, byte[] content) =>
        new() { FileName = fileName, Content = content };
}
