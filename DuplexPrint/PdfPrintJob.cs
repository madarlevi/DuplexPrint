using System.Diagnostics;
using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace VirtualDuplexPrinter;

/// <summary>Owns only generated files. The source document is never deleted.</summary>
internal sealed class PdfPrintJob : IDisposable
{
    public string DirectoryPath { get; }
    public string OddPath => Path.Combine(DirectoryPath, "odd.pdf");
    public string EvenPath => Path.Combine(DirectoryPath, "even.pdf");
    public int PageCount { get; private set; }

    private PdfPrintJob() => DirectoryPath = Directory.CreateTempSubdirectory("DuplexPrint-").FullName;

    public static PdfPrintJob Prepare(string sourcePath, bool reverseBacks) => Prepare(sourcePath,
        new PrintProfile { BackOrder = reverseBacks ? PageOrder.Descending : PageOrder.Ascending });

    public static PdfPrintJob Prepare(string sourcePath, PrintProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kérlek PDF-fájlt válassz.");
        var job = new PdfPrintJob();
        try
        {
            // Both passes use the same locked snapshot, prepared before printing starts.
            using var stream = new FileStream(Path.GetFullPath(sourcePath), FileMode.Open, FileAccess.Read, FileShare.Read);
            using var source = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            if (source.PageCount == 0)
                throw new InvalidDataException("A PDF nem tartalmaz nyomtatható oldalt.");
            // Import resets encryption state, but the original permission flags remain.
            // Never bypass a document's print/quality restrictions by extracting its pages.
            if (!source.SecuritySettings.PermitPrint || !source.SecuritySettings.PermitFullQualityPrint)
                throw new InvalidDataException("A PDF biztonsági beállításai korlátozzák a nyomtatást. Használj nyomtatásra jogosító példányt.");
            job.PageCount = source.PageCount;
            var plan = PagePlan.Create(source.PageCount, profile);
            using var odds = new PdfDocument();
            foreach (int number in plan.Fronts) odds.AddPage(source.Pages[number - 1]);
            odds.Save(job.OddPath);
            if (source.PageCount > 1)
            {
                using var evens = new PdfDocument();
                foreach (int? number in plan.Backs)
                {
                    if (number is not null)
                    {
                        var page = evens.AddPage(source.Pages[number.Value - 1]);
                        page.Rotate = (page.Rotate + profile.BackRotationDegrees) % 360;
                    }
                    else
                    {
                        // Match the unpaired front's paper geometry, including non-A4 pages.
                        var front = source.Pages[source.PageCount - 1];
                        var blank = evens.AddPage();
                        blank.MediaBox = front.MediaBox;
                        blank.CropBox = front.CropBox;
                        blank.Rotate = (front.Rotate + profile.BackRotationDegrees) % 360;
                    }
                }
                evens.Save(job.EvenPath);
            }
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(DirectoryPath, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Temporary PDF cleanup failed: {ex.Message}");
        }
    }
}
