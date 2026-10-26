using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;

namespace VirtualDuplexPrinter;

internal static class PrintLayout
{
    // All dimensions here are physical hundredths of an inch, independent of device DPI.
    // Shrink only, preserve aspect ratio, and center inside the hard printable area.
    public static RectangleF Fit(double widthDip, double heightDip, RectangleF printable)
    {
        if (!double.IsFinite(widthDip) || !double.IsFinite(heightDip) || widthDip <= 0 || heightDip <= 0 ||
            !float.IsFinite(printable.X) || !float.IsFinite(printable.Y) ||
            !float.IsFinite(printable.Width) || !float.IsFinite(printable.Height) || printable.Width <= 0 || printable.Height <= 0)
            throw new InvalidDataException("Érvénytelen PDF- vagy nyomtatható oldalméret.");
        double width = widthDip * 100 / 96;
        double height = heightDip * 100 / 96;
        double scale = Math.Min(1, Math.Min(printable.Width / width, printable.Height / height));
        float w = (float)(width * scale), h = (float)(height * scale);
        return new RectangleF(printable.X + (printable.Width - w) / 2, printable.Y + (printable.Height - h) / 2, w, h);
    }

    public static PaperSize SelectPaper(RenderedPage page, IEnumerable<PaperSize> available, PaperSize fallback)
    {
        double shortSide = Math.Min(page.WidthDip, page.HeightDip) * 100 / 96;
        double longSide = Math.Max(page.WidthDip, page.HeightDip) * 100 / 96;
        return available.FirstOrDefault(p => Math.Abs(Math.Min(p.Width, p.Height) - shortSide) <= 3 &&
            Math.Abs(Math.Max(p.Width, p.Height) - longSide) <= 3) ?? fallback;
    }

    public static bool RotatePaper(RenderedPage page, PaperSize paper) =>
        (page.WidthDip > page.HeightDip) != (paper.Width > paper.Height);

    internal static void Draw(Graphics graphics, Image image, RenderedPage page, RectangleF printable, float hardX, float hardY)
    {
        var state = graphics.Save();
        try
        {
            graphics.PageUnit = GraphicsUnit.Inch;
            graphics.PageScale = .01f;
            // The printer Graphics origin is already at its hardware margin.
            graphics.TranslateTransform(-hardX, -hardY);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.DrawImage(image, Fit(page.WidthDip, page.HeightDip, printable),
                new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
        }
        finally { graphics.Restore(state); }
    }
}

internal static class WindowsPdfPrinter
{
    public static string WorkerExecutable => Path.Combine(AppContext.BaseDirectory, "DuplexPrint.exe");

    public static ProcessStartInfo CreateStartInfo(string printer, string pdf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printer);
        var info = new ProcessStartInfo(WorkerExecutable);
        foreach (var argument in new[] { "--print-pass", printer, Path.GetFullPath(pdf) })
            info.ArgumentList.Add(argument);
        return info;
    }

    public static Task PrintAsync(string printer, string pdf, CancellationToken cancellationToken = default)
    {
        NativePdfRenderer.EnsureAvailable();
        if (!File.Exists(pdf)) throw new FileNotFoundException("A nyomtatandó PDF hiányzik.", pdf);
        // A printer driver failure or a stuck render stays outside the interactive WPF process.
        return WorkerProcess.RunAsync(CreateStartInfo(printer, pdf), TimeSpan.FromMinutes(10), cancellationToken);
    }

    // Regression tests enter this path only with a deliberately nonexistent printer.
    public static async Task PrintInWorkerAsync(string printer, string pdf)
    {
        if (AppPaths.IsCapturePrinter(printer))
            throw new InvalidOperationException("A DuplexPrint virtuális sor helyett fizikai nyomtatót válassz.");
        using var document = new PrintDocument();
        document.PrinterSettings.PrinterName = printer;
        if (!document.PrinterSettings.IsValid)
            throw new InvalidOperationException("A kiválasztott nyomtató nem érhető el.");
        document.PrinterSettings.Copies = 1;
        document.PrinterSettings.Duplex = Duplex.Simplex;
        document.PrinterSettings.PrintRange = PrintRange.AllPages;
        document.PrinterSettings.PrintToFile = false;
        document.OriginAtMargins = false;
        document.PrintController = new StandardPrintController();
        document.DocumentName = "DuplexPrint - " + Path.GetFileNameWithoutExtension(pdf);
        var paperSizes = document.PrinterSettings.PaperSizes.Cast<PaperSize>().ToArray();
        var defaultPaper = document.DefaultPageSettings.PaperSize;
        // Under a disposable parent job: even a forcibly stopped worker leaves no unowned PNGs.
        using var rendered = await NativePdfRenderer.RenderAsync(pdf, parentDirectory: Path.GetDirectoryName(Path.GetFullPath(pdf)));
        int index = 0;
        document.QueryPageSettings += (_, e) =>
        {
            var page = rendered.Pages[index];
            var paper = PrintLayout.SelectPaper(page, paperSizes, defaultPaper);
            e.PageSettings.PaperSize = paper;
            e.PageSettings.Landscape = PrintLayout.RotatePaper(page, paper);
            e.PageSettings.Margins = new Margins(0, 0, 0, 0);
        };
        document.PrintPage += (_, e) =>
        {
            var page = rendered.Pages[index];
            using var image = Image.FromFile(page.FilePath);
            PrintLayout.Draw(e.Graphics ?? throw new IOException("A nyomtató nem biztosított rajzfelületet."), image, page,
                e.PageSettings.PrintableArea, e.PageSettings.HardMarginX, e.PageSettings.HardMarginY);
            e.HasMorePages = ++index < rendered.Pages.Count;
        };
        // Runs in a dedicated child process. No UI, shell verb, PDF viewer, or default-printer fallback.
        document.Print();
    }
}
