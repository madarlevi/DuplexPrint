using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using VirtualDuplexPrinter;

internal static class ProfileWorkflowTests
{
    public static void Run(string root, Action<string, Action> check)
    {
        check("HP defaults preserve the original ascending fronts/backs and instructions", () =>
        {
            var profile = new PrintProfile();
            var plan = PagePlan.Create(5, profile);
            Assert(plan.Fronts.SequenceEqual([1, 3, 5]) && plan.Backs.SequenceEqual(new int?[] { 2, 4, null }), "Original order changed");
            Assert(profile.BackRotationDegrees == 0 && profile.ReinsertionInstructions.Contains("az írás felül") && profile.ReinsertionInstructions.Contains("hátulja felől"), "Original instruction changed");
        });
        check("All profile orders, 1–12 pages: markers, blank positions and 180° backs", () =>
        {
            foreach (var front in Enum.GetValues<PageOrder>())
            foreach (var back in Enum.GetValues<PageOrder>())
            for (int count = 1; count <= 12; count++)
            {
                string source = MakePdf(root, count);
                var profile = new PrintProfile { FrontOrder = front, BackOrder = back, BackRotationDegrees = 180 };
                using var job = PdfPrintJob.Prepare(source, profile);
                var expectedFront = Enumerable.Range(1, count).Where(p => p % 2 != 0);
                if (front == PageOrder.Descending) expectedFront = expectedFront.Reverse();
                using var odds = PdfReader.Open(job.OddPath, PdfDocumentOpenMode.Import);
                Assert(Markers(odds).SequenceEqual(expectedFront), "Wrong front order");
                if (count == 1) { Assert(!File.Exists(job.EvenPath), "Single page has backs"); continue; }
                IEnumerable<int> expectedBack = Enumerable.Range(1, (count + 1) / 2).Select(i => i * 2 <= count ? i * 2 : 0);
                if (back == PageOrder.Descending) expectedBack = expectedBack.Reverse();
                using var evens = PdfReader.Open(job.EvenPath, PdfDocumentOpenMode.Import);
                Assert(Markers(evens).SequenceEqual(expectedBack), "Wrong back/blank order");
                Assert(evens.Pages.Cast<PdfPage>().All(p => p.Rotate == 180), "Back rotation not applied to every page, including blank");
            }
        });
        check("Settings round-trip uses portable JSON values and retains a backup", () =>
        {
            var store = new SettingsStore(Path.Combine(root, "beállítások spaces", "settings.json"));
            Assert(store.Load(out var warning) == new AppSettings() && warning is null && !File.Exists(store.FilePath), "Defaults created a file");
            var settings = new AppSettings { PrinterName = "HP ő & printer", Profile = new PrintProfile { BackOrder = PageOrder.Descending } };
            store.Save(settings);
            string first = File.ReadAllText(store.FilePath);
            Assert(first.Contains("descending") && store.Load(out warning) == settings && warning is null, "Settings changed");
            store.Save(settings with { PrinterName = "Updated" });
            Assert(File.ReadAllText(store.FilePath + ".bak") == first, "Prior file backup lost");
            Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp").Any(), "Temporary settings remain");
        });
        check("Malformed/unknown/null/duplicate settings are rejected without overwriting", () =>
        {
            var store = new SettingsStore(Path.Combine(root, "invalid-settings.json"));
            string valid = JsonSerializer.Serialize(new AppSettings(), SettingsStore.JsonOptions);
            foreach (string text in new[] { "{", "{}", valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"),
                valid.Replace("\"printerName\": \"\"", "\"printerName\": null"), valid.Replace("\"ascending\"", "7"),
                valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"),
                valid.Replace("\"printerModel\": \"HP LaserJet 4250tn\"", "\"printerModel\": null"),
                valid.Replace("\"backRotationDegrees\": 0", "\"backRotationDegrees\": 90"), valid.Replace("\"schemaVersion\": 1", "\"extra\": true, \"schemaVersion\": 1") })
            {
                File.WriteAllText(store.FilePath, text);
                _ = store.Load(out string? warning);
                Assert(warning is not null && File.ReadAllText(store.FilePath) == text, "Malformed settings accepted or overwritten");
            }
            File.WriteAllText(store.FilePath, new string(' ', 131073));
            _ = store.Load(out string? tooLarge);
            Assert(tooLarge is not null, "Oversized settings accepted");
        });
        check("Profile import validates required fields, safe ID and values", () =>
        {
            string path = Path.Combine(root, "profile.json");
            var profile = new PrintProfile { BackOrder = PageOrder.Descending, BackRotationDegrees = 180 };
            File.WriteAllText(path, JsonSerializer.Serialize(profile, SettingsStore.JsonOptions));
            Assert(SettingsStore.ImportProfile(path) == profile, "Imported values changed");
            foreach (string text in new[] { "{}", "null", JsonSerializer.Serialize(profile with { Id = "../../file" }, SettingsStore.JsonOptions),
                JsonSerializer.Serialize(profile with { Id = "unsafe-newline\n" }, SettingsStore.JsonOptions) })
            {
                File.WriteAllText(path, text);
                ExpectFailure(() => SettingsStore.ImportProfile(path));
            }
        });
        check("UI waits for the second click, ignores double clicks and locks printer/profile", () =>
        {
            var calls = new List<int[]>();
            var firstPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string? error = null;
            var window = MakeWindow(root, message => error = message, (_, path, _) =>
            {
                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                calls.Add(Markers(pdf));
                return calls.Count == 1 ? firstPass.Task : Task.CompletedTask;
            });
            var next = (Button)window.FindName("btnNext");
            window.StartPrintJob(MakePdf(root, 5));
            Click(next);
            PumpUntil(() => calls.Count == 1);
            Click(next);
            Assert(calls.Count == 1 && !next.IsEnabled, "Double click submitted another pass");
            firstPass.SetResult();
            PumpUntil(() => next.IsEnabled);
            Assert(calls.Count == 1 && ((ProgressBar)window.FindName("progressBar")).Value == 50, "Backs printed without user confirmation");
            Assert(!((Button)window.FindName("btnSettings")).IsEnabled && !((ComboBox)window.FindName("PrinterComboBox")).IsEnabled, "Mid-job settings not locked");
            Assert(((TextBlock)window.FindName("txtInstructions")).Text == new PrintProfile().ReinsertionInstructions, "Wrong paper reload instructions");
            Click(next);
            PumpUntil(() => window.WasSubmitted);
            Assert(error is null && calls.Count == 2 && calls[0].SequenceEqual([1, 3, 5]) && calls[1].SequenceEqual([2, 4, 0]), "Two-click job changed order");
            window.Close();
        });
        check("Second-pass failure preserves the reload step; retry sends only backs", () =>
        {
            var calls = new List<int[]>();
            string? error = null;
            var window = MakeWindow(root, message => error = message, (_, path, _) =>
            {
                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                calls.Add(Markers(pdf));
                return calls.Count == 2 ? Task.FromException(new IOException("Controlled test failure")) : Task.CompletedTask;
            });
            var next = (Button)window.FindName("btnNext");
            window.StartPrintJob(MakePdf(root, 4));
            Click(next); PumpUntil(() => calls.Count == 1 && next.IsEnabled);
            Click(next); PumpUntil(() => error is not null && next.IsEnabled);
            Assert(!window.WasSubmitted && ((ProgressBar)window.FindName("progressBar")).Value == 50, "Failed backs advanced the step");
            Click(next); PumpUntil(() => window.WasSubmitted);
            Assert(calls.Count == 3 && calls[1].SequenceEqual([2, 4]) && calls[2].SequenceEqual([2, 4]), "Retry reprinted fronts");
            window.Close();
        });
        check("A malformed local settings file survives job submission", () =>
        {
            var store = new SettingsStore(Path.Combine(root, "untouched-settings.json"));
            File.WriteAllText(store.FilePath, "broken JSON");
            var window = MakeWindow(root, _ => { }, (_, _, _) => Task.CompletedTask, store);
            window.StartPrintJob(MakePdf(root, 1));
            Click((Button)window.FindName("btnNext")); PumpUntil(() => window.WasSubmitted);
            Assert(File.ReadAllText(store.FilePath) == "broken JSON", "Job silently overwrote invalid preferences");
            window.Close();
        });
        check("Virtual output queue cannot recursively feed itself", () =>
        {
            Assert(AppPaths.IsCapturePrinter("DuplexPrint") && AppPaths.IsCapturePrinter("duplexprint"), "Capture queue guard missing");
            ExpectFailure(() => WindowsPdfPrinter.PrintInWorkerAsync("DuplexPrint", "unused.pdf").GetAwaiter().GetResult());
        });
        check("Captured inbox archives only its own PDFs, retaining original manual files", () =>
        {
            string inbox = Path.Combine(root, "capture", "Inbox");
            Directory.CreateDirectory(inbox);
            string captured = Path.Combine(inbox, "captured.pdf");
            File.WriteAllText(captured, "test data");
            string original = MakePdf(root, 1);
            ExpectFailure(() => CapturedInbox.Archive(inbox, original, true));
            Assert(File.Exists(original), "Manual PDF was moved");
            CapturedInbox.Archive(inbox, captured, true);
            Assert(!File.Exists(captured) && Directory.GetFiles(Path.Combine(root, "capture", "History", "Submitted")).Length == 1, "Captured PDF not archived");
        });
    }

    private static MainWindow MakeWindow(string root, Action<string> error, Func<string, string, CancellationToken, Task> print, SettingsStore? store = null)
    {
        var window = new MainWindow(error, print, _ => true, store ?? new SettingsStore(Path.Combine(root, Guid.NewGuid() + "settings.json")));
        var printers = (ComboBox)window.FindName("PrinterComboBox");
        printers.ItemsSource = new[] { "Harmless mocked printer" };
        printers.SelectedIndex = 0;
        return window;
    }
    private static void Click(Button button)
    {
        // Match WPF's real routed input dispatch, including its UI synchronization context.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
    internal static void PumpUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("UI test did not complete");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }
    private static string MakePdf(string root, int count)
    {
        string path = Path.Combine(root, Guid.NewGuid() + " árvíztűrő sample.pdf");
        using var pdf = new PdfDocument();
        for (int i = 1; i <= count; i++)
        {
            var page = pdf.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(400 + i);
            using var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(PdfSharp.Drawing.XPens.Black, 10, 10, 20, 20);
        }
        pdf.Save(path);
        return path;
    }
    private static int[] Markers(PdfDocument pdf) => pdf.Pages.Cast<PdfPage>().Select(p => p.Contents.Elements.Count == 0 ? 0 : (int)Math.Round(p.MediaBox.Width) - 400).ToArray();
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch { return; }
        throw new InvalidOperationException("Expected rejection");
    }
}
