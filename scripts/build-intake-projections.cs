#:package SixLabors.ImageSharp@3.1.12
#:property PublishAot=false

using System.Security.Cryptography;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

var repository = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
var assets = Path.Combine(repository, "src", "OpenCRM.Frontend", "packages", "workspace", "src", "assets", "intake");
var source = Path.Combine(repository, "scripts", "assets", "intake-body-projections.png");
var sourceBytes = await File.ReadAllBytesAsync(source);
var sourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
if (sourceHash != "1B4B73C27A642F4DD33B37D2DEFDBB03F0F6C158361FA0C76EDD270954AD0D79")
{
    throw new InvalidOperationException("Исходник схемы изменён; подготовка ракурсов остановлена.");
}

using var image = Image.Load<Rgba32>(sourceBytes);
var width = image.Width;
var height = image.Height;
// Удвоение разрешает нечётные половины без разных размеров холстов и интерполяции линий.
image.Mutate(context => context.Resize(width * 2, height * 2, KnownResamplers.NearestNeighbor));
var destination = Path.Combine(assets, "sedan-v1");
Directory.CreateDirectory(destination);
var views = new Dictionary<string, object>();
var positions = new[] { ("front", 0, 0), ("rear", width, 0), ("left", 0, height), ("right", width, height) };
foreach (var (view, x, y) in positions)
{
    using var projection = image.Clone(context => context.Crop(new Rectangle(x, y, width, height)));
    if (view is "left" or "right")
    {
        // В пустой полосе убираем край соседнего ракурса, ранее скрытый обрезкой 2%.
        projection.ProcessPixelRows(accessor =>
        {
            for (var row = 0; row < 20; row++)
            {
                accessor.GetRowSpan(row).Clear();
            }
        });
    }

    using var stream = new MemoryStream();
    await projection.SaveAsPngAsync(stream, new PngEncoder { ColorType = PngColorType.RgbWithAlpha });
    var bytes = stream.ToArray();
    var file = view + ".png";
    await File.WriteAllBytesAsync(Path.Combine(destination, file), bytes);
    views.Add(view, new { file, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
}

var manifest = new
{
    version = "sedan-v1",
    width,
    height,
    generation = new { sourceFile = "scripts/assets/intake-body-projections.png", sourceSha256 = sourceHash, scale = 2, sampler = "nearest-neighbor", clearedTopRowsForSideViews = 20 },
    views
};

await File.WriteAllTextAsync(Path.Combine(destination, "diagram.json"),
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine($"Подготовлены четыре ракурса {width}×{height}; исходник сохранён.");
