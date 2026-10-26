using Microsoft.Win32;
using System.ComponentModel;
using System.Drawing.Printing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace VirtualDuplexPrinter;

public partial class MainWindow : Window
{
    private string pdfPath = string.Empty;
    private int step;
    private bool busy;
    private bool loadingPrinters;
    private PdfPrintJob? job;
    private string? jobPrinter;
    private CancellationTokenSource? printCancellation;
    private readonly Action<string> showPrintError;
    private readonly Func<string, string, CancellationToken, Task> printPass;
    private readonly Func<string, bool> validatePrinter;
    private readonly SettingsStore settingsStore;
    private AppSettings settings;
    private PrintProfile? jobProfile;
    private string? settingsWarning;
    public bool HasJob => !string.IsNullOrEmpty(pdfPath);
    internal bool WasSubmitted => step == 2;

    public MainWindow() : this(null) { }

    internal MainWindow(Action<string>? reportPrintError,
        Func<string, string, CancellationToken, Task>? submitPass = null,
        Func<string, bool>? isPrinterValid = null, SettingsStore? store = null)
    {
        InitializeComponent();
        showPrintError = reportPrintError ?? (message => MessageBox.Show(this, message, "DuplexPrint", MessageBoxButton.OK, MessageBoxImage.Warning));
        printPass = submitPass ?? WindowsPdfPrinter.PrintAsync;
        validatePrinter = isPrinterValid ?? (printer => !AppPaths.IsCapturePrinter(printer) && new PrinterSettings { PrinterName = printer }.IsValid);
        settingsStore = store ?? new SettingsStore(AppPaths.SettingsFile);
        settings = settingsStore.Load(out settingsWarning);
        txtPrinterStatus.Text = settingsWarning ?? "";
        UpdateProfile();
        SourceInitialized += (_, _) =>
        {
            // Square window corners on Windows 11; earlier Windows versions ignore this attribute.
            int preference = 1;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref preference, sizeof(int));
        };
        Loaded += async (_, _) => await LoadPrintersAsync();
        Closing += OnClosing;
        Closed += (_, _) => job?.Dispose();
    }

    public void SetInbox(string path)
    {
        string relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string displayPath = path.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? "%LOCALAPPDATA%" + path[local.Length..] : path;
        txtInbox.Text = "Figyelt mappa: " + (relative.StartsWith("..", StringComparison.Ordinal) ? displayPath : ".\\" + relative);
        txtInbox.ToolTip = path;
    }

    private async Task LoadPrintersAsync()
    {
        if (loadingPrinters || busy || jobPrinter is not null) return;
        loadingPrinters = true;
        SetControls();
        try
        {
            var selected = PrinterComboBox.SelectedItem as string ?? settings.PrinterName;
            var printers = await Task.Run(() => PrinterSettings.InstalledPrinters.Cast<string>().Where(p => !AppPaths.IsCapturePrinter(p)).ToList());
            var defaultPrinter = await Task.Run(() => new PrinterSettings().PrinterName);
            PrinterComboBox.ItemsSource = printers;
            PrinterComboBox.SelectedItem = printers.Contains(selected ?? "") ? selected
                : printers.Contains(defaultPrinter) ? defaultPrinter : printers.FirstOrDefault();
            txtPrinterStatus.Text = (settingsWarning is null ? "" : settingsWarning + "\n") +
                (printers.Count == 0 ? "Nincs telepített nyomtató. Telepíts egyet, majd kattints a Frissítés gombra." : "");
        }
        catch (Exception ex)
        {
            txtPrinterStatus.Text = "A nyomtatólista nem olvasható: " + ex.Message;
        }
        finally { loadingPrinters = false; SetControls(); }
    }

    public void StartPrintJob(string pdfFile)
    {
        if (HasJob || busy)
            throw new InvalidOperationException("Már van megnyitott feladat.");
        pdfPath = Path.GetFullPath(pdfFile);
        if (!File.Exists(pdfPath) || !string.Equals(Path.GetExtension(pdfPath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            pdfPath = "";
            throw new FileNotFoundException("Válassz egy létező PDF-fájlt.", pdfFile);
        }
        txtDocument.Text = Path.GetFileName(pdfPath);
        txtDocument.ToolTip = pdfPath;
        txtStage.Text = "Az első adag készen áll.";
        txtInstructions.Text = "A páratlan oldalak kerülnek először a nyomtatóba. A második adag előtt lesz időd visszahelyezni a papírt.";
        btnOpen.IsEnabled = false;
        btnNext.IsEnabled = !loadingPrinters;
        step = 0;
        progressBar.Value = 0;
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF dokumentum (*.pdf)|*.pdf", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            // Watcher events can arrive while the native dialog runs its nested message loop.
            if (HasJob)
                MessageBox.Show(this, "Közben feladat érkezett a figyelt mappából. Fejezd be, majd nyisd meg a kiválasztott PDF-et.", "DuplexPrint");
            else
            {
                try { StartPrintJob(dialog.FileName); }
                catch (Exception ex) { showPrintError(ex.Message); }
            }
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await LoadPrintersAsync();

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (busy || loadingPrinters || !HasJob)
            return;
        if (step == 2)
        {
            Close();
            return;
        }
        busy = true;
        using var cancellation = new CancellationTokenSource();
        printCancellation = cancellation;
        SetControls();
        string previousInstructions = txtInstructions.Text;
        try
        {
            NativePdfRenderer.EnsureAvailable();
            string printer = jobPrinter ?? PrinterComboBox.SelectedItem as string
                ?? throw new InvalidOperationException("Nincs kiválasztva nyomtató. Telepíts vagy válassz egyet, majd frissítsd a listát.");
            bool valid = await Task.Run(() => validatePrinter(printer));
            if (!valid)
                throw new InvalidOperationException("A kiválasztott nyomtató már nem érhető el. Ellenőrizd a Windows nyomtatóbeállításait.");
            if (job is null)
            {
                jobProfile = settings.Profile;
                txtInstructions.Text = "A PDF ellenőrzése és előkészítése...";
                job = await Task.Run(() => PdfPrintJob.Prepare(pdfPath, jobProfile));
                jobPrinter = printer;
                settings = settings with { PrinterName = printer };
                try { if (settingsWarning is null) settingsStore.Save(settings); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { txtPrinterStatus.Text = "A beállítások nem menthetők: " + ex.Message; }
            }
            txtInstructions.Text = step == 0 ? "Most a páratlan oldalakat küldjük a nyomtatónak..." : "Most a páros oldalakat küldjük a nyomtatónak...";
            cancellation.Token.ThrowIfCancellationRequested();
            await printPass(printer, step == 0 ? job.OddPath : job.EvenPath, cancellation.Token);
            // A completed worker confirms submission, not that paper has left the printer.
            if (step == 0 && job.PageCount > 1)
            {
                step = 1;
                progressBar.Value = 50;
                txtTitle.Text = "Most jöhet a papírfordítás.";
                txtStage.Text = "Első adag elküldve. Várd meg a lapokat!";
                txtInstructions.Text = jobProfile!.ReinsertionInstructions;
                var plan = PagePlan.Create(job.PageCount, jobProfile);
                txtOrder.Text = "Második adag: " + string.Join(", ", plan.Backs.Take(12).Select(p => p?.ToString() ?? "üres")) + (plan.Backs.Length > 12 ? ", …" : "");
                txtPaper.Text = "2 / 4";
                txtDiagram.Text = "HÁTLAPOK";
                btnNext.Content = "Papír visszahelyezve · Második adag  →";
                navFirst.Foreground = new SolidColorBrush(Color.FromRgb(156, 175, 175));
                navReload.Foreground = Brushes.White;
            }
            else
            {
                step = 2;
                progressBar.Value = 100;
                txtInstructions.Text = "A nyomtatási feladat elküldve. A tényleges állapotot a Windows nyomtatási sorában ellenőrizheted.";
                txtTitle.Text = job.PageCount == 1 ? "Az oldal elküldve." : "Mindkét oldal elküldve.";
                txtStage.Text = job.PageCount == 1 ? "Egy oldal — nincs szükség fordításra." : "A második adag is a nyomtatónál van.";
                txtPaper.Text = "✓";
                txtDiagram.Text = "ELKÜLDVE";
                navReload.Foreground = new SolidColorBrush(Color.FromRgb(156, 175, 175));
                navFinish.Foreground = Brushes.White;
                btnNext.Content = "Bezárás";
                job.Dispose();
                job = null;
            }
        }
        catch (Exception ex)
        {
            if (step == 0)
            {
                job?.Dispose();
                job = null;
                jobPrinter = null;
            }
            txtInstructions.Text = previousInstructions;
            showPrintError("A nyomtatás nem fejeződött be.\n\n" + ex.Message +
                "\n\nÚjrapróbálás előtt ellenőrizd a Windows nyomtatási sorát és a már kinyomtatott lapokat.");
        }
        finally
        {
            busy = false;
            printCancellation = null;
            SetControls();
        }
    }

    private void SetControls()
    {
        btnNext.IsEnabled = !busy && !loadingPrinters && HasJob;
        progressBar.IsIndeterminate = busy;
        PrinterComboBox.IsEnabled = !busy && !loadingPrinters && jobPrinter is null;
        btnRefresh.IsEnabled = !busy && !loadingPrinters && jobPrinter is null;
        btnSettings.IsEnabled = !busy && jobPrinter is null;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Stop the child process first; only then may generated files be deleted.
        if (busy)
        {
            e.Cancel = true;
            printCancellation?.Cancel();
            txtInstructions.Text = "A feladat megszakítása folyamatban...";
            return;
        }
        if (step == 1 && MessageBox.Show(this,
            "A páratlan oldalak már elküldve. Biztosan megszakítod a páros oldalak nyomtatását?",
            "DuplexPrint", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private void UpdateProfile()
    {
        txtProfile.Text = "Profil: " + settings.Profile.Name;
        txtOrder.Text = "Előlapok: " + (settings.Profile.FrontOrder == PageOrder.Ascending ? "1, 3, 5, …" : "…, 5, 3, 1") +
            "   |   Hátlapok: " + (settings.Profile.BackOrder == PageOrder.Ascending ? "2, 4, 6, …" : "…, 6, 4, 2");
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var editor = new SettingsWindow(settingsStore, settings with { PrinterName = PrinterComboBox.SelectedItem as string ?? settings.PrinterName }) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            settings = editor.Result;
            settingsWarning = null;
            txtPrinterStatus.Text = "";
            UpdateProfile();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !HasJob && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (HasJob || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        try
        {
            if (files.Length != 1) throw new InvalidOperationException("Egyszerre egy PDF-et húzz az ablakba.");
            StartPrintJob(files[0]);
        }
        catch (Exception ex) { showPrintError(ex.Message); }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
