using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using VirtualDuplexPrinter;

internal static class Program
{
    private static int passed;
    private static int failed;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "DuplexPrint tests árvíztűrő " + Guid.NewGuid().ToString("N"));

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--capture-ui") return UiCapture.Run(Path.GetFullPath(args[1]));
        // A harmless test child process. This never talks to a printer.
        if (args.Length > 0 && args[0] == "--fake-child")
        {
            if (args[1] == "wait") Thread.Sleep(30_000);
            return int.Parse(args[1]);
        }
        if (args.Length > 0 && args[0] == "--print-pass")
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "received.json"), JsonSerializer.Serialize(args));
            return 0;
        }
        Directory.CreateDirectory(Root);
        try
        {
            ProfileWorkflowTests.Run(Root, Check);
            NativePrintingTests.Run(Root, Check);
            for (int pages = 1; pages <= 9; pages++)
                foreach (bool reverse in new[] { false, true })
                {
                    int count = pages;
                    Check($"{count} pages, reverse={reverse}: ordering, blank geometry, cleanup", () => VerifyPages(count, reverse));
                }
            Check("Two simultaneous jobs have distinct directories", () =>
            {
                var source = MakePdf("parallel.pdf", 3);
                using var a = PdfPrintJob.Prepare(source, false);
                using var b = PdfPrintJob.Prepare(source, true);
                Assert(a.DirectoryPath != b.DirectoryPath, "Temporary directory collision");
            });
            Check("Source may change/delete after preparation; passes remain a snapshot", () =>
            {
                var source = MakePdf("snapshot.pdf", 4);
                using var job = PdfPrintJob.Prepare(source, false);
                File.Delete(source);
                Assert(ReadMarkers(job.OddPath).SequenceEqual(new[] { 1, 3 }), "Front snapshot changed");
                Assert(ReadMarkers(job.EvenPath).SequenceEqual(new[] { 2, 4 }), "Back snapshot changed");
            });
            Check("Missing, empty, invalid, zero-page and non-PDF files rejected", () =>
            {
                Throws(() => PdfPrintJob.Prepare(Path.Combine(Root, "missing.pdf"), false));
                var empty = Path.Combine(Root, "empty.pdf");
                File.WriteAllBytes(empty, []);
                Throws(() => PdfPrintJob.Prepare(empty, false));
                File.WriteAllText(empty, "This is not a PDF.");
                Throws(() => PdfPrintJob.Prepare(empty, false));
                File.WriteAllText(empty, "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\ntrailer\n<< /Root 1 0 R /Size 3 >>\n%%EOF");
                Throws(() => PdfPrintJob.Prepare(empty, false));
                Throws(() => PdfPrintJob.Prepare("file.txt", false));
                Throws(() => PdfPrintJob.Prepare("", false));
            });
            Check("Locked source rejected without deleting it", () =>
            {
                var source = MakePdf("locked.pdf", 2);
                using var held = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Throws(() => PdfPrintJob.Prepare(source, false));
                Assert(File.Exists(source), "Locked source removed");
            });
            Check("Password-required and print-restricted documents rejected", () =>
            {
                var source = Path.Combine(Root, "encrypted.pdf");
                using (var doc = new PdfDocument())
                {
                    doc.AddPage();
                    doc.SecuritySettings.UserPassword = "test-password";
                    doc.SecuritySettings.OwnerPassword = "test-owner-password";
                    doc.Save(source);
                }
                Throws(() => PdfPrintJob.Prepare(source, false));
                using (var doc = new PdfDocument())
                {
                    doc.AddPage();
                    doc.SecuritySettings.OwnerPassword = "owner-only-test";
                    doc.SecuritySettings.PermitPrint = false;
                    doc.Save(source);
                }
                Throws(() => PdfPrintJob.Prepare(source, false));
            });
            Check("UI opens with Next disabled and accepts a PDF job", () =>
            {
                var window = new MainWindow();
                var next = (Button)window.FindName("btnNext");
                Assert(!next.IsEnabled, "Next enabled before a job");
                window.StartPrintJob(MakePdf("ui.pdf", 1));
                Assert(window.HasJob && next.IsEnabled, "Job was not accepted");
                Throws(() => window.StartPrintJob("other.pdf"));
                window.Close();
            });
            Check("Bundled renderer path is anchored to the application directory", () =>
                Assert(NativePdfRenderer.ExecutablePath == Path.Combine(AppContext.BaseDirectory, "DuplexPrint.PdfWorker.exe"), "Incorrect renderer path"));
            Check("UI missing-printer error does not advance or disable retry", () =>
            {
                string? reported = null;
                var window = new MainWindow(message => reported = message);
                window.StartPrintJob(MakePdf("missing printer.pdf", 3));
                var next = (Button)window.FindName("btnNext");
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(reported?.Contains("Nincs kiválasztva nyomtató") == true, "Missing selection was not reported");
                Assert(((ProgressBar)window.FindName("progressBar")).Value == 0 && next.IsEnabled, "Failure advanced or disabled the workflow");
                window.Close();
            });
            Check("Safe process arguments preserve spaces, quotes, Unicode and metacharacters", VerifyArguments);
            Check("Process failure is surfaced", () =>
                Throws<IOException>(() => WorkerProcess.RunAsync(Child("7"), TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()));
            Check("Process timeout is bounded and child is terminated", () =>
                Throws<TimeoutException>(() => WorkerProcess.RunAsync(Child("wait"), TimeSpan.FromMilliseconds(300)).GetAwaiter().GetResult()));
            Check("Unreadable executable launch is surfaced", () =>
                Throws(() => WorkerProcess.RunAsync(new ProcessStartInfo(Path.Combine(Root, "absent.exe")), TimeSpan.FromSeconds(1)).GetAwaiter().GetResult()));
            Check("Inbox readiness waits for writer then succeeds", () =>
            {
                var path = Path.Combine(Root, "copy.pdf");
                using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                writer.WriteByte(1);
                var ready = InboxFiles.WaitUntilReadyAsync(path, TimeSpan.FromSeconds(5), CancellationToken.None);
                Assert(!ready.IsCompleted, "Accepted an exclusively locked writer");
                writer.Dispose();
                ready.GetAwaiter().GetResult();
            });
            Check("Inbox missing/deleted file times out", () =>
                Throws<TimeoutException>(() => InboxFiles.WaitUntilReadyAsync(Path.Combine(Root, "gone.pdf"), TimeSpan.FromMilliseconds(100), CancellationToken.None).GetAwaiter().GetResult()));
            Check("Inbox shutdown cancels readiness waiting", () =>
            {
                using var cancel = new CancellationTokenSource();
                cancel.Cancel();
                Throws<OperationCanceledException>(() => InboxFiles.WaitUntilReadyAsync("missing.pdf", TimeSpan.FromSeconds(10), cancel.Token).GetAwaiter().GetResult());
            });
            Console.WriteLine($"{passed} passed; {failed} failed. No print jobs were sent.");
            return failed == 0 ? 0 : 1;
        }
        finally { Directory.Delete(Root, true); }
    }

    private static void VerifyPages(int count, bool reverse)
    {
        var source = MakePdf($"{count}-{reverse}.pdf", count);
        string temp;
        using (var job = PdfPrintJob.Prepare(source, reverse))
        {
            temp = job.DirectoryPath;
            Assert(job.PageCount == count, "Wrong source count");
            Assert(ReadMarkers(job.OddPath).SequenceEqual(Enumerable.Range(1, count).Where(i => i % 2 == 1)), "Wrong fronts");
            if (count == 1)
                Assert(!File.Exists(job.EvenPath), "One-page job has an empty second pass");
            else
            {
                var expected = Enumerable.Range(1, count).Where(i => i % 2 == 0).ToList();
                if (count % 2 != 0) expected.Add(0);
                if (reverse) expected.Reverse();
                Assert(ReadMarkers(job.EvenPath).SequenceEqual(expected), "Wrong backs or blank placement");
                if (count % 2 != 0)
                {
                    using var evens = PdfReader.Open(job.EvenPath, PdfDocumentOpenMode.Import);
                    var blank = evens.Pages[reverse ? 0 : evens.PageCount - 1];
                    Assert(Math.Abs(blank.MediaBox.Width - (400 + count)) < .01, "Blank width mismatch");
                    Assert(Math.Abs(blank.MediaBox.Height - (600 + count)) < .01, "Blank height mismatch");
                    Assert(blank.Rotate == 90, "Blank rotation mismatch");
                }
            }
        }
        Assert(!Directory.Exists(temp), "Generated PDFs were not removed");
        Assert(File.Exists(source), "Original PDF was deleted");
    }

    private static string MakePdf(string name, int count)
    {
        var path = Path.Combine(Root, name);
        using var doc = new PdfDocument();
        for (int i = 1; i <= count; i++)
        {
            var page = doc.AddPage();
            page.MediaBox = new PdfRectangle(new PdfSharp.Drawing.XPoint(0, 0), new PdfSharp.Drawing.XPoint(400 + i, 600 + i));
            page.Rotate = i % 2 != 0 ? 90 : 0;
            using var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(PdfSharp.Drawing.XPens.Black, 10, 10, i * 10, 20);
        }
        doc.Save(path);
        return path;
    }

    private static int[] ReadMarkers(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfPage>()
            .Select(p => p.Contents.Elements.Count == 0 ? 0 : (int)Math.Round(p.MediaBox.Width) - 400).ToArray();
    }

    private static ProcessStartInfo Child(string result)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        info.ArgumentList.Add("--fake-child");
        info.ArgumentList.Add(result);
        return info;
    }

    private static void VerifyArguments()
    {
        string fakeDir = Path.Combine(Root, "fake reader ő & spaces");
        Directory.CreateDirectory(fakeDir);
        string fake = Path.Combine(fakeDir, "HarmlessReader.exe");
        File.Copy(Environment.ProcessPath!, fake);
        // The apphost still resolves the original test assembly name in its own directory.
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
            if (Path.GetExtension(file) != ".exe")
                File.Copy(file, Path.Combine(fakeDir, Path.GetFileName(file)), true);
        string printer = "Test printer ő & \"quoted\"";
        string pdf = MakePdf("árvíztűrő & sample.pdf", 2);
        var info = WindowsPdfPrinter.CreateStartInfo(printer, pdf);
        info.FileName = fake;
        Assert(!info.UseShellExecute && string.IsNullOrEmpty(info.Arguments), "Unsafe command string");
        WorkerProcess.RunAsync(info, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        var received = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(fakeDir, "received.json")))!;
        Assert(received.SequenceEqual(new[] { "--print-pass", printer, pdf }), "Arguments changed across OS process boundary");
    }

    private static void Check(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static Exception Throws(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex; }
        throw new InvalidOperationException("Expected an exception");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        var exception = Throws(action);
        Assert(exception is T, $"Expected {typeof(T).Name}, got {exception.GetType().Name}");
    }
}
