using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using VirtualDuplexPrinter;

internal static class NativePrintingTests
{
    private static readonly Color[] Colors = [Color.Red, Color.Lime, Color.Blue, Color.Cyan, Color.Magenta, Color.Yellow, Color.DarkRed, Color.DarkGreen, Color.DarkBlue];

    public static void Run(string root, Action<string, Action> check)
    {
        check("Windows PDF worker is deployed with the application", () =>
        {
            NativePdfRenderer.EnsureAvailable();
            Require(new FileInfo(NativePdfRenderer.ExecutablePath).Length > 0, "Missing native worker");
        });
        foreach (int count in new[] { 1, 2, 3, 4, 5, 6, 9 })
            foreach (bool reverse in new[] { false, true })
                check($"Native render: {count} pages, reverse={reverse}, actual front/back colors", () =>
                {
                    var source = MakeColorPdf(root, count);
                    using var job = PdfPrintJob.Prepare(source, reverse);
                    using var fronts = NativePdfRenderer.RenderAsync(job.OddPath, 96).GetAwaiter().GetResult();
                    VerifyColors(fronts, Enumerable.Range(1, count).Where(n => n % 2 == 1).Select(n => Colors[n - 1]).ToArray());
                    if (count > 1)
                    {
                        using var backs = NativePdfRenderer.RenderAsync(job.EvenPath, 96).GetAwaiter().GetResult();
                        var expected = Enumerable.Range(1, count).Where(n => n % 2 == 0).Select(n => Colors[n - 1]).ToList();
                        if (count % 2 != 0) expected.Add(Color.White);
                        if (reverse) expected.Reverse();
                        VerifyColors(backs, expected.ToArray());
                    }
                });
        check("Native crop box, rotation, portrait/landscape and 600 DPI dimensions", () =>
        {
            var path = Path.Combine(root, "geometry ő & %.pdf");
            using (var doc = new PdfDocument())
            {
                foreach (int rotation in new[] { 0, 90, 180, 270 })
                {
                    var page = doc.AddPage();
                    page.MediaBox = Box(0, 0, 144, 216);
                    page.CropBox = Box(0, 0, 72, 144);
                    page.Rotate = rotation;
                    using var graphics = XGraphics.FromPdfPage(page);
                    graphics.DrawRectangle(XBrushes.Red, 0, 0, 144, 216);
                }
                doc.Save(path);
            }
            using var rendered = NativePdfRenderer.RenderAsync(path, 600).GetAwaiter().GetResult();
            Require(rendered.Pages.Count == 4, "Missing geometry pages");
            for (int i = 0; i < 4; i++)
            {
                bool landscape = i % 2 != 0;
                var page = rendered.Pages[i];
                Require(page.PixelWidth == (landscape ? 1200 : 600) && page.PixelHeight == (landscape ? 600 : 1200), "Crop/rotation or DPI mismatch");
                Require(Math.Abs(page.WidthDip - (landscape ? 192 : 96)) < .01, "Wrong physical page width");
            }
        });
        check("Native text, vector, transparency and embedded image rendering", () =>
        {
            PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            string path = Path.Combine(root, "representative document.pdf");
            using (var doc = new PdfDocument())
            {
                var page = doc.AddPage();
                page.MediaBox = Box(0, 0, 288, 360);
                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawString("DuplexPrint 123 - PDF render test", new XFont("Arial", 12), XBrushes.Black, new XPoint(15, 30));
                graphics.DrawRectangle(XBrushes.Red, 20, 50, 90, 70);
                graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(128, 0, 0, 255)), 60, 70, 90, 70);
                using var image = new Bitmap(20, 20);
                using (var g = Graphics.FromImage(image)) g.Clear(Color.Lime);
                using var png = new MemoryStream();
                image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                png.Position = 0;
                using var embedded = XImage.FromStream(png);
                graphics.DrawImage(embedded, 20, 170, 70, 70);
                doc.Save(path);
            }
            using var rendered = NativePdfRenderer.RenderAsync(path, 144).GetAwaiter().GetResult();
            using var bitmap = new Bitmap(rendered.Pages[0].FilePath);
            Require(bitmap.GetPixel(60, 140).R > 240, "Vector rectangle not rendered");
            var blended = bitmap.GetPixel(160, 180);
            Require(blended.R > 90 && blended.R < 170 && blended.B > 90, "Transparency not rendered");
            Require(bitmap.GetPixel(70, 400).G > 240, "Embedded image not rendered");
            int textPixels = 0;
            for (int y = 35; y < 65; y++)
                for (int x = 20; x < 500; x++)
                    if (bitmap.GetPixel(x, y).R < 80) textPixels++;
            Require(textPixels > 100, "Text not rendered");
            string? evidence = Environment.GetEnvironmentVariable("DUPLEXPRINT_TEST_EVIDENCE");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Directory.CreateDirectory(evidence);
                File.Copy(path, Path.Combine(evidence, "representative.pdf"), true);
                File.Copy(rendered.Pages[0].FilePath, Path.Combine(evidence, "representative.png"), true);
            }
        });
        check("Odd-page blank back preserves the front's crop box and rotation", () =>
        {
            var path = Path.Combine(root, "cropped odd.pdf");
            using (var doc = new PdfDocument())
            {
                for (int i = 0; i < 3; i++)
                {
                    var page = doc.AddPage();
                    page.MediaBox = Box(0, 0, 144, 216);
                    page.CropBox = Box(0, 0, 72, 144);
                    page.Rotate = 90;
                }
                doc.Save(path);
            }
            using var job = PdfPrintJob.Prepare(path, false);
            using var backs = NativePdfRenderer.RenderAsync(job.EvenPath, 96).GetAwaiter().GetResult();
            Require(backs.Pages[0].WidthDip == backs.Pages[1].WidthDip && backs.Pages[0].HeightDip == backs.Pages[1].HeightDip, "Blank crop/rotation differs from its front");
        });
        check("Production print worker rejects an unavailable printer without submitting", () =>
        {
            string invalidPrinter = "DuplexPrint regression nonexistent " + Guid.NewGuid().ToString("N");
            var info = WindowsPdfPrinter.CreateStartInfo(invalidPrinter, MakeColorPdf(root, 1));
            try
            {
                WorkerProcess.RunAsync(info, TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                throw new InvalidOperationException("Missing printer was accepted");
            }
            catch (IOException ex) { Require(ex.Message.Contains("nem érhető el"), "Wrong worker failure: " + ex.Message); }
        });
        check("Native invalid and protected PDFs fail before printing and clean render output", () =>
        {
            string parent = Directory.CreateDirectory(Path.Combine(root, "render failures")).FullName;
            string broken = Path.Combine(root, "broken.pdf");
            File.WriteAllText(broken, "not a PDF");
            ExpectFailure(() => NativePdfRenderer.RenderAsync(broken, 96, parent).GetAwaiter().GetResult());
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.SecuritySettings.UserPassword = "test-password";
                doc.SecuritySettings.OwnerPassword = "test-owner";
                doc.Save(broken);
            }
            ExpectFailure(() => NativePdfRenderer.RenderAsync(broken, 96, parent).GetAwaiter().GetResult());
            Require(Directory.GetDirectories(parent).Length == 0, "Failed render left temporary files");
        });
        check("Native A4, Letter landscape, A5 and A3 page sizes render accurately", () =>
        {
            string path = Path.Combine(root, "common paper sizes.pdf");
            (double Width, double Height)[] sizes = [(595.28, 841.89), (792, 612), (419.53, 595.28), (841.89, 1190.55)];
            using (var doc = new PdfDocument())
            {
                foreach (var size in sizes) doc.AddPage().MediaBox = Box(0, 0, size.Width, size.Height);
                doc.Save(path);
            }
            using var output = NativePdfRenderer.RenderAsync(path, 144).GetAwaiter().GetResult();
            for (int i = 0; i < sizes.Length; i++)
                Require(Math.Abs(output.Pages[i].WidthDip - sizes[i].Width * 96 / 72) < .01 &&
                    Math.Abs(output.Pages[i].HeightDip - sizes[i].Height * 96 / 72) < .01, "Common paper dimensions changed");
        });
        check("Native A3 at production 600 DPI fits the guarded memory limit", () =>
        {
            string path = Path.Combine(root, "A3 600dpi.pdf");
            using (var doc = new PdfDocument()) { doc.AddPage().MediaBox = Box(0, 0, 841.89, 1190.55); doc.Save(path); }
            using var output = NativePdfRenderer.RenderAsync(path).GetAwaiter().GetResult();
            Require(output.Pages[0].PixelWidth > 7000 && output.Pages[0].PixelHeight > 9900, "Production DPI was reduced");
        });
        check("Oversized pages and invalid DPI fail safely without leaving images", () =>
        {
            string path = Path.Combine(root, "oversized.pdf");
            string parent = Directory.CreateDirectory(Path.Combine(root, "oversized output")).FullName;
            using (var doc = new PdfDocument()) { doc.AddPage().MediaBox = Box(0, 0, 20000, 20000); doc.Save(path); }
            ExpectFailure(() => NativePdfRenderer.RenderAsync(path, 600, parent).GetAwaiter().GetResult());
            ExpectFailure(() => NativePdfRenderer.RenderAsync(path, -1, parent).GetAwaiter().GetResult());
            Require(Directory.GetDirectories(parent).Length == 0, "Oversized render left output");
        });
        check("Native renderer cancellation cleans its owned directory", () =>
        {
            string parent = Directory.CreateDirectory(Path.Combine(root, "cancel render")).FullName;
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            ExpectFailure(() => NativePdfRenderer.RenderAsync(MakeColorPdf(root, 1), 96, parent, cancel.Token).GetAwaiter().GetResult());
            Require(Directory.GetDirectories(parent).Length == 0, "Cancelled render left output");
        });
        check("Print layout shrinks without cropping, distortion or unwanted enlargement", () =>
        {
            var bounds = new RectangleF(20, 30, 800, 1000);
            var fit = PrintLayout.Fit(960, 480, bounds);
            Require(Math.Abs(fit.Width - 800) < .01 && Math.Abs(fit.Height - 400) < .01, "Aspect ratio changed");
            Require(Math.Abs(fit.X - 20) < .01 && Math.Abs(fit.Y - 330) < .01, "Wrong centering/margins");
            var small = PrintLayout.Fit(96, 96, bounds);
            Require(small.Width == 100 && small.Height == 100, "Small page was enlarged");
            ExpectFailure(() => PrintLayout.Fit(double.NaN, 100, bounds));
            ExpectFailure(() => PrintLayout.Fit(100, 100, RectangleF.Empty));
        });
        check("Driver paper matching supports rotated sizes and retains default for unknown paper", () =>
        {
            var a4 = new PaperSize("A4", 827, 1169);
            var letter = new PaperSize("Letter", 850, 1100);
            var page = new RenderedPage("unused", 1169 * .96, 827 * .96, 10, 10);
            Require(ReferenceEquals(PrintLayout.SelectPaper(page, new[] { letter, a4 }, letter), a4), "Landscape A4 not selected");
            Require(ReferenceEquals(PrintLayout.SelectPaper(page with { WidthDip = 100 }, new[] { a4 }, letter), letter), "Unknown size ignored driver default");
            Require(PrintLayout.RotatePaper(page, a4), "Portrait A4 was not rotated for landscape content");
            var landscapeForm = new PaperSize("Custom landscape", 1169, 827);
            Require(!PrintLayout.RotatePaper(page, landscapeForm), "Already-landscape custom form was rotated twice");
        });
        check("Printing canvas compensates hard margins in physical units", () =>
        {
            using var canvas = new Bitmap(300, 500);
            canvas.SetResolution(100, 100);
            using var graphics = Graphics.FromImage(canvas);
            graphics.Clear(Color.White);
            using var image = new Bitmap(10, 10);
            using (var g = Graphics.FromImage(image)) g.Clear(Color.Red);
            PrintLayout.Draw(graphics, image, new RenderedPage("unused", 96, 96, 10, 10), new RectangleF(20, 30, 260, 440), 20, 30);
            Require(canvas.GetPixel(130, 220).R > 240 && canvas.GetPixel(130, 220).G < 10, "Image missed its physical location");
            Require(canvas.GetPixel(20, 20).G > 240, "Unexpected clipping/scale outside destination");
        });
        check("Native worker arguments preserve printer names and Unicode without a shell", () =>
        {
            string printer = "Printer ő & \"quoted\"";
            string pdf = Path.Combine(root, "ő & sample.pdf");
            var info = WindowsPdfPrinter.CreateStartInfo(printer, pdf);
            Require(info.ArgumentList.SequenceEqual(new[] { "--print-pass", printer, pdf }), "Wrong print protocol");
            Require(string.IsNullOrEmpty(info.Arguments), "Unsafe command string");
        });
    }

    private static string MakeColorPdf(string root, int count)
    {
        string path = Path.Combine(root, "color document ő & %.pdf");
        using var document = new PdfDocument();
        for (int i = 0; i < count; i++)
        {
            var page = document.AddPage();
            page.MediaBox = Box(0, 0, 144 + i * 12, 216 + i * 12);
            using var graphics = XGraphics.FromPdfPage(page);
            var color = Colors[i];
            graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(color.R, color.G, color.B)), 0, 0, page.MediaBox.Width, page.MediaBox.Height);
        }
        document.Save(path);
        return path;
    }
    private static void VerifyColors(RenderedPdf document, Color[] expected)
    {
        Require(document.Pages.Count == expected.Length, "Wrong rendered page count");
        for (int i = 0; i < expected.Length; i++)
        {
            using var bitmap = new Bitmap(document.Pages[i].FilePath);
            var color = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            Require(Math.Abs(color.R - expected[i].R) < 5 && Math.Abs(color.G - expected[i].G) < 5 && Math.Abs(color.B - expected[i].B) < 5, "Rendered page order/content mismatch");
        }
    }
    private static PdfRectangle Box(double x1, double y1, double x2, double y2) => new(new XPoint(x1, y1), new XPoint(x2, y2));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch { return; }
        throw new InvalidOperationException("Expected a failure");
    }
}
