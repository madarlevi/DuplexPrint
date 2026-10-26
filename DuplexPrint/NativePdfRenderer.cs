using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace VirtualDuplexPrinter;

internal sealed record RenderedPage(string FilePath, double WidthDip, double HeightDip, int PixelWidth, int PixelHeight);

internal sealed class RenderedPdf : IDisposable
{
    public string DirectoryPath { get; }
    public IReadOnlyList<RenderedPage> Pages { get; internal set; } = [];
    public RenderedPdf(string? parentDirectory)
    {
        DirectoryPath = parentDirectory is null
            ? Directory.CreateTempSubdirectory("DuplexPrint-render-").FullName
            : Directory.CreateDirectory(Path.Combine(parentDirectory, "render-" + Guid.NewGuid().ToString("N"))).FullName;
    }
    public void Dispose()
    {
        try { Directory.Delete(DirectoryPath, true); }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.TraceWarning(ex.Message); }
    }
}

internal static class NativePdfRenderer
{
    public static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "DuplexPrint.PdfWorker.exe");
    public static void EnsureAvailable()
    {
        if (!File.Exists(ExecutablePath))
            throw new FileNotFoundException("Az alkalmazás PDF-feldolgozó fájlja hiányzik. " +
                "Csomagold ki újra a teljes DuplexPrint ZIP-et ugyanabba a mappába.\n" + ExecutablePath);
    }

    public static async Task<RenderedPdf> RenderAsync(string pdf, int dpi = 600, string? parentDirectory = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 72);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dpi, 600);
        EnsureAvailable();
        if (!File.Exists(pdf)) throw new FileNotFoundException("A PDF nem található.", pdf);
        var result = new RenderedPdf(parentDirectory);
        try
        {
            var info = new ProcessStartInfo(ExecutablePath);
            foreach (string argument in new[] { "--render", Path.GetFullPath(pdf), result.DirectoryPath, dpi.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                info.ArgumentList.Add(argument);
            await WorkerProcess.RunAsync(info, TimeSpan.FromMinutes(8), cancellationToken);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.DirectoryPath, "manifest.json"), cancellationToken));
            var pages = manifest.RootElement.GetProperty("pages");
            if (pages.GetArrayLength() == 0) throw new InvalidDataException("A PDF nem tartalmaz oldalt.");
            var rendered = new List<RenderedPage>();
            foreach (var page in pages.EnumerateArray())
            {
                string expected = $"page-{rendered.Count + 1}.png";
                if (page.GetProperty("file").GetString() != expected)
                    throw new InvalidDataException("Hibás PDF-képjegyzék.");
                double width = page.GetProperty("widthDip").GetDouble();
                double height = page.GetProperty("heightDip").GetDouble();
                int pixelWidth = page.GetProperty("pixelWidth").GetInt32();
                int pixelHeight = page.GetProperty("pixelHeight").GetInt32();
                string file = Path.Combine(result.DirectoryPath, expected);
                if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 ||
                    pixelWidth <= 0 || pixelHeight <= 0 || (long)pixelWidth * pixelHeight > 134217728 || !File.Exists(file))
                    throw new InvalidDataException("Hibás PDF-oldalkép.");
                // Validate the whole pass before its first sheet is submitted.
                using var image = System.Drawing.Image.FromFile(file);
                if (image.Width != pixelWidth || image.Height != pixelHeight)
                    throw new InvalidDataException("A PDF-oldalkép mérete eltér a jegyzéktől.");
                rendered.Add(new RenderedPage(file, width, height, pixelWidth, pixelHeight));
            }
            result.Pages = rendered;
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
